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
    private DescriptorSourceFactory(GrpcChannel? channel, IDescriptorSource source, TimeSpan? channelConnectDuration)
    {
        Channel = channel;
        Source = source;
        ChannelConnectDuration = channelConnectDuration;
    }

    public GrpcChannel? Channel { get; }

    public IDescriptorSource Source { get; }

    /// <summary>
    ///     FR-110: how long establishing the HTTP/2 connection took, when the caller requested it via
    ///     <c>measureChannelConnect</c>; otherwise <see langword="null" /> (gRPC connects lazily). Lets the
    ///     timing panel report a distinct <c>channel</c> phase separate from descriptor and call time.
    /// </summary>
    public TimeSpan? ChannelConnectDuration { get; }

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
        IDescriptorWarningSink? warningSink = null,
        DescriptorSourceOptions? descriptorOptions = null,
        bool measureChannelConnect = false)
        => CreateAsync(address, protosetPaths, [], [], channelOptions, reflectionMetadata, cancellationToken, warningSink, descriptorOptions, measureChannelConnect);

    public static async Task<DescriptorSourceFactory> CreateAsync(
        string? address,
        IReadOnlyList<string> protosetPaths,
        IReadOnlyList<string> protoFiles,
        IReadOnlyList<string> importPaths,
        GrpcChannelFactory.ChannelOptions channelOptions,
        Metadata reflectionMetadata,
        CancellationToken cancellationToken,
        IDescriptorWarningSink? warningSink = null,
        DescriptorSourceOptions? descriptorOptions = null,
        bool measureChannelConnect = false)
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
        TimeSpan? channelConnectDuration = null;

        if (hasAddress)
        {
            channel = GrpcChannelFactory.Create(address!, channelOptions);

            // FR-110: eagerly establish the connection so its cost is a distinct, measured phase rather
            // than hidden inside the first reflection/business RPC. Opt-in — the lazy default is unchanged.
            // ConnectAsync requires a plain SocketsHttpHandler; channels with a custom ConnectCallback
            // (e.g. Unix domain sockets) reject it, so fall back to lazy connect (no channel phase) there.
            if (measureChannelConnect)
            {
                try
                {
                    var connect = System.Diagnostics.Stopwatch.StartNew();
                    await channel.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    channelConnectDuration = connect.Elapsed;
                }
                catch (InvalidOperationException)
                {
                    channelConnectDuration = null;
                }
            }
        }

        var options = descriptorOptions ?? DescriptorSourceOptions.Default;
        IDescriptorSource source;

        if (hasProtoFiles)
        {
            // Highest precedence: shell out to protoc and use the resulting protoset.
            source = await ProtoSource.LoadFromProtoFilesAsync(protoFiles, importPaths, cancellationToken, warningSink, options).ConfigureAwait(false);
        }
        else if (hasProtosets)
        {
            source = await ProtosetSource.LoadFromFilesAsync(protosetPaths, options, cancellationToken, warningSink).ConfigureAwait(false);
        }
        else
        {
            source = new ReflectionSource(channel!, reflectionMetadata, options: options, warningSink: warningSink);
        }

        return new DescriptorSourceFactory(channel, source, channelConnectDuration);
    }
}