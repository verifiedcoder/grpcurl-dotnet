using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IConnectionRegistry" />. The test-connection probe builds a descriptor
///     session from the connection's full channel configuration and issues a reflection
///     <c>ListServices</c> round-trip bounded by a 10s deadline (SPEC-030 §7, FR-018). The cached
///     business channel used by invocation is added with E1.4.
/// </summary>
internal sealed class ConnectionRegistry(ITlsProfileResolver? tlsResolver = null) : IConnectionRegistry
{
    private static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(10);

    public async Task<TestConnectionResult> TestConnectionAsync(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        var addressError = ConnectionValidation.ValidateAddress(connection.Address);

        if (addressError is not null)
        {
            return TestConnectionResult.Failure(addressError);
        }

        var (profile, password) = tlsResolver is null
            ? default
            : await tlsResolver.ResolveAsync(connection, cancellationToken).ConfigureAwait(false);
        var options = ConnectionChannelMapper.ToChannelOptions(connection, maxMessageSize: null, profile, password);
        var metadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var (protosets, protos, imports) = ConnectionChannelMapper.DescriptorPaths(connection);

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCts.CancelAfter(ProbeDeadline);

        try
        {
            await using var session = await DescriptorSourceFactory.CreateAsync(
                connection.Address,
                protosetPaths: protosets,
                protoFiles: protos,
                importPaths: imports,
                channelOptions: options,
                reflectionMetadata: metadata,
                cancellationToken: deadlineCts.Token).ConfigureAwait(false);

            var services = await session.Source.ListServicesAsync(deadlineCts.Token).ConfigureAwait(false);

            return TestConnectionResult.Success(services.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // user cancelled the probe
        }
        catch (OperationCanceledException)
        {
            return TestConnectionResult.Failure($"Timed out after {ProbeDeadline.TotalSeconds:0}s with no response.");
        }
        catch (RpcException ex)
        {
            return TestConnectionResult.Failure(Describe(ex));
        }
        catch (Exception ex)
        {
            return TestConnectionResult.Failure(ex.Message);
        }
    }

    private static string Describe(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.Unimplemented => "Server does not support reflection. Use a protoset or .proto source (coming soon).",
        StatusCode.Unavailable => $"Server unavailable: {ex.Status.Detail}",
        _ => $"{ex.StatusCode}: {ex.Status.Detail}"
    };
}
