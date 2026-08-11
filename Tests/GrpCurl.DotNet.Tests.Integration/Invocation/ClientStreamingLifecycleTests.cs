using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Integration.Fixtures;
using GrpCurl.Net.TestServer.Services;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Integration.Invocation;

/// <summary>
///     Call-ownership coverage for <see cref="DynamicInvoker.InvokeClientStreamingWithMetadataAsync" />
///     and the non-metadata <see cref="DynamicInvoker.InvokeServerStreamingAsync" /> — the paths that
///     used to abandon a live call on every failure exit (PRD-004).
///     <para>
///         Five of the eight cases are proved by <see cref="CallCancellationProbe" />, which watches the
///         cancellation token grpc-dotnet hands to the transport. Only <c>call.Dispose()</c> cancels that
///         token in these scenarios, and neither <c>GrpcCall</c> nor <see cref="CancellationTokenSource" />
///         has a finalizer, so a garbage collection cannot fire it instead. A probe that stopped working
///         would fail those tests rather than silently pass them, and
///         <see cref="CallerCancellation_WhileWriting_StaysAnOperationCanceledException" /> is the declared
///         positive control that proves it is wired up at all.
///     </para>
///     <para>
///         Deliberately absent, so the omissions are not read as oversights:
///         <list type="bullet">
///             <item>
///                 No test drives a source that parks. This method awaits the caller's enumerable
///                 <em>inline</em> — there is no producer task and no linked writer token — so a parked
///                 source holds the call hostage with or without this fix. That is the client-streaming
///                 analogue of the duplex hang PRD-003 fixed, and it is not fixed here.
///             </item>
///             <item>
///                 No leak test for <c>fail-late</c> or for caller cancellation: in both the call is
///                 already terminal (the server sent trailers, or the token in <c>CallOptions</c> is the
///                 one that was cancelled), so disposal is not separately observable either side.
///             </item>
///             <item>
///                 No double-dispose test. <see cref="ClientStreamingInvocationResult.Dispose" /> is a
///                 passthrough to grpc-dotnet's own idempotent <c>Dispose</c>, so such a test would pass
///                 identically with and without a guard and would prove nothing.
///             </item>
///             <item>
///                 No HTTP/2 stream-exhaustion stress loop. <c>GrpcChannelFactory</c> sets
///                 <c>EnableMultipleHttp2Connections</c> on every path, so a leaking client opens a second
///                 connection past Kestrel's 100-stream ceiling rather than stalling; such a loop passes
///                 with the bug present.
///             </item>
///         </list>
///     </para>
/// </summary>
[Collection("GrpcServer")]
public sealed class ClientStreamingLifecycleTests(GrpcTestFixture fixture)
{
    /// <summary>
    ///     Generous relative to the operations under test (loopback RPCs over a running server), so a
    ///     failure means "this never happened", not "this machine was slow".
    /// </summary>
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    private const string ClientStreamingPath = "/testing.TestService/StreamingInputCall";

    private const string ServerStreamingPath = "/testing.TestService/StreamingOutputCall";

    /// <summary>
    ///     Long enough that the server is still parked when the client-side failure happens, and
    ///     nothing but the client can end the call inside <see cref="Bounded" />.
    /// </summary>
    private const int ServerParkMs = 30_000;

    [Fact]
    public async Task WriteSideFailure_WhileServerIsStillReading_ReleasesTheCall()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ClientStreamingPath);
        using var channel = CreateProbeChannel(probe, maxSendMessageSize: 64);

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var oversized = CreateRequestWithPayload(methodDescriptor.InputType, 64 * 1024);

        // reply-with-headers matters twice over: it is what the RpcInvocationException assertion below
        // reads, and AttachResponseHeadersAsync awaits the headers task unbounded — without it the
        // invoker would sit on that await for the full server park before ever reaching its finally.
        var metadata = GrpcChannelFactory.CreateMetadata(
            [
                $"{MetadataConstants.ReplyWithHeaders}: x-cs-header: leak",
                $"{MetadataConstants.DelayMs}: {ServerParkMs}"
            ]);

        // The write fails locally on its own merits while the server is parked: no deadline, no caller
        // cancellation, no server status. Disposing the call is the only thing that can release it.
        var exception = await Should.ThrowAsync<RpcException>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, ToAsyncEnumerable([oversized]), metadata, cancellationToken: token)
                .WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.ResourceExhausted);

        var invocationException = exception.ShouldBeOfType<RpcInvocationException>();

        invocationException.ResponseHeaders.GetValue("x-cs-header").ShouldBe("leak");

        await probe.Released.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task RequestSourceFault_WhileServerIsStillReading_ReleasesTheCall()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ClientStreamingPath);
        using var channel = CreateProbeChannel(probe);

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var first = CreateRequestWithPayload(methodDescriptor.InputType, 16);

        // No catch matches a source fault, so it leaves the method through the finally alone. The
        // client never half-closes, so the server stays in ReadAllAsync and cannot end the call either.
        _ = await Should.ThrowAsync<RequestSourceFailure>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, FaultingSource(first), cancellationToken: token)
                .WaitAsync(Bounded, token));

        await probe.Released.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task RequestSourceAcquisitionFault_ReleasesTheCall()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ClientStreamingPath);
        using var channel = CreateProbeChannel(probe);

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);

        // A distinct ordering: the call exists but its request stream was never touched, so the failure
        // precedes the first write rather than following one.
        _ = await Should.ThrowAsync<RequestSourceFailure>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, new AcquisitionFaultingSource(), cancellationToken: token)
                .WaitAsync(Bounded, token));

        await probe.Released.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task ServerObservesTheStreamReset_WhenTheRequestSourceFaults()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var first = CreateRequestWithPayload(methodDescriptor.InputType, 16);

        var observeId = Guid.NewGuid().ToString("N");
        var observed = CallAbortObserver.Register(observeId);

        try
        {
            var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.ObserveAbortId}: {observeId}"]);

            _ = await Should.ThrowAsync<RequestSourceFailure>(
                async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                        methodDescriptor, FaultingSource(first), metadata, cancellationToken: token)
                    .WaitAsync(Bounded, token));

            // End-to-end corroboration of the probe tests above: the server was parked reading a request
            // stream the client abandoned, and the only exit it has is the client resetting the stream.
            var outcome = await observed.WaitAsync(Bounded, token);

            outcome.ShouldBe(CallAbortObserver.Outcome.Aborted);
        }
        finally
        {
            CallAbortObserver.Forget(observeId);
        }
    }

    [Fact]
    public async Task ServerStreaming_ConsumerBreaksMidStream_ReleasesTheCall()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ServerStreamingPath);
        using var channel = CreateProbeChannel(probe);

        var methodDescriptor = await GetMethodDescriptor(new ReflectionSource(channel), "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);

        // Gql2Grpc's subscription path consumes this overload and can stop early. The server is still
        // streaming when the consumer walks away, so only the iterator's own teardown ends the call.
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64, 64, 64, 64, 64, 64, 64, 64]);

        await ConsumeFirstThenBreak().WaitAsync(Bounded, token);

        await probe.Released.Task.WaitAsync(Bounded, token);

        return;

        async Task ConsumeFirstThenBreak()
        {
            await foreach (var _ in invoker.InvokeServerStreamingAsync(methodDescriptor, request, cancellationToken: token))
            {
                break;
            }
        }
    }

    [Fact]
    public async Task FiniteRequests_MetadataOverload_ReturnsResponseHeadersAndTrailers()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);

        var requests = new List<IMessage>
        {
            CreateRequestWithPayload(methodDescriptor.InputType, 100),
            CreateRequestWithPayload(methodDescriptor.InputType, 200)
        };

        var metadata = GrpcChannelFactory.CreateMetadata(
            [
                $"{MetadataConstants.ReplyWithHeaders}: x-cs-header: lifecycle",
                $"{MetadataConstants.ReplyWithTrailers}: x-cs-trailer: lifecycle"
            ]);

        // Success-path regression cover, and the only coverage this overload's headers and trailers
        // have — every other client-streaming test goes through the non-metadata wrapper.
        //
        // It is NOT a guard on the ownership flag, though it looks like one. Ablation says so: delete
        // `ownershipTransferred = true` and this still passes, because ownership transfers only after
        // `await call.ResponseAsync` has returned, so the call is already terminal and disposing it a
        // second time changes nothing an observer can see. The flag earns its place by not making
        // correctness depend on grpc-dotnet's Dispose being idempotent — not by being testable.
        using var result = await invoker.InvokeClientStreamingWithMetadataAsync(
                methodDescriptor, ToAsyncEnumerable(requests), metadata, cancellationToken: token)
            .WaitAsync(Bounded, token);

        _ = result.Response.ShouldNotBeNull();

        var headers = await result.ResponseHeadersAsync.WaitAsync(Bounded, token);

        headers.GetValue("x-cs-header").ShouldBe("lifecycle");

        var trailers = result.GetTrailers();

        _ = trailers.ShouldNotBeNull();

        trailers.GetValue("x-cs-trailer").ShouldBe("lifecycle");
    }

    [Fact]
    public async Task CallerCancellation_WhileWriting_StaysAnOperationCanceledException()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ClientStreamingPath);
        using var channel = CreateProbeChannel(probe);

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var first = CreateRequestWithPayload(methodDescriptor.InputType, 16);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.DelayMs}: {ServerParkMs}"]);

        // NOT leak coverage: the caller's token IS the call's token, so this passes with or without the
        // ownership fix. It exists to pin the exception contract — the CLI maps RPC failures to
        // 64 + status and cancellation to 130, so an OCE must never be re-typed as an RpcException —
        // and to serve as the positive control for the probe: if the probe never fired at all, the
        // "nothing else can release the call" tests above would pass vacuously and this one would fail.
        _ = await Should.ThrowAsync<OperationCanceledException>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, CancellingSource(first, cancellation), metadata, cancellationToken: cancellation.Token)
                .WaitAsync(Bounded, token));

        await probe.Released.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task EarlyServerFailure_SurfacesNormalizedStatusWithHeaders()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateRequestWithPayload(methodDescriptor.InputType, 16);

        var metadata = GrpcChannelFactory.CreateMetadata(
            [
                $"{MetadataConstants.ReplyWithHeaders}: x-cs-header: early",
                $"{MetadataConstants.FailEarly}: {(int)StatusCode.Internal}"
            ]);

        // NOT leak coverage either — the server ended the stream itself, so nothing about disposal is
        // observable here. It guards the other direction: that the new finally cannot mask or downgrade
        // what the catch produced, headers included.
        var exception = await Should.ThrowAsync<RpcException>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, ToAsyncEnumerable([request]), metadata, cancellationToken: token)
                .WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.Internal);

        var invocationException = exception.ShouldBeOfType<RpcInvocationException>();

        invocationException.ResponseHeaders.GetValue("x-cs-header").ShouldBe("early");
    }

    #region Helper Methods

    private GrpcChannel CreateChannel()
        => GrpcChannelFactory.Create(
            $"http://{fixture.Address}",
            new GrpcChannelFactory.ChannelOptions
            {
                Plaintext = true
            });

    /// <summary>
    ///     The production plaintext channel with an observing handler spliced in front of the transport:
    ///     same <c>SocketsHttpHandler</c> settings and the same protocol guards
    ///     <see cref="GrpcChannelFactory" /> applies, so what is under test is call ownership rather
    ///     than a channel that merely resembles the real one.
    /// </summary>
    private GrpcChannel CreateProbeChannel(CallCancellationProbe probe, int? maxSendMessageSize = null)
    {
        probe.InnerHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true
        };

        return GrpcChannel.ForAddress(
            $"http://{fixture.Address}",
            new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                MaxSendMessageSize = maxSendMessageSize,
                HttpHandler = GrpcChannelFactory.WrapWithProtocolGuards(probe),

                // The probe is owned by the test's `using`, and disposing it twice would take the
                // transport down under a still-running assertion.
                DisposeHttpClient = false
            });
    }

    private static async Task<MethodDescriptor> GetClientStreamingMethod(GrpcChannel channel)
        => await GetMethodDescriptor(new ReflectionSource(channel), "testing.TestService.StreamingInputCall");

    private static async Task<MethodDescriptor> GetMethodDescriptor(ReflectionSource source, string methodName)
    {
        var symbol = await source.FindSymbolAsync(methodName);

        _ = symbol.ShouldNotBeNull();

        return symbol.ShouldBeOfType<MethodDescriptor>();
    }

    private static SimpleDynamicMessage CreateRequestWithPayload(MessageDescriptor descriptor, int payloadSize)
    {
        var message = new SimpleDynamicMessage(descriptor);
        var payloadField = descriptor.FindFieldByName("payload");

        if (payloadField?.MessageType is null)
        {
            return message;
        }

        var payload = new SimpleDynamicMessage(payloadField.MessageType);
        var bodyField = payloadField.MessageType.FindFieldByName("body");

        if (bodyField is not null)
        {
            payload.Fields[bodyField] = ByteString.CopyFrom(new byte[payloadSize]);
        }

        message.Fields[payloadField] = payload;

        return message;
    }

    private static SimpleDynamicMessage CreateStreamingOutputRequest(MessageDescriptor descriptor, int[] sizes)
    {
        var message = new SimpleDynamicMessage(descriptor);

        var responseTypeField = descriptor.FindFieldByName("response_type");

        if (responseTypeField is not null)
        {
            message.Fields[responseTypeField] = 0;
        }

        var paramsField = descriptor.FindFieldByName("response_parameters");

        if (paramsField?.MessageType is null)
        {
            return message;
        }

        message.RepeatedFields[paramsField] = [];

        foreach (var size in sizes)
        {
            var param = new SimpleDynamicMessage(paramsField.MessageType);
            var sizeField = paramsField.MessageType.FindFieldByName("size");

            if (sizeField is not null)
            {
                param.Fields[sizeField] = size;
            }

            message.RepeatedFields[paramsField].Add(param);
        }

        return message;
    }

    private static async IAsyncEnumerable<IMessage> ToAsyncEnumerable(IEnumerable<IMessage> messages)
    {
        foreach (var message in messages)
        {
            yield return message;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IMessage> FaultingSource(IMessage first)
    {
        yield return first;

        await Task.Yield();

        throw new RequestSourceFailure();
    }

    /// <summary>
    ///     Yields one message, then cancels the caller's token and waits to be torn down by it — so the
    ///     cancellation lands while the method is writing rather than before it starts.
    /// </summary>
    private static async IAsyncEnumerable<IMessage> CancellingSource(IMessage first, CancellationTokenSource cancellation)
    {
        yield return first;

        await cancellation.CancelAsync();

        await Task.Delay(Timeout.Infinite, cancellation.Token);

        yield return first;
    }

    /// <summary>
    ///     Fails during enumerator acquisition, before the request stream is ever touched. Written here
    ///     rather than borrowed from <c>DuplexLifecycleTests</c>, whose equivalent parks on a wait handle
    ///     — which this inline write loop would never get past.
    /// </summary>
    private sealed class AcquisitionFaultingSource : IAsyncEnumerable<IMessage>
    {
        public IAsyncEnumerator<IMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => throw new RequestSourceFailure();
    }

    /// <summary>
    ///     Watches the cancellation token grpc-dotnet passes to the transport for the call under test.
    ///     Disposing the call cancels it, which is exactly what resets the HTTP/2 stream, so this is a
    ///     direct observation of the release rather than a proxy for it.
    /// </summary>
    private sealed class CallCancellationProbe(string path) : DelegatingHandler
    {
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Filtered by path so reflection traffic on the same channel cannot supply the signal.
            if (request.RequestUri?.AbsolutePath == path)
            {
                // Deliberately not disposed: the registration has to outlive SendAsync, which returns
                // as soon as the response headers arrive.
                _ = cancellationToken.Register(() => Released.TrySetResult());
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class RequestSourceFailure() : Exception("The request source failed.");

    #endregion
}
