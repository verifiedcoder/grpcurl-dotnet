using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;

namespace GrpCurl.Net.Tests.Unit.DescriptorSources;

public sealed class ProtosetSourceTests
{
    private readonly string _testProtosetPath;
    private readonly string _emptyProtosetPath;
    private readonly string _invalidProtosetPath;
    private readonly string _wellKnownTypesProtosetPath;

    public ProtosetSourceTests()
    {
        var outputDir = Path.GetDirectoryName(typeof(ProtosetSourceTests).Assembly.Location);

        _testProtosetPath = Path.Combine(outputDir!, "TestProtosets", "test.protoset");
        _emptyProtosetPath = Path.Combine(outputDir!, "TestProtosets", "empty.protoset");
        _invalidProtosetPath = Path.Combine(outputDir!, "TestProtosets", "invalid.protoset");
        _wellKnownTypesProtosetPath = Path.Combine(outputDir!, "TestProtosets", "well-known-types.protoset");
    }

    #region LoadFromFileAsync Tests

    [Fact]
    public async Task LoadFromFileAsync_ValidProtoset_LoadsSuccessfully()
    {
        // Arrange

        // Act
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Assert
        source.ShouldNotBeNull();
        source.FileDescriptorSet.ShouldNotBeNull();
    }

    [Fact]
    public async Task LoadFromFileAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".protoset");

        // Act
        // Assert
        await Should.ThrowAsync<FileNotFoundException>(() =>
            ProtosetSource.LoadFromFileAsync(nonExistentPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadFromFileAsync_InvalidProtoset_ThrowsException()
    {
        // Arrange
        if (!File.Exists(_invalidProtosetPath))
        {
            await File.WriteAllTextAsync(_invalidProtosetPath, "not a valid protoset", TestContext.Current.CancellationToken);
        }

        // Act
        // Assert
        await Should.ThrowAsync<Exception>(() =>
            ProtosetSource.LoadFromFileAsync(_invalidProtosetPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadFromFileAsync_EmptyProtoset_LoadsSuccessfully()
    {
        // Arrange

        // Act
        var source = await ProtosetSource.LoadFromFileAsync(_emptyProtosetPath, TestContext.Current.CancellationToken);

        // Assert
        source.ShouldNotBeNull();
    }

    [Fact]
    public async Task LoadFromFileAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        // Act
        // Assert
        await Should.ThrowAsync<TaskCanceledException>(() =>
            ProtosetSource.LoadFromFileAsync(_testProtosetPath, cts.Token));
    }

    [Fact]
    public async Task LoadFromFileAsync_FileExceedsConfiguredLimit_ThrowsInvalidDataException()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".protoset");

        await File.WriteAllBytesAsync(path, [1, 2], TestContext.Current.CancellationToken);

        var options = DescriptorSourceOptions.Default with
        {
            MaxProtosetFileBytes = 1
        };

        try
        {
            // Act
            var exception = await Should.ThrowAsync<InvalidDataException>(() =>
                ProtosetSource.LoadFromFileAsync(path, options, TestContext.Current.CancellationToken));

            // Assert
            exception.Message.ShouldContain("Protoset file");
            exception.Message.ShouldContain("maximum allowed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadFromFileAsync_FileDescriptorCountExceedsConfiguredLimit_ThrowsInvalidDataException()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".protoset");
        var descriptorSet = new FileDescriptorSet
        {
            File =
            {
                new FileDescriptorProto { Name = "a.proto" },
                new FileDescriptorProto { Name = "b.proto" }
            }
        };

        await File.WriteAllBytesAsync(path, descriptorSet.ToByteArray(), TestContext.Current.CancellationToken);

        var options = DescriptorSourceOptions.Default with
        {
            MaxFileDescriptors = 1
        };

        try
        {
            // Act
            var exception = await Should.ThrowAsync<InvalidDataException>(() =>
                ProtosetSource.LoadFromFileAsync(path, options, TestContext.Current.CancellationToken));

            // Assert
            exception.Message.ShouldContain("File descriptor count");
            exception.Message.ShouldContain("exceeds");
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region LoadFromFilesAsync Tests

    [Fact]
    public async Task LoadFromFilesAsync_MultipleProtosets_LoadsAll()
    {
        // Arrange
        var paths = new[] { _testProtosetPath, _wellKnownTypesProtosetPath };

        // Act
        var source = await ProtosetSource.LoadFromFilesAsync(paths, TestContext.Current.CancellationToken);

        // Assert
        source.ShouldNotBeNull();

        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        services.ShouldContain(s => s.Contains("TestService"));
    }

    [Fact]
    public async Task LoadFromFilesAsync_EmptyCollection_ReturnsEmptySource()
    {
        // Arrange
        var emptyPaths = Array.Empty<string>();

        // Act
        var source = await ProtosetSource.LoadFromFilesAsync(emptyPaths, TestContext.Current.CancellationToken);

        // Assert
        source.ShouldNotBeNull();

        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        services.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadFromFilesAsync_SingleFile_LoadsSuccessfully()
    {
        // Arrange
        var paths = new[] { _testProtosetPath };

        // Act
        var source = await ProtosetSource.LoadFromFilesAsync(paths, TestContext.Current.CancellationToken);

        // Assert
        source.ShouldNotBeNull();

        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        services.ShouldNotBeEmpty();
    }

    #endregion

    #region ListServicesAsync Tests

    [Fact]
    public async Task ListServicesAsync_ValidProtoset_ReturnsServices()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        services.ShouldNotBeEmpty();
        services.ShouldContain("testing.TestService");
        services.ShouldContain("testing.UnimplementedService");
    }

    [Fact]
    public async Task ListServicesAsync_ServicesAreSorted()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        var sortedServices = services.OrderBy(s => s).ToList();

        services.ShouldBe(sortedServices);
    }

    [Fact]
    public async Task ListServicesAsync_EmptyProtoset_ReturnsEmptyList()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_emptyProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var services = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        services.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListServicesAsync_MultipleCallsReturnSameResults()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var services1 = await source.ListServicesAsync(TestContext.Current.CancellationToken);
        var services2 = await source.ListServicesAsync(TestContext.Current.CancellationToken);

        // Assert
        services1.ShouldBe(services2);
    }

    #endregion

    #region FindSymbolAsync Tests - Services

    [Fact]
    public async Task FindSymbolAsync_ExistingService_ReturnsServiceDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

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
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.NonExistentService", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldBeNull();
    }

    #endregion

    #region FindSymbolAsync Tests - Methods

    [Fact]
    public async Task FindSymbolAsync_ExistingMethod_ReturnsMethodDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

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
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var unary = await source.FindSymbolAsync("testing.TestService.UnaryCall", TestContext.Current.CancellationToken);
        var serverStreaming = await source.FindSymbolAsync("testing.TestService.StreamingOutputCall", TestContext.Current.CancellationToken);
        var clientStreaming = await source.FindSymbolAsync("testing.TestService.StreamingInputCall", TestContext.Current.CancellationToken);
        var bidi = await source.FindSymbolAsync("testing.TestService.FullDuplexCall", TestContext.Current.CancellationToken);

        // Assert
        unary.ShouldNotBeNull();
        unary.ShouldBeOfType<MethodDescriptor>();

        serverStreaming.ShouldNotBeNull();
        serverStreaming.ShouldBeOfType<MethodDescriptor>();

        clientStreaming.ShouldNotBeNull();
        clientStreaming.ShouldBeOfType<MethodDescriptor>();

        bidi.ShouldNotBeNull();
        bidi.ShouldBeOfType<MethodDescriptor>();
    }

    #endregion

    #region FindSymbolAsync Tests - Messages

    [Fact]
    public async Task FindSymbolAsync_ExistingMessage_ReturnsMessageDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

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
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var messageNames = new[]
        {
            "testing.Empty",
            "testing.Payload",
            "testing.EchoStatus",
            "testing.SimpleRequest",
            "testing.SimpleResponse",
            "testing.StreamingInputCallRequest",
            "testing.StreamingInputCallResponse",
            "testing.ResponseParameters",
            "testing.StreamingOutputCallRequest",
            "testing.StreamingOutputCallResponse"
        };

        // Act
        // Assert
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
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.PayloadType", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<EnumDescriptor>();
        symbol.FullName.ShouldBe("testing.PayloadType");
    }

    [Fact]
    public async Task FindSymbolAsync_EnumValue_ReturnsEnumValueDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.PayloadType.COMPRESSABLE", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<EnumValueDescriptor>();
    }

    #endregion

    #region FindSymbolAsync Tests - Fields

    [Fact]
    public async Task FindSymbolAsync_ExistingField_ReturnsFieldDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.SimpleRequest.response_size", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<FieldDescriptor>();
    }

    [Fact]
    public async Task FindSymbolAsync_NestedMessageField_ReturnsFieldDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.SimpleRequest.payload", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();

        var field = symbol.ShouldBeOfType<FieldDescriptor>();

        field.FieldType.ShouldBe(FieldType.Message);
    }

    [Fact]
    public async Task FindSymbolAsync_RepeatedField_ReturnsFieldDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.StreamingOutputCallRequest.response_parameters", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();

        var field = symbol.ShouldBeOfType<FieldDescriptor>();

        field.IsRepeated.ShouldBeTrue();
    }

    #endregion

    #region FindSymbolAsync Tests - Edge Cases

    [Fact]
    public async Task FindSymbolAsync_EmptyName_ReturnsNull()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldBeNull();
    }

    [Fact]
    public async Task FindSymbolAsync_PartialName_ReturnsNull()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.Simple", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldBeNull();
    }

    [Fact]
    public async Task FindSymbolAsync_CaseSensitive_ReturnsNull()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.TESTSERVICE", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldBeNull();
    }

    #endregion

    #region FileDescriptorSet Tests

    [Fact]
    public async Task FileDescriptorSet_AfterLoading_IsPopulated()
    {
        // Arrange

        // Act
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Assert
        source.FileDescriptorSet.ShouldNotBeNull();
        source.FileDescriptorSet.File.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task FileDescriptorSet_ContainsAllProtoFiles()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var fileNames = source.FileDescriptorSet!.File.Select(f => f.Name).ToList();

        // Assert
        fileNames.ShouldContain(f => f.Contains("test.proto"));
    }

    #endregion

    #region Well-Known Types Tests

    [Fact]
    public async Task LoadFromFileAsync_WellKnownTypes_LoadsSuccessfully()
    {
        // Arrange

        // Act
        var source = await ProtosetSource.LoadFromFileAsync(_wellKnownTypesProtosetPath, TestContext.Current.CancellationToken);

        // Assert
        source.ShouldNotBeNull();
    }

    [Fact]
    public async Task FindSymbolAsync_WellKnownTimestamp_ReturnsDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_wellKnownTypesProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("google.protobuf.Timestamp", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<MessageDescriptor>();
    }

    [Fact]
    public async Task FindSymbolAsync_WellKnownDuration_ReturnsDescriptor()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_wellKnownTypesProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("google.protobuf.Duration", TestContext.Current.CancellationToken);

        // Assert
        symbol.ShouldNotBeNull();
        symbol.ShouldBeOfType<MessageDescriptor>();
    }

    #endregion

    #region Method Descriptor Properties Tests

    [Fact]
    public async Task MethodDescriptor_UnaryCall_HasCorrectProperties()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.TestService.UnaryCall", TestContext.Current.CancellationToken) as MethodDescriptor;

        // Assert
        symbol.ShouldNotBeNull();
        symbol.IsClientStreaming.ShouldBeFalse();
        symbol.IsServerStreaming.ShouldBeFalse();
        symbol.InputType.FullName.ShouldBe("testing.SimpleRequest");
        symbol.OutputType.FullName.ShouldBe("testing.SimpleResponse");
    }

    [Fact]
    public async Task MethodDescriptor_ServerStreaming_HasCorrectProperties()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.TestService.StreamingOutputCall", TestContext.Current.CancellationToken) as MethodDescriptor;

        // Assert
        symbol.ShouldNotBeNull();
        symbol.IsClientStreaming.ShouldBeFalse();
        symbol.IsServerStreaming.ShouldBeTrue();
    }

    [Fact]
    public async Task MethodDescriptor_ClientStreaming_HasCorrectProperties()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.TestService.StreamingInputCall", TestContext.Current.CancellationToken) as MethodDescriptor;

        // Assert
        symbol.ShouldNotBeNull();
        symbol.IsClientStreaming.ShouldBeTrue();
        symbol.IsServerStreaming.ShouldBeFalse();
    }

    [Fact]
    public async Task MethodDescriptor_BidirectionalStreaming_HasCorrectProperties()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);

        // Act
        var symbol = await source.FindSymbolAsync("testing.TestService.FullDuplexCall", TestContext.Current.CancellationToken) as MethodDescriptor;

        // Assert
        symbol.ShouldNotBeNull();
        symbol.IsClientStreaming.ShouldBeTrue();
        symbol.IsServerStreaming.ShouldBeTrue();
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ListServicesAsync_ConcurrentCalls_AllSucceed()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => source.ListServicesAsync(TestContext.Current.CancellationToken))
            .ToList();

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var result in results)
        {
            result.ShouldNotBeEmpty();
            result.ShouldBe(results[0]);
        }
    }

    [Fact]
    public async Task FindSymbolAsync_ConcurrentCalls_AllSucceed()
    {
        // Arrange
        var source = await ProtosetSource.LoadFromFileAsync(_testProtosetPath, TestContext.Current.CancellationToken);
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken))
            .ToList();

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var result in results)
        {
            result.ShouldNotBeNull();
        }
    }

    #endregion
}
