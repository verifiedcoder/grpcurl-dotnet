using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Utilities;

namespace Gql2Grpc.Execution;

/// <summary>
/// Creates the <see cref="IDescriptorSource"/> used for a single process invocation along with
/// the <see cref="GrpcChannel"/> reused by both descriptor discovery and RPC calls.
/// Disposes both in reverse order.
/// </summary>
internal sealed class DescriptorSourceFactory : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly IDescriptorSource _source;

    private DescriptorSourceFactory(GrpcChannel channel, IDescriptorSource source)
    {
        _channel = channel;
        _source = source;
    }

    public GrpcChannel Channel => _channel;

    public IDescriptorSource Source => _source;

    public static async Task<DescriptorSourceFactory> CreateAsync(
        string address,
        IReadOnlyList<string> protosetPaths,
        GrpcChannelFactory.ChannelOptions channelOptions,
        Metadata reflectionMetadata,
        CancellationToken cancellationToken)
    {
        var channel = GrpcChannelFactory.Create(address, channelOptions);

        IDescriptorSource source;

        if (protosetPaths.Count > 0)
        {
            source = await ProtosetSource.LoadFromFilesAsync(protosetPaths, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            source = new ReflectionSource(channel, reflectionMetadata, ownsChannel: false);
        }

        return new DescriptorSourceFactory(channel, source);
    }

    public async ValueTask DisposeAsync()
    {
        if (_source is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await _channel.ShutdownAsync().ConfigureAwait(false);
        _channel.Dispose();
    }
}
