using Google.Protobuf;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Unit.Fixtures;
using System.Text;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class SimpleDynamicMessageTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithDescriptor_CreatesEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor);

        // Assert
        message.ShouldNotBeNull();
        message.Descriptor.ShouldBe(descriptor);
        message.Fields.ShouldBeEmpty();
        message.RepeatedFields.ShouldBeEmpty();
        message.MapFields.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithNullJson_CreatesEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, null);

        // Assert
        message.ShouldNotBeNull();
        message.Fields.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithEmptyJson_CreatesEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, "{}");

        // Assert
        message.ShouldNotBeNull();
        message.Fields.ShouldBeEmpty();
    }

    #endregion

    #region Scalar Field Tests

    [Fact]
    public void ParseJson_Int32Field_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"responseSize": 42}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("response_size");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
        message.Fields[field].ShouldBe(42);
    }

    [Fact]
    public void ParseJson_BoolField_ParsesTrue()
    {
        // Arrange
        const string json = """{"fillUsername": true}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("fill_username");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
        message.Fields[field].ShouldBe(true);
    }

    [Fact]
    public void ParseJson_BoolField_ParsesFalse()
    {
        // Arrange
        const string json = """{"fillUsername": false}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("fill_username");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
        message.Fields[field].ShouldBe(false);
    }

    #endregion

    #region Enum Field Tests

    [Fact]
    public void ParseJson_EnumField_StringName_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"responseType": "COMPRESSABLE"}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("response_type");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
        message.Fields[field].ShouldBe(0); // COMPRESSABLE = 0
    }

    [Fact]
    public void ParseJson_EnumField_NumericValue_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"responseType": 1}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("response_type");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
        message.Fields[field].ShouldBe(1); // UNCOMPRESSABLE = 1
    }

    [Fact]
    public void ParseJson_EnumField_UnknownName_ThrowsException()
    {
        // Arrange
        const string json = """{"responseType": "INVALID_ENUM"}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => new SimpleDynamicMessage(descriptor, json));
        ex.Message.ShouldContain("Unknown enum value");
    }

    #endregion

    #region Nested Message Tests

    [Fact]
    public void ParseJson_NestedMessage_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"payload": {"type": "COMPRESSABLE", "body": "dGVzdA=="}}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var payloadField = descriptor.FindFieldByName("payload");
        payloadField.ShouldNotBeNull();
        message.Fields.ContainsKey(payloadField).ShouldBeTrue();

        var nestedMessage = message.Fields[payloadField].ShouldBeOfType<SimpleDynamicMessage>();
        var typeField = nestedMessage.Descriptor.FindFieldByName("type");
        typeField.ShouldNotBeNull();
        nestedMessage.Fields[typeField].ShouldBe(0); // COMPRESSABLE = 0
    }

    [Fact]
    public void ParseJson_NullNestedMessage_ParsesAsNull()
    {
        // Arrange
        const string json = """{"payload": null}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var payloadField = descriptor.FindFieldByName("payload");
        payloadField.ShouldNotBeNull();
        message.Fields.ContainsKey(payloadField).ShouldBeTrue();
        message.Fields[payloadField].ShouldBeNull();
    }

    #endregion

    #region Repeated Field Tests

    [Fact]
    public void ParseJson_RepeatedField_EmptyArray_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"responseParameters": []}""";
        var descriptor = TestDescriptorProvider.StreamingOutputCallRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("response_parameters");
        field.ShouldNotBeNull();
        message.RepeatedFields.ContainsKey(field).ShouldBeTrue();
        message.RepeatedFields[field].ShouldBeEmpty();
    }

    [Fact]
    public void ParseJson_RepeatedField_SingleElement_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"responseParameters": [{"size": 100}]}""";
        var descriptor = TestDescriptorProvider.StreamingOutputCallRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("response_parameters");
        field.ShouldNotBeNull();
        message.RepeatedFields.ContainsKey(field).ShouldBeTrue();
        message.RepeatedFields[field].ShouldHaveSingleItem();

        var nestedMessage = message.RepeatedFields[field][0].ShouldBeOfType<SimpleDynamicMessage>();
        nestedMessage.ShouldNotBeNull();
    }

    [Fact]
    public void ParseJson_RepeatedField_MultipleElements_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"responseParameters": [{"size": 100}, {"size": 200}, {"size": 300}]}""";
        var descriptor = TestDescriptorProvider.StreamingOutputCallRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("response_parameters");
        field.ShouldNotBeNull();
        message.RepeatedFields.ContainsKey(field).ShouldBeTrue();
        message.RepeatedFields[field].Count.ShouldBe(3);
    }

    [Fact]
    public void ParseJson_RepeatedField_NullElement_ThrowsException()
    {
        // Arrange
        const string json = """{"responseParameters": [{"size": 100}, null, {"size": 200}]}""";
        var descriptor = TestDescriptorProvider.StreamingOutputCallRequest;

        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => new SimpleDynamicMessage(descriptor, json));
        ex.Message.ShouldContain("Null values are not allowed in repeated field");
    }

    #endregion

    #region Unknown Field Tests

    [Fact]
    public void ParseJson_UnknownField_AllowUnknownTrue_SkipsField()
    {
        // Arrange
        const string json = """{"responseSize": 42, "unknownField": "value"}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json, allowUnknownFields: true);

        // Assert
        message.UnknownFields.ShouldHaveSingleItem();
        message.UnknownFields.ShouldContain("unknownField");
    }

    [Fact]
    public void ParseJson_UnknownField_AllowUnknownFalse_ThrowsException()
    {
        // Arrange
        const string json = """{"responseSize": 42, "unknownField": "value"}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() =>
            new SimpleDynamicMessage(descriptor, json, allowUnknownFields: false));

        ex.Message.ShouldContain("Unknown field 'unknownField'");
        ex.Message.ShouldContain("--allow-unknown-fields");
    }

    [Fact]
    public void UnknownFields_Property_ReturnsCollectedUnknownFields()
    {
        // Arrange
        const string json = """{"unknownField1": 1, "unknownField2": "test", "responseSize": 42}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json, allowUnknownFields: true);

        // Assert
        message.UnknownFields.Count.ShouldBe(2);
        message.UnknownFields.ShouldContain("unknownField1");
        message.UnknownFields.ShouldContain("unknownField2");
    }

    #endregion

    #region JSON Serialization Tests (ToJson)

    [Fact]
    public void ToJson_EmptyMessage_ReturnsEmptyObject()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldBe("{}");
    }

    [Fact]
    public void ToJson_Int32Field_SerializesCorrectly()
    {
        // Arrange
        const string json = """{"responseSize": 42}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var output = message.ToJson();

        // Assert
        output.ShouldContain("\"responseSize\":");
        output.ShouldContain("42");
    }

    [Fact]
    public void ToJson_BoolField_SerializesCorrectly()
    {
        // Arrange
        const string json = """{"fillUsername": true}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var output = message.ToJson();

        // Assert
        output.ShouldContain("\"fillUsername\":true");
    }

    [Fact]
    public void ToJson_EnumField_SerializesAsString()
    {
        // Arrange
        const string json = """{"responseType": "COMPRESSABLE"}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var output = message.ToJson();

        // Assert
        output.ShouldContain("\"responseType\":\"COMPRESSABLE\"");
    }

    [Fact]
    public void ToJson_NestedMessage_SerializesCorrectly()
    {
        // Arrange
        const string json = """{"payload": {"type": "COMPRESSABLE"}}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var output = message.ToJson();

        // Assert
        output.ShouldContain("\"payload\":{");
        output.ShouldContain("\"type\":\"COMPRESSABLE\"");
    }

    [Fact]
    public void ToJson_RepeatedField_SerializesAsArray()
    {
        // Arrange
        const string json = """{"responseParameters": [{"size": 100}, {"size": 200}]}""";
        var descriptor = TestDescriptorProvider.StreamingOutputCallRequest;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var output = message.ToJson();

        // Assert
        output.ShouldContain("\"responseParameters\":[");
    }

    #endregion

    #region Round-Trip Tests (JSON -> Binary -> JSON)

    [Fact]
    public void RoundTrip_SimpleMessage_PreservesData()
    {
        // Arrange
        const string originalJson = """{"responseSize": 42, "fillUsername": true}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, originalJson);

        // Act
        var binaryData = new byte[message.CalculateSize()];
        using (var output = new CodedOutputStream(binaryData))
        {
            message.WriteTo(output);
        }

        var parsedMessage = new SimpleDynamicMessage(descriptor);
        using (var input = new CodedInputStream(binaryData))
        {
            parsedMessage.MergeFrom(input);
        }

        // Assert
        var field = descriptor.FindFieldByName("response_size");
        field.ShouldNotBeNull();
        parsedMessage.Fields.ContainsKey(field).ShouldBeTrue();
        parsedMessage.Fields[field].ShouldBe(42);
    }

    [Fact]
    public void RoundTrip_NestedMessage_PreservesData()
    {
        // Arrange
        const string originalJson = """{"payload": {"type": "UNCOMPRESSABLE", "body": "dGVzdA=="}}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, originalJson);

        // Act
        var binaryData = new byte[message.CalculateSize()];
        using (var output = new CodedOutputStream(binaryData))
        {
            message.WriteTo(output);
        }

        var parsedMessage = new SimpleDynamicMessage(descriptor);
        using (var input = new CodedInputStream(binaryData))
        {
            parsedMessage.MergeFrom(input);
        }

        // Assert
        var payloadField = descriptor.FindFieldByName("payload");
        payloadField.ShouldNotBeNull();
        parsedMessage.Fields.ContainsKey(payloadField).ShouldBeTrue();

        var nestedMessage = parsedMessage.Fields[payloadField].ShouldBeOfType<SimpleDynamicMessage>();
        var typeField = nestedMessage.Descriptor.FindFieldByName("type");
        typeField.ShouldNotBeNull();
        nestedMessage.Fields[typeField].ShouldBe(1); // UNCOMPRESSABLE = 1
    }

    [Fact]
    public void RoundTrip_RepeatedField_PreservesData()
    {
        // Arrange
        const string originalJson = """{"responseParameters": [{"size": 100}, {"size": 200}, {"size": 300}]}""";
        var descriptor = TestDescriptorProvider.StreamingOutputCallRequest;
        var message = new SimpleDynamicMessage(descriptor, originalJson);

        // Act
        var binaryData = new byte[message.CalculateSize()];
        using (var output = new CodedOutputStream(binaryData))
        {
            message.WriteTo(output);
        }

        var parsedMessage = new SimpleDynamicMessage(descriptor);
        using (var input = new CodedInputStream(binaryData))
        {
            parsedMessage.MergeFrom(input);
        }

        // Assert
        var field = descriptor.FindFieldByName("response_parameters");
        field.ShouldNotBeNull();
        parsedMessage.RepeatedFields.ContainsKey(field).ShouldBeTrue();
        parsedMessage.RepeatedFields[field].Count.ShouldBe(3);
    }

    #endregion

    #region Bytes Field Tests

    [Fact]
    public void ParseJson_BytesField_Base64_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"body": "SGVsbG8gV29ybGQ="}"""; // "Hello World" in base64
        var descriptor = TestDescriptorProvider.Payload;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("body");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();

        var byteString = message.Fields[field].ShouldBeOfType<ByteString>();
        Encoding.UTF8.GetString(byteString.ToByteArray()).ShouldBe("Hello World");
    }

    [Fact]
    public void ParseJson_BytesField_EmptyString_ParsesAsEmpty()
    {
        // Arrange
        const string json = """{"body": ""}""";
        var descriptor = TestDescriptorProvider.Payload;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("body");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();

        var byteString = message.Fields[field].ShouldBeOfType<ByteString>();
        byteString.ToByteArray().ShouldBeEmpty();
    }

    [Fact]
    public void ToJson_BytesField_SerializesAsBase64()
    {
        // Arrange
        const string json = """{"body": "SGVsbG8="}"""; // "Hello" in base64
        var descriptor = TestDescriptorProvider.Payload;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var output = message.ToJson();

        // Assert
        output.ShouldContain("\"body\":\"SGVsbG8=\"");
    }

    #endregion

    #region Size Calculation Tests

    [Fact]
    public void CalculateSize_EmptyMessage_ReturnsZero()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var size = message.CalculateSize();

        // Assert
        size.ShouldBe(0);
    }

    [Fact]
    public void CalculateSize_WithFields_ReturnsCorrectSize()
    {
        // Arrange
        const string json = """{"responseSize": 42}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var size = message.CalculateSize();

        // Assert
        size.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CalculateSize_MatchesActualWrittenSize()
    {
        // Arrange
        const string json = """{"responseSize": 42, "fillUsername": true}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var calculatedSize = message.CalculateSize();
        var binaryData = new byte[calculatedSize];
        using (var output = new CodedOutputStream(binaryData))
        {
            message.WriteTo(output);
        }

        // Assert
        binaryData.Length.ShouldBe(calculatedSize);
    }

    #endregion

    #region Field Name Resolution Tests

    [Fact]
    public void ParseJson_JsonName_ParsesCorrectly()
    {
        // Arrange - Using JSON name (camelCase)
        const string json = """{"fillUsername": true}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("fill_username");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
    }

    [Fact]
    public void ParseJson_ProtoName_ParsesCorrectly()
    {
        // Arrange - Using proto name (snake_case)
        const string json = """{"fill_username": true}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("fill_username");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
    }

    [Fact]
    public void ParseJson_CaseInsensitive_ParsesCorrectly()
    {
        // Arrange - Using different case
        const string json = """{"FILLUSERNAME": true}""";
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("fill_username");
        field.ShouldNotBeNull();
        message.Fields.ContainsKey(field).ShouldBeTrue();
    }

    #endregion

    #region Invalid JSON Tests

    [Fact]
    public void ParseJson_InvalidJsonSyntax_ThrowsException()
    {
        // Arrange
        const string json = """{"responseSize": }"""; // Invalid JSON
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act & Assert
        Should.Throw<System.Text.Json.JsonException>(() => new SimpleDynamicMessage(descriptor, json));
    }

    [Fact]
    public void ParseJson_NotAnObject_ThrowsException()
    {
        // Arrange
        const string json = "[1, 2, 3]"; // Array instead of object
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => new SimpleDynamicMessage(descriptor, json));
    }

    #endregion

    #region Descriptor Property Test

    [Fact]
    public void Descriptor_ReturnsCorrectDescriptor()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = new SimpleDynamicMessage(descriptor);

        // Assert
        message.Descriptor.ShouldBeSameAs(descriptor);
    }

    #endregion
}
