using Google.Protobuf.Reflection;
using Grpc.Net.Client;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Tests.Integration.Fixtures;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Tests.Integration.DescriptorSources;

[Collection("GrpcServer")]
public sealed class ReflectionSourceTests(GrpcTestFixture fixture)
{
    #region ListServicesAsync Tests

    [Fact]
    public async Task ListServicesAsync_ReturnsAvailableServices()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        services.ShouldNotBeEmpty();
        services.ShouldContain("testing.TestService");
        services.ShouldContain("testing.UnimplementedService");
    }

    [Fact]
    public async Task ListServicesAsync_ReturnsReflectionService()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        services.ShouldContain(s => s.Contains("ServerReflection"));
    }

    [Fact]
    public async Task ListServicesAsync_ServicesAreSorted()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        var sortedServices = services.OrderBy(s => s).ToList();
        
        services.ShouldBe(sortedServices);
    }

    [Fact]
    public async Task ListServicesAsync_MultipleCallsReturnSameResults()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var services1 = await source.ListServicesAsync(TestContext.Current.CancellationToken);
        var services2 = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        services1.ShouldBe(services2);
    }

    [Fact]
    public async Task ListServicesAsync_WithCancellation_ThrowsCanceledException()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);
        
        using var cts = new CancellationTokenSource();
        
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            source.ListServicesAsync(cts.Token));
    }

    #endregion

    #region FindSymbolAsync Tests - Services

    [Fact]
    public async Task FindSymbolAsync_ExistingService_ReturnsServiceDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<ServiceDescriptor>();
        symbol.FullName.ShouldBe("testing.TestService");
    }

    [Fact]
    public async Task FindSymbolAsync_NonExistentService_ReturnsNull()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.NonExistentService", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldBeNull();
    }

    [Fact]
    public async Task FindSymbolAsync_UnimplementedService_ReturnsServiceDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.UnimplementedService", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<ServiceDescriptor>();
    }

    #endregion

    #region FindSymbolAsync Tests - Methods

    [Fact]
    public async Task FindSymbolAsync_ExistingMethod_ReturnsMethodDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.TestService.UnaryCall", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<MethodDescriptor>();
        symbol.FullName.ShouldBe("testing.TestService.UnaryCall");
    }

    [Fact]
    public async Task FindSymbolAsync_AllMethodTypes_ReturnsCorrectDescriptors()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act - Empty call
        var empty = await source.FindSymbolAsync("testing.TestService.EmptyCall", TestContext.Current.CancellationToken);

        // Assert
        empty.ShouldNotBeNull();
        empty.ShouldBeOfType<MethodDescriptor>();

        // Act - Unary
        var unary = await source.FindSymbolAsync("testing.TestService.UnaryCall", TestContext.Current.CancellationToken);

        // Assert
        unary.ShouldNotBeNull();
        
        var unaryMethod = (MethodDescriptor)unary;
        
        unaryMethod.IsClientStreaming.ShouldBeFalse();
        unaryMethod.IsServerStreaming.ShouldBeFalse();

        // Act - Server streaming
        var serverStreaming = await source.FindSymbolAsync("testing.TestService.StreamingOutputCall", TestContext.Current.CancellationToken);

        // Assert
        serverStreaming.ShouldNotBeNull();
        
        var serverStreamingMethod = (MethodDescriptor)serverStreaming;
        
        serverStreamingMethod.IsClientStreaming.ShouldBeFalse();
        serverStreamingMethod.IsServerStreaming.ShouldBeTrue();

        // Act - Client streaming
        var clientStreaming = await source.FindSymbolAsync("testing.TestService.StreamingInputCall", TestContext.Current.CancellationToken);

        // Assert
        clientStreaming.ShouldNotBeNull();
        
        var clientStreamingMethod = (MethodDescriptor)clientStreaming;
        
        clientStreamingMethod.IsClientStreaming.ShouldBeTrue();
        clientStreamingMethod.IsServerStreaming.ShouldBeFalse();

        // Act - Bidirectional
        var bidi = await source.FindSymbolAsync("testing.TestService.FullDuplexCall", TestContext.Current.CancellationToken);

        // Assert
        bidi.ShouldNotBeNull();
        
        var bidiMethod = (MethodDescriptor)bidi;
        
        bidiMethod.IsClientStreaming.ShouldBeTrue();
        bidiMethod.IsServerStreaming.ShouldBeTrue();
    }

    #endregion

    #region FindSymbolAsync Tests - Messages

    [Fact]
    public async Task FindSymbolAsync_ExistingMessage_ReturnsMessageDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.SimpleRequest", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<MessageDescriptor>();
        symbol.FullName.ShouldBe("testing.SimpleRequest");
    }

    [Fact]
    public async Task FindSymbolAsync_AllMessages_ReturnsCorrectDescriptors()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);
        var messageNames = new[]
        {
            "testing.Empty",
            "testing.Payload",
            "testing.EchoStatus",
            "testing.SimpleRequest",
            "testing.SimpleResponse"
        };

        // Act & Assert
        foreach (var name in messageNames)
        {
            var symbol = await source.FindSymbolAsync(name, TestContext.Current.CancellationToken);

            symbol.ShouldNotBeNull();
            symbol.ShouldBeOfType<MessageDescriptor>();
            symbol.FullName.ShouldBe(name);
        }
    }

    #endregion

    #region FindSymbolAsync Tests - Enums

    [Fact]
    public async Task FindSymbolAsync_ExistingEnum_ReturnsEnumDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.PayloadType", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<EnumDescriptor>();
        symbol.FullName.ShouldBe("testing.PayloadType");
    }

    #endregion

    #region FindSymbolAsync Tests - Fields

    [Fact]
    public async Task FindSymbolAsync_FieldAccessedViaMessage_ReturnsFieldDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var messageSymbol = await source.FindSymbolAsync("testing.SimpleRequest", TestContext.Current.CancellationToken);

        // Assert
        messageSymbol.ShouldNotBeNull();
        messageSymbol.ShouldBeOfType<MessageDescriptor>();

        // Act - Access fields from the message descriptor
        var messageDescriptor = (MessageDescriptor)messageSymbol;
        var responseTypeField = messageDescriptor.FindFieldByName("response_type");
        var responseSizeField = messageDescriptor.FindFieldByName("response_size");

        // Assert
        responseTypeField.ShouldNotBeNull();
        responseSizeField.ShouldNotBeNull();
        responseTypeField.FieldType.ShouldBe(FieldType.Enum);
        responseSizeField.FieldType.ShouldBe(FieldType.Int32);
    }

    #endregion

    #region FileDescriptorSet Tests

    [Fact]
    public async Task FileDescriptorSet_AfterLoadingService_IsPopulated()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);

        // Assert
        source.FileDescriptorSet.ShouldNotBeNull();
        source.FileDescriptorSet.File.ShouldNotBeEmpty();
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task FindSymbolAsync_SequentialCalls_AllSucceed()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act & Assert
        for (var i = 0; i < 3; i++)
        {
            var result = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);

            result.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task ListServicesAsync_SequentialCalls_AllSucceed()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act & Assert
        for (var i = 0; i < 3; i++)
        {
            var result = await source.ListServicesAsync(TestContext.Current.CancellationToken);

            result.ShouldNotBeEmpty();
        }
    }

    #endregion

    #region Create Factory Method Tests

    [Fact]
    public async Task Create_WithAddress_CreatesWorkingReflectionSource()
    {
        // Arrange
        var channelOptions = new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true
            }
        };

        // Act
        using var source = ReflectionSource.Create($"http://{fixture.Address}", channelOptions);
        
        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        services.ShouldNotBeEmpty();
        services.ShouldContain("testing.TestService");
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WithOwnedChannel_DoesNotThrow()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress($"http://{fixture.Address}", new GrpcChannelOptions());
        var source = new ReflectionSource(channel, ownsChannel: true);

        // Act & Assert
        Should.NotThrow(() => source.Dispose());
    }

    #endregion

    #region FindSymbolAsync Tests - Edge Cases

    [Fact]
    public async Task FindSymbolAsync_NonExistentSymbol_ReturnsNull()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("nonexistent.Symbol", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldBeNull();
    }

    [Fact]
    public async Task FindSymbolAsync_WithCancelledToken_ThrowsOperationCancelled()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);
        
        using var cts = new CancellationTokenSource();
        
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            source.FindSymbolAsync("testing.TestService", cts.Token));
    }

    [Fact]
    public async Task FindSymbolAsync_MapFieldMessage_ReturnsMessageDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.MapFieldsMessage", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<MessageDescriptor>();
        symbol.FullName.ShouldBe("testing.MapFieldsMessage");
    }

    [Fact]
    public async Task FindSymbolAsync_OneofMessage_ReturnsMessageDescriptor()
    {
        // Arrange
        using var channel = CreateChannel();
        
        var source = new ReflectionSource(channel);

        // Act
        var symbol = await source.FindSymbolAsync("testing.OneofMessage", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<MessageDescriptor>();
        symbol.FullName.ShouldBe("testing.OneofMessage");
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

    #endregion
}
