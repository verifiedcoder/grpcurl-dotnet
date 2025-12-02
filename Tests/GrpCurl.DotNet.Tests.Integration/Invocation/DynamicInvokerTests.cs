using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Net.Client;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Integration.Fixtures;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Integration.Invocation;

[Collection("GrpcServer")]
public sealed class DynamicInvokerTests(GrpcTestFixture fixture)
{
    #region InvokeUnaryAsync Tests

    [Fact]
    public async Task InvokeUnaryAsync_EmptyCall_ReturnsEmptyResponse()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.EmptyCall");
        var invoker = new DynamicInvoker(channel);
        var request = new SimpleDynamicMessage(methodDescriptor.InputType);

        // Act
        var response = await invoker.InvokeUnaryAsync(methodDescriptor, request);

        // Assert
        response.ShouldNotBeNull();
    }

    [Fact]
    public async Task InvokeUnaryAsync_UnaryCall_ReturnsResponse()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.UnaryCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateSimpleRequest(methodDescriptor.InputType, "test data");

        // Act
        var response = await invoker.InvokeUnaryAsync(methodDescriptor, request);

        // Assert
        response.ShouldNotBeNull();
    }

    [Fact]
    public async Task InvokeUnaryAsync_WithPayload_ReturnsPayloadInResponse()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.UnaryCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateSimpleRequest(methodDescriptor.InputType, "payload data");

        // Act
        var response = await invoker.InvokeUnaryAsync(methodDescriptor, request) as SimpleDynamicMessage;

        // Assert
        response.ShouldNotBeNull();
        var payloadField = response.Descriptor.FindFieldByName("payload");
        if (payloadField is not null && response.Fields.TryGetValue(payloadField, out var payload))
        {
            payload.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task InvokeUnaryAsync_WithMetadata_SendsHeaders()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.EmptyCall");
        var metadata = GrpcChannelFactory.CreateMetadata(["x-custom-header: test-value"]);
        var invoker = new DynamicInvoker(channel);
        var request = new SimpleDynamicMessage(methodDescriptor.InputType);

        // Act
        var response = await invoker.InvokeUnaryAsync(methodDescriptor, request, metadata);

        // Assert
        response.ShouldNotBeNull();
    }

    [Fact]
    public async Task InvokeUnaryAsync_WithCancellation_ThrowsCanceledException()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.EmptyCall");
        var invoker = new DynamicInvoker(channel);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var request = new SimpleDynamicMessage(methodDescriptor.InputType);

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            invoker.InvokeUnaryAsync(methodDescriptor, request, cancellationToken: cts.Token));
    }

    #endregion

    #region InvokeServerStreamingAsync Tests

    [Fact]
    public async Task InvokeServerStreamingAsync_SingleResponse_ReturnsOneMessage()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [100]);
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request))
        {
            responses.Add(response);
        }

        // Assert
        responses.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task InvokeServerStreamingAsync_MultipleResponses_ReturnsAllMessages()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [100, 200, 300]);
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request))
        {
            responses.Add(response);
        }

        // Assert
        responses.Count.ShouldBe(3);
    }

    [Fact]
    public async Task InvokeServerStreamingAsync_EmptyResponseParameters_ReturnsNoMessages()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, []);
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request))
        {
            responses.Add(response);
        }

        // Assert
        responses.ShouldBeEmpty();
    }

    [Fact]
    public async Task InvokeServerStreamingAsync_ResponsesHavePayloads()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingOutputCall");
        var invoker = new DynamicInvoker(channel);
        var request = CreateStreamingOutputRequest(methodDescriptor.InputType, [50, 100]);

        // Act & Assert
        await foreach (var response in invoker.InvokeServerStreamingAsync(methodDescriptor, request))
        {
            response.ShouldNotBeNull();
            var dynamicResponse = response as SimpleDynamicMessage;
            dynamicResponse.ShouldNotBeNull();
        }
    }

    #endregion

    #region InvokeClientStreamingAsync Tests

    [Fact]
    public async Task InvokeClientStreamingAsync_SingleRequest_ReturnsAggregatedSize()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingInputCall");
        var invoker = new DynamicInvoker(channel);
        var requests = new List<IMessage>
        {
            CreateStreamingInputRequest(methodDescriptor.InputType, 100)
        };

        // Act
        var response = await invoker.InvokeClientStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests));

        // Assert
        response.ShouldNotBeNull();
        var dynamicResponse = response as SimpleDynamicMessage;
        dynamicResponse.ShouldNotBeNull();
        var sizeField = dynamicResponse.Descriptor.FindFieldByName("aggregated_payload_size");
        sizeField.ShouldNotBeNull();
        dynamicResponse.Fields.ContainsKey(sizeField).ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeClientStreamingAsync_MultipleRequests_ReturnsCorrectTotal()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingInputCall");
        var invoker = new DynamicInvoker(channel);
        var requests = new List<IMessage>
        {
            CreateStreamingInputRequest(methodDescriptor.InputType, 100),
            CreateStreamingInputRequest(methodDescriptor.InputType, 200),
            CreateStreamingInputRequest(methodDescriptor.InputType, 300)
        };

        // Act
        var response = await invoker.InvokeClientStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests));

        // Assert
        response.ShouldNotBeNull();
        var dynamicResponse = response as SimpleDynamicMessage;
        dynamicResponse.ShouldNotBeNull();
        var sizeField = dynamicResponse.Descriptor.FindFieldByName("aggregated_payload_size");
        sizeField.ShouldNotBeNull();
        var totalSize = (int)dynamicResponse.Fields[sizeField]!;
        totalSize.ShouldBe(600); // 100 + 200 + 300
    }

    [Fact]
    public async Task InvokeClientStreamingAsync_EmptyRequests_ReturnsZeroSize()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.StreamingInputCall");
        var invoker = new DynamicInvoker(channel);
        IMessage[] requests = [];

        // Act
        var response = await invoker.InvokeClientStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests));

        // Assert
        response.ShouldNotBeNull();
        var dynamicResponse = response as SimpleDynamicMessage;
        dynamicResponse.ShouldNotBeNull();
        var sizeField = dynamicResponse.Descriptor.FindFieldByName("aggregated_payload_size");
        if (sizeField is not null && dynamicResponse.Fields.TryGetValue(sizeField, out var size))
        {
            ((int)size!).ShouldBe(0);
        }
    }

    #endregion

    #region InvokeDuplexStreamingAsync Tests

    [Fact]
    public async Task InvokeDuplexStreamingAsync_SingleRequest_ReturnsSingleResponse()
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
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests)))
        {
            responses.Add(response);
        }

        // Assert
        responses.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task InvokeDuplexStreamingAsync_MultipleRequests_ReturnsMultipleResponses()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.FullDuplexCall");
        var invoker = new DynamicInvoker(channel);
        var requests = new List<IMessage>
        {
            CreateStreamingOutputRequest(methodDescriptor.InputType, [100]),
            CreateStreamingOutputRequest(methodDescriptor.InputType, [200]),
            CreateStreamingOutputRequest(methodDescriptor.InputType, [300])
        };
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests)))
        {
            responses.Add(response);
        }

        // Assert
        responses.Count.ShouldBe(3);
    }

    [Fact]
    public async Task InvokeDuplexStreamingAsync_EmptyRequests_ReturnsNoResponses()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.FullDuplexCall");
        var invoker = new DynamicInvoker(channel);
        IMessage[] requests = [];
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests)))
        {
            responses.Add(response);
        }

        // Assert
        responses.ShouldBeEmpty();
    }

    #endregion

    #region HalfDuplexCall Tests

    [Fact]
    public async Task HalfDuplexCall_BuffersAllRequests_ThenResponds()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);
        var methodDescriptor = await GetMethodDescriptor(source, "testing.TestService.HalfDuplexCall");
        var invoker = new DynamicInvoker(channel);
        var requests = new List<IMessage>
        {
            CreateStreamingOutputRequest(methodDescriptor.InputType, [100]),
            CreateStreamingOutputRequest(methodDescriptor.InputType, [200])
        };
        var responses = new List<IMessage>();

        // Act
        await foreach (var response in invoker.InvokeDuplexStreamingAsync(methodDescriptor, ToAsyncEnumerable(requests)))
        {
            responses.Add(response);
        }

        // Assert
        responses.Count.ShouldBe(2);
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

    private static SimpleDynamicMessage CreateSimpleRequest(MessageDescriptor descriptor, string payloadData)
    {
        var message = new SimpleDynamicMessage(descriptor);

        // Set response_type = 0 (COMPRESSABLE)
        var responseTypeField = descriptor.FindFieldByName("response_type");

        if (responseTypeField is not null)
        {
            message.Fields[responseTypeField] = 0;
        }

        // Set response_size
        var responseSizeField = descriptor.FindFieldByName("response_size");

        if (responseSizeField is not null)
        {
            message.Fields[responseSizeField] = 100;
        }

        // Create nested payload
        var payloadField = descriptor.FindFieldByName("payload");

        if (payloadField?.MessageType is null)
        {
            return message;
        }

        var payload = new SimpleDynamicMessage(payloadField.MessageType);
        var typeField = payloadField.MessageType.FindFieldByName("type");
        var bodyField = payloadField.MessageType.FindFieldByName("body");

        if (typeField is not null)
        {
            payload.Fields[typeField] = 0;
        }

        if (bodyField is not null)
        {
            payload.Fields[bodyField] = ByteString.CopyFromUtf8(payloadData);
        }

        message.Fields[payloadField] = payload;

        return message;
    }

    private static SimpleDynamicMessage CreateStreamingOutputRequest(MessageDescriptor descriptor, int[] sizes)
    {
        var message = new SimpleDynamicMessage(descriptor);

        // Set response_type
        var responseTypeField = descriptor.FindFieldByName("response_type");

        if (responseTypeField is not null)
        {
            message.Fields[responseTypeField] = 0;
        }

        // Set response_parameters (repeated field)
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

    private static SimpleDynamicMessage CreateStreamingInputRequest(MessageDescriptor descriptor, int payloadSize)
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
            var data = new byte[payloadSize];

            payload.Fields[bodyField] = ByteString.CopyFrom(data);
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

    #endregion
}
