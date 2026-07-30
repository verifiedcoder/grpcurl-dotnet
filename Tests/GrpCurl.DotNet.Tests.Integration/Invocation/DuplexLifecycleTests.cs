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
///     Lifecycle coverage for <see cref="DynamicInvoker.InvokeDuplexStreamingWithMetadataAsync" /> —
///     the bidi path the CLI, Studio and the conformance adapter all use (PRD-003).
///     <para>
///         20 executed cases from 17 methods. Seven drive a source that parks indefinitely — the
///         shape that reproduces the filed hang, since the finite-list sources used elsewhere always
///         complete on their own and can never strand a producer; nine drive a source that fails
///         mid-stream, one of them a four-case theory over the exception types that are ambiguous
///         between a source and the transport; two fail on the write half instead; two are
///         regression cover for the finite and writer-less paths. No test cancels the caller's
///         token, so anything that unblocks a source proves the invoker's own cancellation did it.
///     </para>
/// </summary>
[Collection("GrpcServer")]
public sealed class DuplexLifecycleTests(GrpcTestFixture fixture)
{
    /// <summary>
    ///     Generous relative to the operations under test (a loopback RPC that completes after one
    ///     message), so a failure means "this hung", not "this machine was slow".
    /// </summary>
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task EarlyServerCompletion_CooperativeBlockingSource_EnumerationCompletes()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateStreamingOutputRequest(methodDescriptor.InputType, [64]));

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.CompleteAfterRequests}: 1"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, source.Cooperative(), metadata, cancellationToken: token);

        var responses = await DrainAsync(result, token).WaitAsync(Bounded, token);

        responses.Count.ShouldBe(1);

        // The producer was parked on the caller's source, not on anything the caller cancelled:
        // only the invoker's writer cancellation can have unwound it.
        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task EarlyServerCompletion_UncancellableSource_CompletesWithoutFaulting()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateStreamingOutputRequest(methodDescriptor.InputType, [64]));

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.CompleteAfterRequests}: 1"]);

        try
        {
            await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
                methodDescriptor, source.Uncancellable(), metadata, cancellationToken: token);

            // A source that ignores cancellation must not be able to hold the consumer hostage,
            // and the successful RPC must not be reported as a failure just because a write could
            // no longer land (grpc-dotnet surfaces that as RpcException with StatusCode.OK).
            var responses = await DrainAsync(result, token).WaitAsync(Bounded, token);

            responses.Count.ShouldBe(1);
        }
        finally
        {
            source.ReleaseUncancellable();
        }

        // The stranded producer is released only once the call is over, so it cannot have been
        // what ended the enumeration above — and awaiting it here leaves nothing running.
        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task EarlyServerFailure_BlockingSource_SurfacesNormalizedStatus()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateStreamingOutputRequest(methodDescriptor.InputType, [64]));

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.FailEarly}: {(int)StatusCode.Internal}"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, source.Cooperative(), metadata, cancellationToken: token);

        var drain = DrainAsync(result, token);

        var exception = await Should.ThrowAsync<RpcException>(async () => await drain.WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.Internal);

        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task DisposeBeforeEnumeration_CancelsAndObservesProducer()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var source = new BlockingRequestSource(CreateStreamingOutputRequest(methodDescriptor.InputType, [64]));

        var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, source.Cooperative(), cancellationToken: token);

        // Not a single response is read: disposal alone has to stop the producer.
        await result.DisposeAsync().AsTask().WaitAsync(Bounded, token);

        await source.Unwound.Task.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task RequestSourceFault_RacingIndependentServerCompletion_EndsBoundedEitherWay()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.CompleteAfterRequests}: 1"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, FaultingSource(request), metadata, cancellationToken: token);

        var drain = DrainAsync(result, token);

        // This is the one scenario with a genuinely undetermined outcome, and the assertion says so
        // rather than pretending otherwise. The server here completes OK on its own after the first
        // request, so it races the source's failure:
        //
        //   * source throws first  -> the fault is recorded and surfaces;
        //   * server completes first -> teardown cancels the source before it throws, and a failure
        //     raised after we asked it to stop is deliberately not recorded (§13.1), so the
        //     successful RPC stays successful.
        //
        // Both are correct. What must never happen is a hang or a lost response, so that is what is
        // asserted. Source faults are pinned deterministically by the tests that use an ordinary
        // server, which has no independent reason to finish.
        try
        {
            var responses = await drain.WaitAsync(Bounded, token);

            responses.Count.ShouldBe(1);
        }
        catch (RequestSourceFailure)
        {
            // The other legal outcome.
        }
    }

    [Fact]
    public async Task RequestSourceFault_WhileServerAwaitsMoreRequests_SurfacesToTheCaller()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);

        // Deliberately NO complete-after-requests: an ordinary bidi server reads until the client
        // half-closes, so nothing but the producer's own failure can end this call. Without that
        // coupling the server waits for a request that never comes while the reader waits for the
        // server, and the fault can never reach the caller.
        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, FaultingSource(request), cancellationToken: token);

        var drain = DrainAsync(result, token);

        _ = await Should.ThrowAsync<RequestSourceFailure>(async () => await drain.WaitAsync(Bounded, token));
    }

    [Fact]
    public async Task RequestSourceFault_IsPreferredOverTheStatusItProvokes()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);

        // fail-late fires once the server has drained the request stream — i.e. immediately after
        // the half-close the faulting producer performs. Both halves therefore end badly, and the
        // caller's own failure is the root cause: it is what truncated the stream.
        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.FailLate}: {(int)StatusCode.Internal}"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, FaultingSource(request), metadata, cancellationToken: token);

        var drain = DrainAsync(result, token);

        _ = await Should.ThrowAsync<RequestSourceFailure>(async () => await drain.WaitAsync(Bounded, token));
    }

    [Fact]
    public async Task RequestSourceFault_WithSlowServer_StillSurfacesWithinBound()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);

        // A half-close is ordinary request EOF; gRPC does not make the server's response completion
        // a consequence of it. Here the server is still working on the first request long after the
        // producer has failed, so releasing the reader cannot depend on the server volunteering.
        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.DelayMs}: 30000"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, FaultingSource(request), metadata, cancellationToken: token);

        var drain = DrainAsync(result, token);

        _ = await Should.ThrowAsync<RequestSourceFailure>(async () => await drain.WaitAsync(Bounded, token));
    }

    [Theory]
    [InlineData(AmbiguousFault.InvalidOperation)]
    [InlineData(AmbiguousFault.Io)]
    [InlineData(AmbiguousFault.RpcOk)]
    [InlineData(AmbiguousFault.Canceled)]
    public async Task SourceFaultOfAmbiguousType_IsNotSwallowedAsTransportNoise(AmbiguousFault shape)
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);

        // Every one of these types is also a shape the transport produces after a completed call.
        // Classifying by type is only safe around the write itself; a source that happens to throw
        // one of them must still be reported, not silently treated as post-completion noise.
        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, AmbiguouslyFaultingSource(request, shape), cancellationToken: token);

        var drain = DrainAsync(result, token);

        var thrown = await Should.ThrowAsync<Exception>(async () => await drain.WaitAsync(Bounded, token));

        // Assignable rather than exact: an async iterator that throws OperationCanceledException
        // completes its MoveNextAsync as *canceled*, so the awaiter raises a fresh
        // TaskCanceledException rather than the instance the source threw. What matters is that the
        // failure reaches the caller as its own kind instead of being discarded as transport noise.
        thrown.ShouldBeAssignableTo(ExpectedType(shape));
        thrown.ShouldNotBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task IndependentServerError_IsNotReplacedByACleanupFault()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.FailEarly}: {(int)StatusCode.Internal}"]);

        // The server fails on its own; the source is merely parked and only fails *because* the
        // read path then cancels it. A fault our own cleanup created must never be promoted over
        // the server status that actually ended the call.
        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, CancellationTranslatingSource(), metadata, cancellationToken: token);

        var drain = DrainAsync(result, token);

        var exception = await Should.ThrowAsync<RpcException>(async () => await drain.WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.Internal);
    }

    [Fact]
    public async Task CleanServerCompletion_IsNotReplacedByACleanupFault()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.CompleteAfterRequests}: 1"]);

        // The successful counterpart of IndependentServerError_IsNotReplacedByACleanupFault: the RPC
        // completed OK, and the source only fails because teardown then cancelled it. A completed
        // response half is authoritative, so that cleanup fault must not turn OK into an error.
        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, CancellationTranslatingSource(request), metadata, cancellationToken: token);

        var responses = await DrainAsync(result, token).WaitAsync(Bounded, token);

        responses.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SourceThrownRpcException_KeepsItsOwnStatus()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);

        // A gRPC-backed request source can raise an RpcException of its own. It belongs to that
        // source's call, not to this one, so this call's deadline and protocol normalization must
        // not rewrite it — and it must not depend on how quickly this server happens to finish.
        var sourceStatus = new Status(StatusCode.Cancelled, "No grpc-status found on response");

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, RpcFaultingSource(request, sourceStatus), cancellationToken: token);

        var drain = DrainAsync(result, token);

        var exception = await Should.ThrowAsync<RpcException>(async () => await drain.WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.Cancelled);
    }

    [Fact]
    public async Task WriteSideFailure_DoesNotDisplaceTheServerStatus()
    {
        var token = TestContext.Current.CancellationToken;

        // A send limit small enough that the request below cannot be written at all, so the write
        // half is guaranteed to fail while the server is independently failing the call.
        using var channel = GrpcChannelFactory.Create(
            $"http://{fixture.Address}",
            new GrpcChannelFactory.ChannelOptions
            {
                Plaintext = true,
                MaxSendMessageSize = 64
            });

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);
        var oversized = CreateRequestWithPayload(methodDescriptor.InputType, 64 * 1024);

        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.FailEarly}: {(int)StatusCode.Internal}"]);

        // A write-half failure is always a shadow of the call itself failing — whether it surfaces
        // as RESOURCE_EXHAUSTED on its own merits or as grpc-dotnet's Status(Cancelled) teardown
        // artifact. The read half carries the authoritative status, so the server's error must win
        // either way. (On Windows the artifact form of this race reported CANCELLED instead of
        // INTERNAL; this pins the outcome regardless of which way the write loses.)
        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, ToAsyncEnumerable([oversized]), metadata, cancellationToken: token);

        var drain = DrainAsync(result, token);

        var exception = await Should.ThrowAsync<RpcException>(async () => await drain.WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.Internal);
    }

    [Fact]
    public async Task WriteFault_SurvivesTheCancellationItsOwnAbortCauses()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = GrpcChannelFactory.Create(
            $"http://{fixture.Address}",
            new GrpcChannelFactory.ChannelOptions
            {
                Plaintext = true,
                MaxSendMessageSize = 1024
            });

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);

        var small = CreateStreamingOutputRequest(methodDescriptor.InputType, [64]);
        var oversized = CreateRequestWithPayload(methodDescriptor.InputType, 64 * 1024);

        // The server is still busy when the second write fails locally with RESOURCE_EXHAUSTED, so
        // nothing else can end the call: the producer's own bounded abort is what releases the read,
        // and the CANCELLED it produces is an artifact of that abort. The genuine write fault must
        // not be discarded in favour of the status it itself caused.
        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.DelayMs}: 30000"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, ToAsyncEnumerable([small, oversized]), metadata, cancellationToken: token);

        var drain = DrainAsync(result, token);

        var exception = await Should.ThrowAsync<RpcException>(async () => await drain.WaitAsync(Bounded, token));

        exception.StatusCode.ShouldBe(StatusCode.ResourceExhausted);
    }

    [Fact]
    public async Task ImmediateServerCompletion_IsNotReplacedByAnAcquisitionFault()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);

        // Completing without reading a single request is valid duplex behaviour, and the enumerator
        // is acquired synchronously — a path the causal guard originally missed, so a source that
        // translates teardown cancellation during acquisition could still fault a successful call.
        var metadata = GrpcChannelFactory.CreateMetadata([$"{MetadataConstants.CompleteAfterRequests}: 0"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, new AcquisitionFaultingSource(), metadata, cancellationToken: token);

        var responses = await DrainAsync(result, token).WaitAsync(Bounded, token);

        responses.ShouldBeEmpty();
    }

    [Fact]
    public async Task FiniteRequests_MetadataOverload_ReturnsAllResponsesAndTrailers()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var methodDescriptor = await GetDuplexMethod(channel);
        var invoker = new DynamicInvoker(channel);

        var requests = new List<IMessage>
        {
            CreateStreamingOutputRequest(methodDescriptor.InputType, [100, 200, 300]),
            CreateStreamingOutputRequest(methodDescriptor.InputType, [400, 500, 600])
        };

        var metadata = GrpcChannelFactory.CreateMetadata(
            [$"{MetadataConstants.ReplyWithTrailers}: x-duplex-trailer: lifecycle"]);

        await using var result = invoker.InvokeDuplexStreamingWithMetadataAsync(
            methodDescriptor, ToAsyncEnumerable(requests), metadata, cancellationToken: token);

        var responses = await DrainAsync(result, token).WaitAsync(Bounded, token);

        responses.Count.ShouldBe(6);

        var trailers = result.GetTrailers();

        _ = trailers.ShouldNotBeNull();
        trailers.GetValue("x-duplex-trailer").ShouldBe("lifecycle");
    }

    [Fact]
    public async Task ServerStreaming_MetadataOverload_StillStreamsUnderAwaitUsing()
    {
        var token = TestContext.Current.CancellationToken;

        using var channel = CreateChannel();

        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [100, 200, 300]);

        // The writer-less path shares the result type, so it has to keep working after
        // StreamingInvocationResult became async-disposable.
        await using var result = invoker.InvokeServerStreamingWithMetadataAsync(
            methodDescriptor, request, cancellationToken: token);

        var responses = await DrainAsync(result, token).WaitAsync(Bounded, token);

        responses.Count.ShouldBe(3);
    }

    #region Helper Methods

    private GrpcChannel CreateChannel()
        => GrpcChannelFactory.Create(
            $"http://{fixture.Address}",
            new GrpcChannelFactory.ChannelOptions
            {
                Plaintext = true
            });

    private static async Task<MethodDescriptor> GetDuplexMethod(GrpcChannel channel)
        => await GetMethodDescriptor(new ReflectionSource(channel), "testing.TestService.FullDuplexCall");

    private static async Task<MethodDescriptor> GetMethodDescriptor(ReflectionSource source, string methodName)
    {
        var symbol = await source.FindSymbolAsync(methodName);

        _ = symbol.ShouldNotBeNull();

        return symbol.ShouldBeOfType<MethodDescriptor>();
    }

    private static async Task<List<IMessage>> DrainAsync(StreamingInvocationResult result, CancellationToken cancellationToken)
    {
        var responses = new List<IMessage>();

        await foreach (var response in result.ResponseStream.WithCancellation(cancellationToken))
        {
            responses.Add(response);
        }

        return responses;
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

    /// <summary>Fails with a type the transport also produces after a completed call.</summary>
    private static async IAsyncEnumerable<IMessage> AmbiguouslyFaultingSource(IMessage first, AmbiguousFault shape)
    {
        yield return first;

        await Task.Yield();

        throw shape switch
        {
            AmbiguousFault.InvalidOperation => new InvalidOperationException("source failed"),
            AmbiguousFault.Io => new IOException("source failed"),
            AmbiguousFault.RpcOk => new RpcException(new Status(StatusCode.OK, string.Empty)),
            _ => new OperationCanceledException("source failed")
        };
    }

    private static Type ExpectedType(AmbiguousFault shape) => shape switch
    {
        AmbiguousFault.InvalidOperation => typeof(InvalidOperationException),
        AmbiguousFault.Io => typeof(IOException),
        AmbiguousFault.RpcOk => typeof(RpcException),
        _ => typeof(OperationCanceledException)
    };

    /// <summary>
    ///     Parks until cancelled and then reports its own failure type rather than propagating the
    ///     cancellation — the shape that exposes non-causal fault preference.
    /// </summary>
    private static async IAsyncEnumerable<IMessage> CancellationTranslatingSource(
        IMessage? first = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Parks, so nothing it does can end the call: only the read path's cancellation makes it
        // fail, and it reports that as an ordinary exception rather than a cancellation.
        if (first is not null)
        {
            yield return first;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new RequestSourceFailure();
        }
    }

    /// <summary>Fails with an <see cref="RpcException" /> of its own, as a gRPC-backed source can.</summary>
    private static async IAsyncEnumerable<IMessage> RpcFaultingSource(IMessage first, Status status)
    {
        yield return first;

        await Task.Yield();

        throw new RpcException(status);
    }

    /// <summary>
    ///     Fails during synchronous enumerator acquisition rather than during enumeration, waiting for
    ///     teardown and then reporting it as an ordinary exception.
    /// </summary>
    private sealed class AcquisitionFaultingSource : IAsyncEnumerable<IMessage>
    {
        public IAsyncEnumerator<IMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken.WaitHandle.WaitOne();

            throw new RequestSourceFailure();
        }
    }

    /// <summary>Exception shapes that are ambiguous between a caller's source and the transport.</summary>
    public enum AmbiguousFault
    {
        InvalidOperation,
        Io,
        RpcOk,
        Canceled
    }

    /// <summary>
    ///     Stands in for an interactive request source: it emits one message and then waits for
    ///     input that never arrives.
    /// </summary>
    private sealed class BlockingRequestSource(IMessage first)
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the source's enumeration actually unwound.</summary>
        public TaskCompletionSource Unwound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Parks on the enumerator's own token, as a well-behaved source does.</summary>
        public async IAsyncEnumerable<IMessage> Cooperative([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                yield return first;

                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                _ = Unwound.TrySetResult();
            }
        }

        /// <summary>
        ///     Parks on something with no token at all — the shape of a console read already
        ///     issued to the OS, which cancellation cannot recall.
        /// </summary>
        public async IAsyncEnumerable<IMessage> Uncancellable()
        {
            try
            {
                yield return first;

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

    private sealed class RequestSourceFailure() : Exception("The request source failed.");

    #endregion
}
