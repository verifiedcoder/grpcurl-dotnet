using Google.Protobuf.Reflection;
using Grpc.Core;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Reflection-backed <see cref="IDescriptorService" />. Builds the catalog through Core's
///     <see cref="DescriptorSourceFactory" /> using the connection's full channel options, so the
///     explorer sees exactly what a CLI <c>list</c> would. The session (and its channel) is disposed
///     once the catalog is read; the long-lived business channel for invocation arrives with E1.4.
/// </summary>
internal sealed class DescriptorService : IDescriptorService
{
    public async Task<DescriptorLoadResult> LoadAsync(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        var options = ConnectionChannelMapper.ToChannelOptions(connection);
        var metadata = ConnectionChannelMapper.BuildReflectionMetadata(connection);
        var warnings = new CollectingWarningSink();

        try
        {
            await using var session = await DescriptorSourceFactory.CreateAsync(
                connection.Address,
                protosetPaths: [],
                protoFiles: [],
                importPaths: [],
                channelOptions: options,
                reflectionMetadata: metadata,
                cancellationToken: cancellationToken,
                warningSink: warnings).ConfigureAwait(false);

            var serviceNames = await session.Source.ListServicesAsync(cancellationToken).ConfigureAwait(false);
            var services = new List<ServiceEntry>(serviceNames.Count);

            foreach (var name in serviceNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await session.Source.FindSymbolAsync(name, cancellationToken).ConfigureAwait(false) is ServiceDescriptor descriptor)
                {
                    services.Add(MapService(descriptor));
                }
            }

            return DescriptorLoadResult.Success(new ServiceCatalog(services, warnings.Messages));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // user cancellation — the VM treats this as "no longer loading", not an error
        }
        catch (RpcException ex)
        {
            return DescriptorLoadResult.Failure(MapRpcError(ex));
        }
    }

    private static ServiceEntry MapService(ServiceDescriptor descriptor)
    {
        var methods = descriptor.Methods
            .Select(m => new ServiceMethod(
                m.Name,
                $"{descriptor.FullName}/{m.Name}",
                StreamingShapeExtensions.FromFlags(m.IsClientStreaming, m.IsServerStreaming),
                m.InputType.FullName,
                m.OutputType.FullName))
            .ToList();

        return new ServiceEntry(descriptor.FullName, methods);
    }

    private static DescriptorLoadError MapRpcError(RpcException ex)
    {
        if (ex.StatusCode == StatusCode.Unimplemented)
        {
            return new DescriptorLoadError(
                "The server does not implement gRPC server reflection.",
                "The server may not enable reflection; configure a protoset or .proto files instead.",
                ReflectionUnavailable: true);
        }

        var detail = string.IsNullOrWhiteSpace(ex.Status.Detail) ? ex.StatusCode.ToString() : ex.Status.Detail;
        return new DescriptorLoadError(detail, Hint: null, ReflectionUnavailable: false);
    }

    /// <summary>Collects Core's non-fatal descriptor warnings as data instead of console writes.</summary>
    private sealed class CollectingWarningSink : IDescriptorWarningSink
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public void OnWarning(string message) => _messages.Add(message);
    }
}
