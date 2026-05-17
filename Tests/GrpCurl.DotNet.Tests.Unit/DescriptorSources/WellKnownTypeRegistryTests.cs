using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;

namespace GrpCurl.Net.Tests.Unit.DescriptorSources;

public sealed class WellKnownTypeRegistryTests
{
    #region Descriptors Dictionary Tests

    [Fact]
    public void Descriptors_ShouldNotBeNull()
    {
        // Act
        var descriptors = WellKnownTypeRegistry.Descriptors;

        // Assert
        descriptors.ShouldNotBeNull();
    }

    [Fact]
    public void Descriptors_ShouldNotBeEmpty()
    {
        // Act
        var descriptors = WellKnownTypeRegistry.Descriptors;

        // Assert
        descriptors.Count.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("google/protobuf/timestamp.proto")]
    [InlineData("google/protobuf/duration.proto")]
    [InlineData("google/protobuf/empty.proto")]
    [InlineData("google/protobuf/field_mask.proto")]
    [InlineData("google/protobuf/any.proto")]
    [InlineData("google/protobuf/struct.proto")]
    [InlineData("google/protobuf/wrappers.proto")]
    [InlineData("google/protobuf/api.proto")]
    [InlineData("google/protobuf/source_context.proto")]
    [InlineData("google/protobuf/type.proto")]
    public void Descriptors_ContainsExpectedWellKnownType(string protoFileName)
    {
        // Act
        var descriptors = WellKnownTypeRegistry.Descriptors;

        // Assert
        descriptors.ShouldContainKey(protoFileName);
    }

    [Fact]
    public void Descriptors_ContainsDescriptorProto()
    {
        // Act - the "meta" proto that describes all protos
        var descriptors = WellKnownTypeRegistry.Descriptors;

        // Assert
        descriptors.ShouldContainKey("google/protobuf/descriptor.proto");
    }

    [Theory]
    [InlineData("google/protobuf/timestamp.proto")]
    [InlineData("google/protobuf/duration.proto")]
    [InlineData("google/protobuf/empty.proto")]
    public void Descriptors_ValuesAreValidFileDescriptors(string protoFileName)
    {
        // Act
        var descriptors = WellKnownTypeRegistry.Descriptors;
        var descriptor = descriptors[protoFileName];

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.ShouldBeAssignableTo<FileDescriptor>();
        descriptor.Name.ShouldBe(protoFileName);
    }

    #endregion

    #region TryGetDescriptor Tests - Known Types

    [Theory]
    [InlineData("google/protobuf/timestamp.proto")]
    [InlineData("google/protobuf/duration.proto")]
    [InlineData("google/protobuf/empty.proto")]
    [InlineData("google/protobuf/field_mask.proto")]
    [InlineData("google/protobuf/any.proto")]
    [InlineData("google/protobuf/struct.proto")]
    [InlineData("google/protobuf/wrappers.proto")]
    [InlineData("google/protobuf/descriptor.proto")]
    public void TryGetDescriptor_KnownType_ReturnsTrue(string protoFileName)
    {
        // Act
        var result = WellKnownTypeRegistry.TryGetDescriptor(protoFileName, out var descriptor);

        // Assert
        result.ShouldBeTrue();
        
        descriptor.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("google/protobuf/timestamp.proto")]
    [InlineData("google/protobuf/duration.proto")]
    [InlineData("google/protobuf/empty.proto")]
    public void TryGetDescriptor_KnownType_ReturnsCorrectDescriptor(string protoFileName)
    {
        // Act
        WellKnownTypeRegistry.TryGetDescriptor(protoFileName, out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.Name.ShouldBe(protoFileName);
    }

    #endregion

    #region TryGetDescriptor Tests - Unknown Types

    [Fact]
    public void TryGetDescriptor_UnknownType_ReturnsFalse()
    {
        // Act
        var result = WellKnownTypeRegistry.TryGetDescriptor("nonexistent/type.proto", out var descriptor);

        // Assert
        result.ShouldBeFalse();
        
        descriptor.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("random.proto")]
    [InlineData("google/protobuf/nonexistent.proto")]
    [InlineData("my/custom/service.proto")]
    public void TryGetDescriptor_VariousUnknownTypes_ReturnsFalse(string protoFileName)
    {
        // Act
        var result = WellKnownTypeRegistry.TryGetDescriptor(protoFileName, out var descriptor);

        // Assert
        result.ShouldBeFalse();
        
        descriptor.ShouldBeNull();
    }

    [Fact]
    public void TryGetDescriptor_CaseSensitive_ReturnsFalseForWrongCase()
    {
        // Act - proto file names are case-sensitive
        var result = WellKnownTypeRegistry.TryGetDescriptor("Google/Protobuf/Timestamp.proto", out var descriptor);

        // Assert
        result.ShouldBeFalse();
        
        descriptor.ShouldBeNull();
    }

    #endregion

    #region Descriptor Content Validation Tests

    [Fact]
    public void Descriptors_TimestampHasExpectedMessages()
    {
        // Arrange
        WellKnownTypeRegistry.TryGetDescriptor("google/protobuf/timestamp.proto", out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.MessageTypes.ShouldNotBeEmpty();
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Timestamp");
    }

    [Fact]
    public void Descriptors_DurationHasExpectedMessages()
    {
        // Arrange
        WellKnownTypeRegistry.TryGetDescriptor("google/protobuf/duration.proto", out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.MessageTypes.ShouldNotBeEmpty();
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Duration");
    }

    [Fact]
    public void Descriptors_EmptyHasExpectedMessages()
    {
        // Arrange
        WellKnownTypeRegistry.TryGetDescriptor("google/protobuf/empty.proto", out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.MessageTypes.ShouldNotBeEmpty();
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Empty");
    }

    [Fact]
    public void Descriptors_WrappersHasExpectedMessages()
    {
        // Arrange
        WellKnownTypeRegistry.TryGetDescriptor("google/protobuf/wrappers.proto", out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.MessageTypes.ShouldNotBeEmpty();
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Int32Value");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "StringValue");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "BoolValue");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "DoubleValue");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "FloatValue");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Int64Value");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "UInt32Value");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "UInt64Value");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "BytesValue");
    }

    [Fact]
    public void Descriptors_AnyHasExpectedMessages()
    {
        // Arrange
        WellKnownTypeRegistry.TryGetDescriptor("google/protobuf/any.proto", out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.MessageTypes.ShouldNotBeEmpty();
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Any");
    }

    [Fact]
    public void Descriptors_StructHasExpectedMessages()
    {
        // Arrange
        WellKnownTypeRegistry.TryGetDescriptor("google/protobuf/struct.proto", out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.MessageTypes.ShouldNotBeEmpty();
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Struct");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "Value");
        descriptor.MessageTypes.ShouldContain(m => m.Name == "ListValue");
    }

    [Fact]
    public void Descriptors_FieldMaskHasExpectedMessages()
    {
        // Arrange
        WellKnownTypeRegistry.TryGetDescriptor("google/protobuf/field_mask.proto", out var descriptor);

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.MessageTypes.ShouldNotBeEmpty();
        descriptor.MessageTypes.ShouldContain(m => m.Name == "FieldMask");
    }

    #endregion

    #region Thread Safety / Lazy Initialization Tests

    [Fact]
    public void Descriptors_MultipleCalls_ReturnSameInstance()
    {
        // Act
        var descriptors1 = WellKnownTypeRegistry.Descriptors;
        var descriptors2 = WellKnownTypeRegistry.Descriptors;

        // Assert - Lazy<T> ensures same instance
        ReferenceEquals(descriptors1, descriptors2).ShouldBeTrue();
    }

    [Fact]
    public async Task TryGetDescriptor_MultipleConcurrentCalls_AllSucceed()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 20)
                              .Select(_ => Task.Run(() =>
                              {
                                  var result = WellKnownTypeRegistry.TryGetDescriptor(
                                      "google/protobuf/timestamp.proto", out var descriptor);
                                  return (result, descriptor);
                              }))
                              .ToArray();

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var (result, descriptor) in results)
        {
            result.ShouldBeTrue();
            descriptor.ShouldNotBeNull();
            descriptor!.Name.ShouldBe("google/protobuf/timestamp.proto");
        }
    }

    [Fact]
    public async Task Descriptors_ConcurrentAccess_AllReturnSameInstance()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(0, 20)
                              .Select(_ => Task.Run(() => WellKnownTypeRegistry.Descriptors))
                              .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - all tasks should return the same dictionary instance
        var firstResult = results[0];
        
        foreach (var result in results)
        {
            ReferenceEquals(result, firstResult).ShouldBeTrue();
        }
    }

    #endregion

    #region Recursive Dependency Registration Tests

    [Fact]
    public void Descriptors_IncludesDependenciesOfRegisteredTypes()
    {
        // Act - types like api.proto depend on source_context.proto and type.proto
        // which in turn may depend on any.proto. All should be registered.
        var descriptors = WellKnownTypeRegistry.Descriptors;

        // Assert - verify that transitive dependencies are included
        descriptors.ShouldContainKey("google/protobuf/source_context.proto");
        descriptors.ShouldContainKey("google/protobuf/type.proto");
        descriptors.ShouldContainKey("google/protobuf/any.proto");
    }

    #endregion
}
