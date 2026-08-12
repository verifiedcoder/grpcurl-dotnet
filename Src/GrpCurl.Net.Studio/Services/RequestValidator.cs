using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IRequestValidator" />: resolves the method's input descriptor via Core's
///     <see cref="DescriptorSourceFactory" /> (cached per connection+method — the descriptor outlives
///     the channel) and probes <c>CreateMessageFromJson</c>. JSON syntax errors surface a line/column;
///     semantic errors surface a message only. Advisory: an unresolvable schema yields no problems.
/// </summary>
internal sealed class RequestValidator(IInvocationService invocation, ITlsProfileResolver? tlsResolver = null) : IRequestValidator, IDisposable
{
    /// <summary>
    ///     How long disposal waits for an in-flight operation to leave the critical section before
    ///     giving up on draining. Bounded on purpose: shutdown must not hang on a stuck operation.
    /// </summary>
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, MessageDescriptor> _inputCache = [];

    private int _disposed;

    public async Task<IReadOnlyList<ValidationProblem>> ValidateAsync(
        SavedConnection connection, string methodSymbol, string requestJson, bool allowUnknownFields, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        MessageDescriptor input;

        try
        {
            input = await ResolveInputAsync(connection, methodSymbol, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return []; // schema unavailable → skip advisory validation; the server stays the authority
        }

        try
        {
            _ = invocation.CreateMessageFromJson(input, requestJson, allowUnknownFields);
            return [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return [new ValidationProblem(Clean(ex.Message), ToPosition(ex.LineNumber), ToPosition(ex.BytePositionInLine))];
        }
        catch (Exception ex)
        {
            return [new ValidationProblem(ex.Message, Line: null, Column: null)];
        }
    }

    private async Task<MessageDescriptor> ResolveInputAsync(SavedConnection connection, string methodSymbol, CancellationToken cancellationToken)
    {
        var key = $"{connection.Id}|{methodSymbol}";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inputCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var (profile, password) = tlsResolver is null
                ? default
                : await tlsResolver.ResolveAsync(connection, cancellationToken).ConfigureAwait(false);
            var options = ConnectionChannelMapper.ToChannelOptions(connection, maxMessageSize: null, profile, password);
            var reflectionMetadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
            var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(connection);

            await using var session = await DescriptorSourceFactory.CreateAsync(
                connection.Address, protosets, protos, imports,
                channelOptions: options,
                reflectionMetadata: reflectionMetadata,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var symbol = methodSymbol.Replace('/', '.');

            if (await session.Source.FindSymbolAsync(symbol, cancellationToken).ConfigureAwait(false) is not MethodDescriptor method)
            {
                throw new InvalidOperationException($"Method '{methodSymbol}' was not found.");
            }

            _inputCache[key] = method.InputType;
            return method.InputType;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    // System.Text.Json reports 0-based line/byte positions; surface them 1-based.
    private static int? ToPosition(long? value) => value is { } v ? (int)v + 1 : null;

    // Trim the " LineNumber: x | BytePositionInLine: y." suffix STJ appends — position is shown separately.
    private static string Clean(string message)
    {
        var marker = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return marker >= 0 ? message[..marker] : message;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    /// <summary>
    ///     Releases the validation gate. Idempotent and non-throwing: this is a container-owned
    ///     singleton, so it runs from <c>ServiceProvider.Dispose()</c> during shutdown, where a throw
    ///     aborts disposal of every singleton after it (PRD-005). The injected invocation service and
    ///     TLS resolver are container-owned too and are deliberately not disposed here.
    /// </summary>
    public void Dispose()
    {
        // Atomic, so two concurrent disposals cannot both pass the check and race the teardown.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Drain before destroying the gate: disposing a SemaphoreSlim while an operation owns it (or is
        // queued on it) is undefined, and the previous version simply assumed no work was live
        // (PRD-005 review, finding 3). Bounded, so shutdown cannot hang.
        var drained = _gate.Wait(DisposeDrainTimeout);

        // Disposed while still held, so nothing can queue behind it; a later caller gets
        // ObjectDisposedException. If the drain timed out the gate is left alone rather than destroyed
        // under an owner.
        if (drained)
        {
            _gate.Dispose();
        }
    }
}
