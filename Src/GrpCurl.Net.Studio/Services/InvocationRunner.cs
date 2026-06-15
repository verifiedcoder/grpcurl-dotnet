using System.Diagnostics;
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
internal sealed class InvocationRunner(IInvocationService invocation) : IInvocationRunner
{
    public async Task<InvocationResultModel> InvokeUnaryAsync(InvocationRequestModel request, CancellationToken cancellationToken)
    {
        var connection = request.Connection;
        var options = ConnectionChannelMapper.ToChannelOptions(connection, ParseSizeOrNull(request.MaxMessageSize));
        var reflectionMetadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var deadline = ParseDeadline(request.Deadline);

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
                return Failure($"Method '{request.MethodSymbol}' was not found on the server.");
            }

            resolve.Stop();

            var callHeaders = GrpcChannelFactory.CreateMetadata(
                request.Headers.Select(h => $"{h.Name}: {h.Value}"),
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
                ErrorMessage: outcome.Ok ? null : NonEmpty(outcome.Status.Detail, outcome.Status.CodeName));
        }
        catch (OperationCanceledException)
        {
            throw; // user cancellation
        }
        catch (RpcException ex)
        {
            return Failure(NonEmpty(ex.Status.Detail, ex.StatusCode.ToString()));
        }
        catch (Exception ex)
        {
            // Malformed request JSON, etc. — server/Core stays the authority; advisory validation is E1.4 PR-C.
            return Failure(ex.Message);
        }
    }

    private static InvocationResultModel Failure(string message)
        => new(false, null, [], [],
            new InvocationStatusModel((int)StatusCode.Unknown, nameof(StatusCode.Unknown), message),
            new TimingModel([], 0, 0), message);

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

    private static DateTime? ParseDeadline(string? deadline)
        => string.IsNullOrWhiteSpace(deadline) ? null : DateTime.UtcNow.Add(GrpcChannelFactory.ParseDuration(deadline));

    private static int? ParseSizeOrNull(string? size)
        => string.IsNullOrWhiteSpace(size) ? null : GrpcChannelFactory.ParseSize(size);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string NonEmpty(string? primary, string fallback) => string.IsNullOrEmpty(primary) ? fallback : primary;
}
