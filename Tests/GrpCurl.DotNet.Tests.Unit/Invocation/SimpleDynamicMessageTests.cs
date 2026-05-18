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
        output.ShouldContain("\"response_size\":");
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
        output.ShouldContain("\"fill_username\":true");
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
        output.ShouldContain("\"response_type\":\"COMPRESSABLE\"");
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
        output.ShouldContain("\"response_parameters\":[");
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

    #region Emit Defaults Tests (ToJson with includeDefaults=true)

    [Fact]
    public void ToJson_WithEmitDefaults_EmitsDefaultInt32()
    {
        // Arrange - empty message, no fields set
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - response_size should appear with default value 0
        json.ShouldContain("\"response_size\":0");
    }

    [Fact]
    public void ToJson_WithEmitDefaults_EmitsDefaultBool()
    {
        // Arrange - empty message, no fields set
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - fill_username should appear with default value false
        json.ShouldContain("\"fill_username\":false");
    }

    [Fact]
    public void ToJson_WithEmitDefaults_EmitsDefaultEnum()
    {
        // Arrange - empty message, no fields set
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - response_type should appear with first enum value
        json.ShouldContain("\"response_type\":");
    }

    [Fact]
    public void ToJson_WithEmitDefaults_EmitsDefaultNestedMessageAsNull()
    {
        // Arrange - empty message, no fields set
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - payload (message field) should appear as null
        json.ShouldContain("\"payload\":null");
    }

    [Fact]
    public void ToJson_WithEmitDefaults_SkipsAlreadyPopulatedFields()
    {
        // Arrange - set response_size to 42
        const string inputJson = """{"responseSize": 42}""";

        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor, inputJson);

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - response_size should be 42 (not duplicated with 0)
        json.ShouldContain("\"response_size\":42");

        // Should not contain default 0 for response_size
        var count = json.Split("response_size").Length - 1;

        count.ShouldBe(1);
    }

    [Fact]
    public void ToJson_WithEmitDefaults_EmitsDefaultRepeatedAsEmptyArray()
    {
        // Arrange - empty StreamingOutputCallRequest (has repeated response_parameters)
        var descriptor = TestDescriptorProvider.StreamingOutputCallRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - response_parameters should appear as empty array
        json.ShouldContain("\"response_parameters\":[]");
    }

    [Fact]
    public void ToJson_WithoutEmitDefaults_OmitsDefaultFields()
    {
        // Arrange - empty message
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson(includeDefaults: false);

        // Assert - should be empty object since no fields set
        json.ShouldBe("{}");
    }

    #endregion

    #region Map JSON Serialization Tests

    [Fact]
    public void ToJson_MapStringToString_OutputsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringMapField = descriptor.FindFieldByName("string_map")!;

        message.MapFields[stringMapField] = new Dictionary<object, object?>
        {
            ["alpha"] = "one",
            ["beta"] = "two"
        };

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldContain("\"string_map\":{");
        json.ShouldContain("\"alpha\":\"one\"");
        json.ShouldContain("\"beta\":\"two\"");
    }

    [Fact]
    public void ToJson_MapStringToInt_OutputsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var intMapField = descriptor.FindFieldByName("int_map")!;

        message.MapFields[intMapField] = new Dictionary<object, object?>
        {
            ["count"] = 42,
            ["size"] = 100
        };

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldContain("\"int_map\":{");
        json.ShouldContain("\"count\":42");
        json.ShouldContain("\"size\":100");
    }

    [Fact]
    public void ToJson_MapIntKey_OutputsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var intKeyMapField = descriptor.FindFieldByName("int_key_map")!;

        message.MapFields[intKeyMapField] = new Dictionary<object, object?>
        {
            [1] = "one",
            [2] = "two"
        };

        // Act
        var json = message.ToJson();

        // Assert - int keys should become strings in JSON (JSON object keys are always strings)
        json.ShouldContain("\"int_key_map\":{");
        json.ShouldContain("\"1\":\"one\"");
        json.ShouldContain("\"2\":\"two\"");
    }

    [Fact]
    public void ToJson_EmptyMap_OmittedByDefault()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringMapField = descriptor.FindFieldByName("string_map")!;

        message.MapFields[stringMapField] = [];

        // Act
        var json = message.ToJson(includeDefaults: false);

        // Assert - empty map should be omitted when includeDefaults is false
        json.ShouldNotContain("string_map");
    }

    [Fact]
    public void ToJson_EmptyMap_WithEmitDefaults_OutputsEmptyObject()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringMapField = descriptor.FindFieldByName("string_map")!;

        message.MapFields[stringMapField] = [];

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - empty map should appear as empty object when includeDefaults is true
        json.ShouldContain("\"string_map\":{}");
    }

    [Fact]
    public void FromJson_MapField_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"string_map":{"a":"b","c":"d"}}""";

        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var stringMapField = descriptor.FindFieldByName("string_map")!;
        message.MapFields.ContainsKey(stringMapField).ShouldBeTrue();
        var map = message.MapFields[stringMapField];
        map.Count.ShouldBe(2);
        map["a"].ShouldBe("b");
        map["c"].ShouldBe("d");
    }

    #endregion

    #region Oneof JSON Serialization Tests

    [Fact]
    public void ToJson_OneofStringValue_OutputsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.OneofMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringValueField = descriptor.FindFieldByName("string_value")!;

        message.Fields[stringValueField] = "hello";

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldContain("\"string_value\":\"hello\"");
    }

    [Fact]
    public void ToJson_OneofIntValue_OutputsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.OneofMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var intValueField = descriptor.FindFieldByName("int_value")!;

        message.Fields[intValueField] = 42;

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldContain("\"int_value\":42");
    }

    [Fact]
    public void FromJson_OneofField_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"string_value":"hello","name":"test"}""";

        var descriptor = TestDescriptorProvider.OneofMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var stringValueField = descriptor.FindFieldByName("string_value")!;
        var nameField = descriptor.FindFieldByName("name")!;

        message.Fields.ContainsKey(stringValueField).ShouldBeTrue();
        message.Fields[stringValueField].ShouldBe("hello");

        message.Fields.ContainsKey(nameField).ShouldBeTrue();
        message.Fields[nameField].ShouldBe("test");
    }

    #endregion

    #region All Scalars JSON Tests

    [Fact]
    public void FromJson_AllScalarTypes_ParsesCorrectly()
    {
        // Arrange
        const string json = """
            {
                "double_val": 3.14,
                "float_val": 2.71,
                "int32_val": 42,
                "int64_val": "123456789",
                "uint32_val": 100,
                "uint64_val": "200",
                "sint32_val": -50,
                "sint64_val": "-999",
                "fixed32_val": 777,
                "fixed64_val": "888",
                "sfixed32_val": -333,
                "sfixed64_val": "-444",
                "bool_val": true,
                "string_val": "hello scalars",
                "bytes_val": "YmluYXJ5"
            }
            """;
        var descriptor = TestDescriptorProvider.AllScalarsMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        message.Fields[descriptor.FindFieldByName("double_val")!].ShouldBe(3.14);
        message.Fields[descriptor.FindFieldByName("float_val")!].ShouldBeOfType<float>();
        message.Fields[descriptor.FindFieldByName("int32_val")!].ShouldBe(42);
        message.Fields[descriptor.FindFieldByName("int64_val")!].ShouldBe(123456789L);
        message.Fields[descriptor.FindFieldByName("uint32_val")!].ShouldBe(100u);
        message.Fields[descriptor.FindFieldByName("uint64_val")!].ShouldBe(200UL);
        message.Fields[descriptor.FindFieldByName("sint32_val")!].ShouldBe(-50);
        message.Fields[descriptor.FindFieldByName("sint64_val")!].ShouldBe(-999L);
        message.Fields[descriptor.FindFieldByName("fixed32_val")!].ShouldBe(777u);
        message.Fields[descriptor.FindFieldByName("fixed64_val")!].ShouldBe(888UL);
        message.Fields[descriptor.FindFieldByName("sfixed32_val")!].ShouldBe(-333);
        message.Fields[descriptor.FindFieldByName("sfixed64_val")!].ShouldBe(-444L);
        message.Fields[descriptor.FindFieldByName("bool_val")!].ShouldBe(true);
        message.Fields[descriptor.FindFieldByName("string_val")!].ShouldBe("hello scalars");
        message.Fields[descriptor.FindFieldByName("bytes_val")!].ShouldBeOfType<ByteString>();
    }

    [Fact]
    public void ToJson_AllScalarTypes_OutputsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.AllScalarsMessage;
        var message = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [descriptor.FindFieldByName("double_val")!] = 3.14,
                [descriptor.FindFieldByName("float_val")!] = 2.71f,
                [descriptor.FindFieldByName("int32_val")!] = 42,
                [descriptor.FindFieldByName("int64_val")!] = 123456789L,
                [descriptor.FindFieldByName("uint32_val")!] = 100u,
                [descriptor.FindFieldByName("uint64_val")!] = 200UL,
                [descriptor.FindFieldByName("sint32_val")!] = -50,
                [descriptor.FindFieldByName("sint64_val")!] = -999L,
                [descriptor.FindFieldByName("fixed32_val")!] = 777u,
                [descriptor.FindFieldByName("fixed64_val")!] = 888UL,
                [descriptor.FindFieldByName("sfixed32_val")!] = -333,
                [descriptor.FindFieldByName("sfixed64_val")!] = -444L,
                [descriptor.FindFieldByName("bool_val")!] = true,
                [descriptor.FindFieldByName("string_val")!] = "hello scalars",
                [descriptor.FindFieldByName("bytes_val")!] = ByteString.CopyFromUtf8("binary")
            }
        };

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldContain("\"double_val\":");
        json.ShouldContain("\"float_val\":");
        json.ShouldContain("\"int32_val\":42");
        json.ShouldContain("\"uint32_val\":100");
        json.ShouldContain("\"sint32_val\":-50");
        json.ShouldContain("\"sfixed32_val\":-333");
        json.ShouldContain("\"bool_val\":true");
        json.ShouldContain("\"string_val\":\"hello scalars\"");
        json.ShouldContain("\"bytes_val\":\"YmluYXJ5\"");
    }

    [Fact]
    public void ToJson_Int64_OutputsAsQuotedString()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.AllScalarsMessage;
        var message = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [descriptor.FindFieldByName("int64_val")!] = 123456789L,
                [descriptor.FindFieldByName("sint64_val")!] = -999L,
                [descriptor.FindFieldByName("sfixed64_val")!] = -444L
            }
        };

        // Act
        var json = message.ToJson();

        // Assert - int64/sint64/sfixed64 should be quoted as strings in JSON
        json.ShouldContain("\"int64_val\":\"123456789\"");
        json.ShouldContain("\"sint64_val\":\"-999\"");
        json.ShouldContain("\"sfixed64_val\":\"-444\"");
    }

    [Fact]
    public void ToJson_UInt64_OutputsAsQuotedString()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.AllScalarsMessage;
        var message = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [descriptor.FindFieldByName("uint64_val")!] = 200UL,
                [descriptor.FindFieldByName("fixed64_val")!] = 888UL
            }
        };

        // Act
        var json = message.ToJson();

        // Assert - uint64/fixed64 should be quoted as strings in JSON
        json.ShouldContain("\"uint64_val\":\"200\"");
        json.ShouldContain("\"fixed64_val\":\"888\"");
    }

    [Fact]
    public void ToJson_Float_OutputsCorrectFormat()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.AllScalarsMessage;
        var message = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [descriptor.FindFieldByName("float_val")!] = 3.14f
            }
        };

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldContain("\"float_val\":");
        json.ShouldContain("3.14");
    }

    [Fact]
    public void ToJson_Double_OutputsCorrectFormat()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.AllScalarsMessage;
        var message = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [descriptor.FindFieldByName("double_val")!] = 2.718281828
            }
        };

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldContain("\"double_val\":");
        json.ShouldContain("2.718281828");
    }

    #endregion

    #region Float Special Values Tests

    [Fact]
    public void ConvertJsonValue_FloatNaN_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"float_val": "NaN"}""";

        var descriptor = TestDescriptorProvider.AllScalarsMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("float_val");

        field.ShouldNotBeNull();

        message.Fields.ContainsKey(field).ShouldBeTrue();

        var value = message.Fields[field].ShouldBeOfType<float>();

        float.IsNaN(value).ShouldBeTrue();
    }

    [Fact]
    public void ConvertJsonValue_FloatInfinity_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"float_val": "Infinity"}""";

        var descriptor = TestDescriptorProvider.AllScalarsMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("float_val");

        field.ShouldNotBeNull();

        message.Fields.ContainsKey(field).ShouldBeTrue();

        var value = message.Fields[field].ShouldBeOfType<float>();

        value.ShouldBe(float.PositiveInfinity);
    }

    [Fact]
    public void ConvertJsonValue_FloatNegativeInfinity_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"float_val": "-Infinity"}""";

        var descriptor = TestDescriptorProvider.AllScalarsMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("float_val");

        field.ShouldNotBeNull();

        message.Fields.ContainsKey(field).ShouldBeTrue();

        var value = message.Fields[field].ShouldBeOfType<float>();

        value.ShouldBe(float.NegativeInfinity);
    }

    #endregion

    #region Int64/UInt64 String Parsing Tests

    [Fact]
    public void ConvertJsonValue_Int64AsString_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"int64_val": "9223372036854775807"}""";

        var descriptor = TestDescriptorProvider.AllScalarsMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("int64_val");

        field.ShouldNotBeNull();

        message.Fields.ContainsKey(field).ShouldBeTrue();
        message.Fields[field].ShouldBe(long.MaxValue);
    }

    [Fact]
    public void ConvertJsonValue_UInt64AsString_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"uint64_val": "18446744073709551615"}""";

        var descriptor = TestDescriptorProvider.AllScalarsMessage;

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("uint64_val");

        field.ShouldNotBeNull();

        message.Fields.ContainsKey(field).ShouldBeTrue();
        message.Fields[field].ShouldBe(ulong.MaxValue);
    }

    #endregion

    #region ToJson Include Defaults All Scalars Tests

    [Fact]
    public void ToJson_WithIncludeDefaults_AllScalarTypes_OutputsDefaults()
    {
        // Arrange - empty message with no fields set
        var descriptor = TestDescriptorProvider.AllScalarsMessage;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson(includeDefaults: true);

        // Assert - all scalar fields should appear with their default values
        json.ShouldContain("\"double_val\":0");
        json.ShouldContain("\"float_val\":0");
        json.ShouldContain("\"int32_val\":0");
        json.ShouldContain("\"int64_val\":\"0\"");
        json.ShouldContain("\"uint32_val\":0");
        json.ShouldContain("\"uint64_val\":\"0\"");
        json.ShouldContain("\"sint32_val\":0");
        json.ShouldContain("\"sint64_val\":\"0\"");
        json.ShouldContain("\"fixed32_val\":0");
        json.ShouldContain("\"fixed64_val\":\"0\"");
        json.ShouldContain("\"sfixed32_val\":0");
        json.ShouldContain("\"sfixed64_val\":\"0\"");
        json.ShouldContain("\"bool_val\":false");
        json.ShouldContain("\"string_val\":\"\"");
        json.ShouldContain("\"bytes_val\":\"\"");
    }

    #endregion

    #region Oneof ToJson Output Tests

    [Fact]
    public void ToJson_OneofField_OnlyOutputsActiveField()
    {
        // Arrange - set string_value in the oneof via JSON parsing
        const string json = """{"string_value": "hello"}""";

        var descriptor = TestDescriptorProvider.OneofMessage;
        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var output = message.ToJson();

        // Assert - only string_value should appear, not int_value or message_value
        output.ShouldContain("\"string_value\":\"hello\"");
        output.ShouldNotContain("int_value");
        output.ShouldNotContain("message_value");
    }

    #endregion
}
