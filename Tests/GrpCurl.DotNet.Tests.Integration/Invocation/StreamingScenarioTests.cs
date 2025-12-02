using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Integration.Fixtures;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Integration.Invocation;

[Collection("GrpcServer")]
public sealed class StreamingScenarioTests(GrpcTestFixture fixture)
{
    #region Server Streaming Cancellation Tests

    [Fact]
    public async Task ServerStreaming_CancellationDuringStream_StopsProcessing()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [100, 100, 100, 100, 100]);
        using var cts = new CancellationTokenSource();
        var responses = new List<IMessage>();
        Exception? caughtException = null;

        // Act
        try
        {
            await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request, cancellationToken: cts.Token))
            {
                responses.Add(response);

                if (responses.Count >= 2)
                {
                    await cts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            caughtException = ex;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            caughtException = ex;
        }

        // Assert
        caughtException.ShouldNotBeNull();
        responses.Count.ShouldBeGreaterThanOrEqualTo(2);
        responses.Count.ShouldBeLessThan(5);
    }

    [Fact]
    public async Task ServerStreaming_PreCancelledToken_ThrowsImmediately()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [100]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        // The pre-cancelled token should cause an immediate exception before any iteration
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in invoker.InvokeServerStreamingAsync(methodDescriptor, request, cancellationToken: cts.Token))
            {
                // This body should never execute - the exception is thrown before iteration starts
            }
        });
    }

    #endregion

    #region Client Streaming Cancellation Tests

    [Fact]
    public async Task ClientStreaming_PreCancelledToken_ThrowsImmediately()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingInputCall");
        var invoker = new DynamicInvoker(channel);
        var requests = CreateStreamingInputRequests(methodDescriptor.InputType, [100, 200]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            invoker.InvokeClientStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ClientStreaming_LargeNumberOfRequests_HandlesCorrectly()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingInputCall");
        var invoker = new DynamicInvoker(channel);
        var sizes = Enumerable.Range(1, 50).Select(_ => 10).ToArray();
        var requests = CreateStreamingInputRequests(methodDescriptor.InputType, sizes);

        // Act
        var response = await invoker.InvokeClientStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests));

        // Assert
        response.ShouldNotBeNull();
        var dynamicResponse = response as SimpleDynamicMessage;
        dynamicResponse.ShouldNotBeNull();
        var sizeField = dynamicResponse.Descriptor.FindFieldByName("aggregated_payload_size");
        sizeField.ShouldNotBeNull();
        var totalSize = (int)dynamicResponse.Fields[sizeField]!;
        totalSize.ShouldBe(500); // 50 * 10
    }

    #endregion

    #region Duplex Streaming Tests

    [Fact]
    public async Task DuplexStreaming_PreCancelledToken_ThrowsImmediately()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.FullDuplexCall");
        var invoker = new DynamicInvoker(channel);
        var requests = new List<IMessage>
        {
            CreateStreamingOutputRequest(methodDescriptor.InputType, [100])
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        // The pre-cancelled token should cause an immediate exception before any iteration
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests), cancellationToken: cts.Token))
            {
                // This body should never execute - the exception is thrown before iteration starts
            }
        });
    }

    [Fact]
    public async Task DuplexStreaming_ManyMessagesEachDirection_AllReceived()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.FullDuplexCall");
        var invoker = new DynamicInvoker(channel);
        var requests = Enumerable.Range(1, 10)
            .Select(_ => CreateStreamingOutputRequest(methodDescriptor.InputType, [50]))
            .ToList();
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests)))
        {
            responses.Add(response);
        }

        // Assert
        responses.Count.ShouldBe(10);
    }

    [Fact]
    public async Task DuplexStreaming_MultipleResponsesPerRequest_AllReceived()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.FullDuplexCall");
        var invoker = new DynamicInvoker(channel);
        var requests = new List<IMessage>
        {
            CreateStreamingOutputRequest(methodDescriptor.InputType, [100, 200, 300]),
            CreateStreamingOutputRequest(methodDescriptor.InputType, [400, 500, 600])
        };
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests)))
        {
            responses.Add(response);
        }

        // Assert
        responses.Count.ShouldBe(6);
    }

    #endregion

    #region Streaming with Metadata Tests

    [Fact]
    public async Task ServerStreaming_WithMetadata_HeadersSent()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var metadata = GrpcChannelFactory.CreateMetadata(["x-custom-header: test-value"]);
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [100]);
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request, headers: metadata))
        {
            responses.Add(response);
        }

        // Assert
        responses.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ClientStreaming_WithMetadata_HeadersSent()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingInputCall");
        var metadata = GrpcChannelFactory.CreateMetadata(["x-custom-header: test-value"]);
        var invoker = new DynamicInvoker(channel);
        var requests = CreateStreamingInputRequests(methodDescriptor.InputType, [100]);

        // Act
        var response = await invoker.InvokeClientStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests), headers: metadata);

        // Assert
        response.ShouldNotBeNull();
    }

    [Fact]
    public async Task DuplexStreaming_WithMetadata_HeadersSent()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.FullDuplexCall");
        var metadata = GrpcChannelFactory.CreateMetadata(["x-custom-header: test-value"]);
        var invoker = new DynamicInvoker(channel);
        var requests = new List<IMessage>
        {
            CreateStreamingOutputRequest(methodDescriptor.InputType, [100])
        };
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests), headers: metadata))
        {
            responses.Add(response);
        }

        // Assert
        responses.ShouldHaveSingleItem();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ServerStreaming_LargePayloadSizes_HandlesCorrectly()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [1000, 2000, 3000]);
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request))
        {
            responses.Add(response);
        }

        // Assert
        responses.Count.ShouldBe(3);
        foreach (var dynamicResponse in responses.Select(response => response as SimpleDynamicMessage))
        {
            dynamicResponse.ShouldNotBeNull();
            var payloadField = dynamicResponse.Descriptor.FindFieldByName("payload");
            if (payloadField is not null)
            {
                dynamicResponse.Fields.ContainsKey(payloadField).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public async Task ClientStreaming_VariablePayloadSizes_AccumulatesCorrectly()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingInputCall");
        var invoker = new DynamicInvoker(channel);
        var sizes = new[] { 1, 10, 100, 1000 };
        var requests = CreateStreamingInputRequests(methodDescriptor.InputType, sizes);

        // Act
        var response = await invoker.InvokeClientStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests));

        // Assert
        response.ShouldNotBeNull();
        var dynamicResponse = response as SimpleDynamicMessage;
        dynamicResponse.ShouldNotBeNull();
        var sizeField = dynamicResponse.Descriptor.FindFieldByName("aggregated_payload_size");
        sizeField.ShouldNotBeNull();
        var totalSize = (int)dynamicResponse.Fields[sizeField]!;
        totalSize.ShouldBe(1111); // 1 + 10 + 100 + 1000
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

    private static async Task<MethodDescriptor> GetMethodDescriptor(ReflectionSource source, string methodName)
    {
        // Arrange & Act
        var symbol = await source.FindSymbolAsync(methodName);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<MethodDescriptor>();

        return (MethodDescriptor)symbol;
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

    private static List<IMessage> CreateStreamingInputRequests(MessageDescriptor descriptor, int[] payloadSizes)
    {
        var requests = new List<IMessage>();

        foreach (var size in payloadSizes)
        {
            var message = new SimpleDynamicMessage(descriptor);

            var payloadField = descriptor.FindFieldByName("payload");

            if (payloadField?.MessageType is not null)
            {
                var payload = new SimpleDynamicMessage(payloadField.MessageType);
                var bodyField = payloadField.MessageType.FindFieldByName("body");

                if (bodyField is not null)
                {
                    var data = new byte[size];

                    payload.Fields[bodyField] = ByteString.CopyFrom(data);
                }

                message.Fields[payloadField] = payload;
            }

            requests.Add(message);
        }

        return requests;
    }

    private static async IAsyncEnumerable<IMessage> ToAsyncEnumerable(IEnumerable<IMessage> messages)
    {
        foreach (var message in messages)
        {
            yield return message;
        }

        await Task.CompletedTask;
    }

    #endregion
}
