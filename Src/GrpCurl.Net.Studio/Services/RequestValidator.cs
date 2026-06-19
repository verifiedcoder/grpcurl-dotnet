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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, MessageDescriptor> _inputCache = [];

    public async Task<IReadOnlyList<ValidationProblem>> ValidateAsync(
        SavedConnection connection, string methodSymbol, string requestJson, bool allowUnknownFields, CancellationToken cancellationToken)
    {
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

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
