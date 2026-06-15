using System.Diagnostics;
using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IInvocationRunner" />: resolves the business channel + method descriptor
///     for the connection via Core's <see cref="DescriptorSourceFactory" /> (reflection + the RPC
///     share one channel, as the CLI does), builds the request/metadata/deadline, runs the call
///     through <see cref="IInvocationService" />, and maps the outcome to model types. Stateless per
///     invoke (the session/channel is disposed after the call); per-connection channel caching is a
///     later optimisation. User cancellation propagates; resolution/parse failures become a failed
///     <see cref="InvocationResultModel" />.
/// </summary>
internal sealed partial class InvocationRunner(IInvocationService invocation) : IInvocationRunner
{
    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvVarPattern();

    public async Task<InvocationResultModel> InvokeUnaryAsync(InvocationRequestModel request, CancellationToken cancellationToken)
    {
        var connection = request.Connection;
        var options = ConnectionChannelMapper.ToChannelOptions(connection, ParseSizeOrNull(request.MaxMessageSize));
        var reflectionMetadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var deadline = ParseDeadline(request.Deadline);
        var ctx = new ErrorContext(request.MethodSymbol, connection.Address, DeadlineSet: deadline is not null);

        try
        {
            var resolve = Stopwatch.StartNew();

            await using var session = await DescriptorSourceFactory.CreateAsync(
                connection.Address, [], [], [],
                channelOptions: options,
                reflectionMetadata: reflectionMetadata,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Accept both the dotted FQN and the pkg.Service/Method invocation grammar.
            var symbol = request.MethodSymbol.Replace('/', '.');

            if (await session.Source.FindSymbolAsync(symbol, cancellationToken).ConfigureAwait(false) is not MethodDescriptor method)
            {
                return Failure(ErrorMapper.FromSchema($"Method '{request.MethodSymbol}' was not found on the server.", ctx));
            }

            resolve.Stop();

            // FR-066: resolve ${ENV_VAR} placeholders in header values at send time; an undefined
            // variable fails the call (never sent as empty).
            var callHeaders = GrpcChannelFactory.CreateMetadata(
                request.Headers.Select(h => $"{h.Name}: {ResolveEnvironmentVariables(h.Value)}"),
                NullIfBlank(connection.UserAgent));

            var requestMessage = invocation.CreateMessageFromJson(method.InputType, request.RequestJson, request.AllowUnknownFields);

            var call = Stopwatch.StartNew();
            var outcome = await invocation.InvokeUnaryAsync(session.Channel!, method, requestMessage, callHeaders, deadline, cancellationToken).ConfigureAwait(false);
            call.Stop();

            var responseJson = outcome.Response is null
                ? null
                : invocation.MessageToJson(outcome.Response, request.EmitDefaults, indent: true);

            var timing = new TimingModel(
                [new TimingPhase("Resolve", resolve.Elapsed), new TimingPhase("Call", call.Elapsed)],
                RequestBytes: requestMessage.CalculateSize(),
                ResponseBytes: outcome.Response?.CalculateSize() ?? 0);

            return new InvocationResultModel(
                Ok: outcome.Ok,
                ResponseJson: responseJson,
                ResponseHeaders: ToItems(outcome.ResponseHeaders),
                ResponseTrailers: ToItems(outcome.ResponseTrailers),
                Status: new InvocationStatusModel(outcome.Status.Code, outcome.Status.CodeName, outcome.Status.Detail),
                Timing: timing,
                ErrorMessage: outcome.Ok ? null : NonEmpty(outcome.Status.Detail, outcome.Status.CodeName),
                Error: outcome.Ok ? null : ErrorMapper.FromOutcome(outcome, ctx));
        }
        catch (OperationCanceledException)
        {
            throw; // user cancellation
        }
        catch (RpcException ex)
        {
            return Failure(ErrorMapper.FromRpcException(ex, ctx));
        }
        catch (Exception ex)
        {
            // Malformed request JSON, etc. — server/Core stays the authority; advisory validation is E1.5 PR-C.
            return Failure(ErrorMapper.FromInternal(ex.Message, ctx));
        }
    }

    private static InvocationResultModel Failure(ErrorModel error)
        => new(false, null, [], [],
            new InvocationStatusModel(error.StatusCode, error.StatusName, error.Headline),
            new TimingModel([], 0, 0), error.Headline, error);

    private static IReadOnlyList<MetadataItem> ToItems(Metadata? metadata)
    {
        if (metadata is null)
        {
            return [];
        }

        var items = new List<MetadataItem>(metadata.Count);

        foreach (var entry in metadata)
        {
            items.Add(entry.IsBinary
                ? new MetadataItem(entry.Key, Convert.ToBase64String(entry.ValueBytes), IsBinary: true)
                : new MetadataItem(entry.Key, entry.Value, IsBinary: false));
        }

        return items;
    }

    private static string ResolveEnvironmentVariables(string value)
        => EnvVarPattern().Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            return Environment.GetEnvironmentVariable(name)
                   ?? throw new InvalidOperationException($"Environment variable '{name}' referenced by a header is not set.");
        });

    private static DateTime? ParseDeadline(string? deadline)
        => string.IsNullOrWhiteSpace(deadline) ? null : DateTime.UtcNow.Add(GrpcChannelFactory.ParseDuration(deadline));

    private static int? ParseSizeOrNull(string? size)
        => string.IsNullOrWhiteSpace(size) ? null : GrpcChannelFactory.ParseSize(size);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string NonEmpty(string? primary, string fallback) => string.IsNullOrEmpty(primary) ? fallback : primary;
}
