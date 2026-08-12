using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Integration.Fixtures;
using GrpCurl.Net.TestServer.Services;
using GrpCurl.Net.Utilities;
using System.Runtime.CompilerServices;

namespace GrpCurl.Net.Tests.Integration.Invocation;

/// <summary>
///     Lifecycle coverage for <see cref="DynamicInvoker.InvokeClientStreamingWithMetadataAsync" /> and
///     the non-metadata <see cref="DynamicInvoker.InvokeServerStreamingAsync" />: the paths that used to
///     abandon a live call on every failure exit (PRD-004), and — since the request half moved onto a
///     <c>RequestStreamProducer</c> — the parked-source hang (PRD-004A).
///     <para>
///         Two independent observation mechanisms, each with its own ablation answer.
///         <see cref="CallCancellationProbe" /> watches the cancellation token grpc-dotnet hands to the
///         transport: only <c>call.Dispose()</c> cancels it in these scenarios, and neither
///         <c>GrpcCall</c> nor <see cref="CancellationTokenSource" /> has a finalizer, so a garbage
///         collection cannot fire it instead. <c>BlockingRequestSource.Unwound</c> proves the invoker's
///         own writer cancellation released a source nothing else could reach —
///         <b>no PRD-004A test cancels the caller's token</b> except the one that says so in its name,
///         so anything that unblocks a source proves the producer did it.
///         <see cref="CallerCancellation_WhileWriting_StaysAnOperationCanceledException" /> is the
///         declared positive control proving the probe is wired up at all.
///     </para>
///     <para>
///         Deliberately absent, so the omissions are not read as oversights:
///         <list type="bullet">
///             <item>
///                 No leak test for <c>fail-late</c> or for caller cancellation: in both the call is
///                 already terminal (the server sent trailers, or the token in <c>CallOptions</c> is the
///                 one that was cancelled), so disposal is not separately observable either side.
///             </item>
///             <item>
///                 No double-dispose test. <see cref="ClientStreamingInvocationResult.Dispose" /> now
///                 releases the call and then the producer's token sources, but both are idempotent on
///                 their own (grpc-dotnet's guarded <c>Dispose</c>, the producer's <c>Interlocked</c>
///                 release), so such a test would pass identically with and without a guard here.
///             </item>
///             <item>
///                 No HTTP/2 stream-exhaustion stress loop. <c>GrpcChannelFactory</c> sets
///                 <c>EnableMultipleHttp2Connections</c> on every path, so a leaking client opens a second
///                 connection past Kestrel's 100-stream ceiling rather than stalling; such a loop passes
///                 with the bug present.
///             </item>
///             <item>
///                 Nothing proves an uncancellable producer is ever <em>reclaimed</em>. Nothing can recall
///                 a read already issued to the OS. What is asserted is that the caller is released and
///                 the source's eventual unwind is observed — see
///                 <see cref="EarlyServerCompletion_UncancellableSource_CompletesWithoutFaulting" />.
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
    public async Task WriteSideFailure_WhenTheServerHasSentNoHeaders_StillReleasesTheCall()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ClientStreamingPath);
        using var channel = CreateProbeChannel(probe, maxSendMessageSize: 64);

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var oversized = CreateRequestWithPayload(methodDescriptor.InputType, 64 * 1024);

        // No reply-with-headers, deliberately. The server parks before sending anything, so the header
        // task the catch consults never resolves. An earlier revision of this test set that header and
        // in doing so hid the defect the PRD-004 review found: the catch awaited those headers, and the
        // finally cannot run until the catch returns, so a pre-header write failure stayed live for as
        // long as the server chose. This is the regression for that, and it fails without the fix.
        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.DelayMs}: {ServerParkMs}"]);

        // The write fails locally on its own merits while the server is parked: no deadline, no caller
        // cancellation, no server status. Disposing the call is the only thing that can release it.
        var exception = await Should.ThrowAsync<RpcException>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, ToAsyncEnumerable([oversized]), metadata, cancellationToken: token)
                .WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.ResourceExhausted);

        // Bare, not enriched: there were no headers when it failed, and the invoker declines to wait
        // for headers that may never come. Asserted rather than left implicit, because the difference
        // between this and RpcInvocationException is exactly what buys the release below.
        exception.ShouldNotBeOfType<RpcInvocationException>();

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

    #region Parked-source lifecycle (PRD-004A)

    [Fact]
    public async Task EarlyServerFailure_ParkedCooperativeSource_SurfacesStatusWithinBound()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateRequestWithPayload(methodDescriptor.InputType, 16));

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.FailEarly}: {(int)StatusCode.Internal}"]);

        // The headline case. The server returns a terminal status while the source is still parked;
        // before the producer existed the invoker was inside `await foreach` and never reached
        // ResponseAsync, so this status went unseen for as long as the source held on.
        var exception = await Should.ThrowAsync<RpcException>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, source.Cooperative(), metadata, cancellationToken: token)
                .WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.Internal);

        // Parked on the caller's source, not on anything the caller cancelled: only the invoker's own
        // writer cancellation can have unwound it.
        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task EarlyServerCompletion_ParkedCooperativeSource_ReturnsResponse()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateRequestWithPayload(methodDescriptor.InputType, 16));

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.CompleteAfterRequests}: 1"]);

        // The OK-status variant: the server answers after one message and never drains the rest, so a
        // successful call has to be reported even though the source is still parked. A source fault
        // provoked by the teardown must not turn this into an error.
        using var result = await invoker.InvokeClientStreamingWithMetadataAsync(
                methodDescriptor, source.Cooperative(), metadata, cancellationToken: token)
            .WaitAsync(Bounded, token);

        _ = result.Response.ShouldNotBeNull();

        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task EarlyServerCompletion_UncancellableSource_CompletesWithoutFaulting()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateRequestWithPayload(methodDescriptor.InputType, 16));

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.CompleteAfterRequests}: 1"]);

        try
        {
            // A source that ignores cancellation must not hold the caller hostage, and the successful
            // RPC must not be reported as a failure just because a write could no longer land.
            using var result = await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, source.Uncancellable(), metadata, cancellationToken: token)
                .WaitAsync(Bounded, token);

            _ = result.Response.ShouldNotBeNull();
        }
        finally
        {
            source.ReleaseUncancellable();
        }

        // Released only after the call is over, so it cannot have been what ended the call above —
        // and awaiting it here leaves nothing running.
        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task CallerCancellation_ParkedSource_StaysAnOperationCanceledException()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ClientStreamingPath);
        using var channel = CreateProbeChannel(probe);

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateRequestWithPayload(methodDescriptor.InputType, 16));

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.DelayMs}: {ServerParkMs}"]);

        var invocation = invoker.InvokeClientStreamingWithMetadataAsync(
            methodDescriptor, source.Cooperative(), metadata, cancellationToken: cancellation.Token);

        // Cancel only once the source is genuinely parked. Cancelling sooner lets the pump's own
        // WriteAsync observe the token and exit, which ends the call for a reason unrelated to this.
        await source.Parked.Task.WaitAsync(Bounded, token);

        await cancellation.CancelAsync();

        // Cancellation now reaches the caller through ResponseAsync, which reports RpcException rather
        // than OperationCanceledException — so the invoker converts it back. Without that the CLI's
        // exit 130 silently becomes 64 + status.
        //
        // This case pins the CONVERSION, not the hang: the source here is cooperative, so the writer
        // token reaches it and it unwinds either way. The hang it cannot see is the next test's.
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await invocation.WaitAsync(Bounded, token));

        await probe.Released.Task.WaitAsync(Bounded, token);
        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task CallerCancellation_UncancellableSource_StillReturnsWithinBound()
    {
        var token = TestContext.Current.CancellationToken;

        using var probe = new CallCancellationProbe(ClientStreamingPath);
        using var channel = CreateProbeChannel(probe);

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateRequestWithPayload(methodDescriptor.InputType, 16));

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.DelayMs}: {ServerParkMs}"]);

        try
        {
            var invocation = invoker.InvokeClientStreamingWithMetadataAsync(
                methodDescriptor, source.Uncancellable(), metadata, cancellationToken: cancellation.Token);

            // Load-bearing: the source must already be parked on something the token cannot reach
            // before the cancel lands. Cancel first and the pump's WriteAsync sees the token, exits,
            // and the caller is released by the very mechanism this test exists to do without.
            await source.Parked.Task.WaitAsync(Bounded, token);

            await cancellation.CancelAsync();

            // The acceptance criterion in full: the caller is released even though the source ignores
            // its token entirely. Nothing can recall the park, so the only way this returns is by
            // observing the response side concurrently — which is the whole of PRD-004A.
            _ = await Should.ThrowAsync<OperationCanceledException>(async () => await invocation.WaitAsync(Bounded, token));

            await probe.Released.Task.WaitAsync(Bounded, token);
        }
        finally
        {
            source.ReleaseUncancellable();
        }

        // Released after the call is over, so the stranded producer cannot be what ended it. Awaiting
        // its unwind here is also what proves the fault it may raise on the way out is observed
        // rather than escaping unobserved.
        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task RequestSourceFault_AbortsRatherThanHalfClosing()
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

            // The policy decision, asserted rather than assumed. Under the duplex half-close-then-grace
            // policy the server would see a clean EOF, record Drained, and answer with an aggregate
            // over a request stream the client already knew was truncated.
            var outcome = await observed.WaitAsync(Bounded, token);

            outcome.ShouldBe(CallAbortObserver.Outcome.Aborted);
        }
        finally
        {
            CallAbortObserver.Forget(observeId);
        }
    }

    [Fact]
    public async Task SourceThrownRpcException_KeepsItsOwnStatus()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetClientStreamingMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var first = CreateRequestWithPayload(methodDescriptor.InputType, 16);

        // A gRPC-backed source carries its own call's status. Normalizing it would rewrite a foreign
        // status using this call's deadline and cancellation state, so a source fault propagates
        // untouched however it is typed.
        var sourceStatus = new Status(StatusCode.FailedPrecondition, "the source's own call failed");

        var exception = await Should.ThrowAsync<RpcException>(
            async () => await invoker.InvokeClientStreamingWithMetadataAsync(
                    methodDescriptor, RpcFaultingSource(first, sourceStatus), cancellationToken: token)
                .WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        exception.Status.Detail.ShouldBe("the source's own call failed");
        exception.ShouldNotBeOfType<RpcInvocationException>();
    }

    #endregion

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
    ///     Fails during enumerator acquisition, before the request stream is ever touched.
    ///     <para>
    ///         Throws immediately rather than parking on a wait handle first, as
    ///         <c>DuplexLifecycleTests</c>'s equivalent does. That was originally forced — the write loop
    ///         was inline, so a park during acquisition would have hung the caller outright — and PRD-004A
    ///         removes the constraint. It is kept because the two shapes test different things: parking
    ///         first covers a source that reports teardown as an ordinary exception, which the duplex
    ///         suite already pins on the shared producer, while this one covers acquisition failing on
    ///         its own merits before any teardown exists to blame.
    ///     </para>
    /// </summary>
    private sealed class AcquisitionFaultingSource : IAsyncEnumerable<IMessage>
    {
        public IAsyncEnumerator<IMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => throw new RequestSourceFailure();
    }

    /// <summary>
    ///     Yields one message and then fails with an <see cref="RpcException" /> of its own — the shape a
    ///     gRPC-backed source has when <i>its</i> call fails, which must not be rewritten as though it
    ///     belonged to this one.
    /// </summary>
    private static async IAsyncEnumerable<IMessage> RpcFaultingSource(IMessage first, Status status)
    {
        yield return first;

        await Task.Yield();

        throw new RpcException(status);
    }

    /// <summary>
    ///     Stands in for an interactive request source: it emits one message and then waits for input
    ///     that never arrives. Copied from <c>DuplexLifecycleTests</c> now that both call shapes drive
    ///     the same producer; it takes the first message, so it is message-agnostic.
    /// </summary>
    private sealed class BlockingRequestSource(IMessage first)
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the source's enumeration actually unwound.</summary>
        public TaskCompletionSource Unwound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Completes once the source has yielded its message and is actually parked. Tests that
        ///     cancel must wait for this first: cancelling earlier means the pump's own
        ///     <c>WriteAsync</c> observes the token and exits, which unblocks the caller for a reason
        ///     that has nothing to do with the fix under test.
        /// </summary>
        public TaskCompletionSource Parked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Parks on the enumerator's own token, as a well-behaved source does.</summary>
        public async IAsyncEnumerable<IMessage> Cooperative([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                yield return first;

                _ = Parked.TrySetResult();

                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                _ = Unwound.TrySetResult();
            }
        }

        /// <summary>
        ///     Parks on something with no token at all — the shape of a console read already issued to
        ///     the OS, which cancellation cannot recall.
        /// </summary>
        public async IAsyncEnumerable<IMessage> Uncancellable()
        {
            try
            {
                yield return first;

                _ = Parked.TrySetResult();

                await _released.Task;
            }
            finally
            {
                _ = Unwound.TrySetResult();
            }
        }

        /// <summary>Lets an uncancellable enumeration finish so the test leaves nothing running.</summary>
        public void ReleaseUncancellable() => _ = _released.TrySetResult();
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
