using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Creates the <see cref="IDescriptorSource" /> used for a single CLI invocation along
///     with the optional <see cref="GrpcChannel" /> reused by both descriptor discovery and
///     the business RPC call. The returned session is <see cref="IAsyncDisposable" />; it
///     owns the channel and the descriptor source, and disposes both in reverse order.
///     This is the canonical entry point that <c>list</c>, <c>describe</c>, <c>invoke</c>,
///     and the Gql2Grpc query path all use so that TLS/mTLS material, deadlines, and
///     authority overrides apply uniformly to reflection and the actual RPC.
/// </summary>
internal sealed class DescriptorSourceFactory : IAsyncDisposable
{
    private DescriptorSourceFactory(GrpcChannel? channel, IDescriptorSource source)
    {
        Channel = channel;
        Source = source;
    }

    public GrpcChannel? Channel { get; }

    public IDescriptorSource Source { get; }

    public async ValueTask DisposeAsync()
    {
        if (Source is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (Channel is not null)
        {
            await Channel.ShutdownAsync().ConfigureAwait(false);

            Channel.Dispose();
        }
    }

    public static Task<DescriptorSourceFactory> CreateAsync(
        string? address,
        IReadOnlyList<string> protosetPaths,
        GrpcChannelFactory.ChannelOptions channelOptions,
        Metadata reflectionMetadata,
        CancellationToken cancellationToken,
        IDescriptorWarningSink? warningSink = null)
        => CreateAsync(address, protosetPaths, [], [], channelOptions, reflectionMetadata, cancellationToken, warningSink);

    public static async Task<DescriptorSourceFactory> CreateAsync(
        string? address,
        IReadOnlyList<string> protosetPaths,
        IReadOnlyList<string> protoFiles,
        IReadOnlyList<string> importPaths,
        GrpcChannelFactory.ChannelOptions channelOptions,
        Metadata reflectionMetadata,
        CancellationToken cancellationToken,
        IDescriptorWarningSink? warningSink = null)
    {
        var hasProtosets = protosetPaths.Count > 0;
        var hasProtoFiles = protoFiles.Count > 0;
        var hasAddress = !string.IsNullOrEmpty(address);

        if (!hasProtosets && !hasProtoFiles && !hasAddress)
        {
            throw new ArgumentException(
                "Either an address, protoset files, or .proto source files must be supplied.",
                nameof(address));
        }

        GrpcChannel? channel = null;

        if (hasAddress)
        {
            channel = GrpcChannelFactory.Create(address!, channelOptions);
        }

        IDescriptorSource source;

        if (hasProtoFiles)
        {
            // Highest precedence: shell out to protoc and use the resulting protoset.
            source = await ProtoSource.LoadFromProtoFilesAsync(protoFiles, importPaths, cancellationToken, warningSink).ConfigureAwait(false);
        }
        else if (hasProtosets)
        {
            source = await ProtosetSource.LoadFromFilesAsync(protosetPaths, DescriptorSourceOptions.Default, cancellationToken, warningSink).ConfigureAwait(false);
        }
        else
        {
            source = new ReflectionSource(channel!, reflectionMetadata, warningSink: warningSink);
        }

        return new DescriptorSourceFactory(channel, source);
    }
}