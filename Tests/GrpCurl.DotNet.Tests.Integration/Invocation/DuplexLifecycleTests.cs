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
///         Four of these tests drive a request source that blocks indefinitely, because that is
///         the shape which reproduces the filed hang: the finite-list sources used elsewhere
///         always complete on their own and so can never leave a producer stranded. Two more
///         cover a source that faults mid-stream, and the last two are regression cover for the
///         finite and writer-less paths. No test cancels the caller's token, so anything that
///         unblocks a source proves the invoker's own writer cancellation did it.
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
    public async Task RequestSourceFault_SurfacesToTheCaller()
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

        // The caller's own input errors (malformed request JSON, a stdin limit breach) reach the
        // CLI through this path; absorbing transport write noise must not absorb them too.
        _ = await Should.ThrowAsync<RequestSourceFailure>(async () => await drain.WaitAsync(Bounded, token));
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
