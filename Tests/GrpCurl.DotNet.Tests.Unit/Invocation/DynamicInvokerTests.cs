using Grpc.Core;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Invocation;

/// <summary>
/// Unit tests for DynamicInvoker static methods and InvocationResult,
/// covering coverage gaps in CreateMessageFromJson, MessageToJson, and ConvertMapKey.
/// </summary>
public sealed class DynamicInvokerTests
{
    #region CreateMessageFromJson Tests

    [Fact]
    public void CreateMessageFromJson_NullJson_ReturnsEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, null);

        // Assert
        message.ShouldNotBeNull();
        message.ShouldBeOfType<SimpleDynamicMessage>();

        var dynamicMessage = (SimpleDynamicMessage)message;

        dynamicMessage.Fields.ShouldBeEmpty();
        dynamicMessage.RepeatedFields.ShouldBeEmpty();
        dynamicMessage.MapFields.ShouldBeEmpty();
    }

    [Fact]
    public void CreateMessageFromJson_EmptyJson_ReturnsEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        // Act
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, "{}");

        // Assert
        message.ShouldNotBeNull();
        message.ShouldBeOfType<SimpleDynamicMessage>();

        var dynamicMessage = (SimpleDynamicMessage)message;

        dynamicMessage.Fields.ShouldBeEmpty();
        dynamicMessage.RepeatedFields.ShouldBeEmpty();
        dynamicMessage.MapFields.ShouldBeEmpty();
    }

    [Fact]
    public void CreateMessageFromJson_WithUnknownFields_ThrowsWhenNotAllowed()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        const string json = """{"responseSize": 42, "nonExistentField": "bad"}""";

        // Act
        // Assert
        var ex = Should.Throw<ArgumentException>(() => DynamicInvoker.CreateMessageFromJson(descriptor, json, allowUnknownFields: false));

        ex.Message.ShouldContain("Unknown field 'nonExistentField'");
        ex.Message.ShouldContain("--allow-unknown-fields");
    }

    [Fact]
    public void CreateMessageFromJson_WithUnknownFields_AllowedByDefault()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        const string json = """{"responseSize": 42, "nonExistentField": "ignored"}""";

        // Act -- allowUnknownFields defaults to true
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, json);

        // Assert
        message.ShouldNotBeNull();

        var dynamicMessage = (SimpleDynamicMessage)message;

        dynamicMessage.UnknownFields.ShouldContain("nonExistentField");

        // The known field should still be parsed
        var field = descriptor.FindFieldByName("response_size");

        field.ShouldNotBeNull();

        dynamicMessage.Fields.ContainsKey(field).ShouldBeTrue();
        dynamicMessage.Fields[field].ShouldBe(42);
    }

    [Fact]
    public void CreateMessageFromJson_WithValidFields_ReturnsPopulatedMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;

        const string json = """{"responseSize": 100, "fillUsername": true}""";

        // Act
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, json);

        // Assert
        message.ShouldNotBeNull();

        var dynamicMessage = (SimpleDynamicMessage)message;

        var sizeField = descriptor.FindFieldByName("response_size");

        sizeField.ShouldNotBeNull();

        dynamicMessage.Fields[sizeField].ShouldBe(100);

        var usernameField = descriptor.FindFieldByName("fill_username");

        usernameField.ShouldNotBeNull();

        dynamicMessage.Fields[usernameField].ShouldBe(true);
    }

    #endregion

    #region MessageToJson Tests

    [Fact]
    public void MessageToJson_SimpleDynamicMessage_ReturnsPrettyPrintedJson()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        const string inputJson = """{"responseSize": 42}""";
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, inputJson);

        // Act
        var outputJson = DynamicInvoker.MessageToJson(message);

        // Assert - output is pretty-printed with 2-space indentation
        outputJson.ShouldContain("\"response_size\": 42");
    }

    [Fact]
    public void MessageToJson_WithIncludeDefaults_EmitsAllFields()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, "{}");

        // Act
        var outputJson = DynamicInvoker.MessageToJson(message, includeDefaults: true);

        // Assert -- should contain default values for all fields (pretty-printed)
        outputJson.ShouldContain("\"response_size\": 0");
        outputJson.ShouldContain("\"fill_username\": false");
        outputJson.ShouldContain("\"payload\": null");
    }

    [Fact]
    public void MessageToJson_EmptyMessage_WithoutDefaults_ReturnsPrettyPrintedEmptyBraces()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, "{}");

        // Act
        var outputJson = DynamicInvoker.MessageToJson(message, includeDefaults: false);

        // Assert
        outputJson.ShouldBe("{}");
    }

    #endregion

    #region ToJson Tests

    [Fact]
    public void ToJson_EmptyMessage_ReturnsEmptyBraces()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var json = message.ToJson();

        // Assert
        json.ShouldBe("{}");
    }

    #endregion

    #region ConvertMapKey Tests (via Map Field JSON Parsing)

    [Fact]
    public void ConvertMapKey_StringKey_ReturnsString()
    {
        // Arrange -- MapFieldsMessage field 1 is map<string, string> string_map
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"stringMap": {"hello": "world", "foo": "bar"}}""";

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("string_map");

        field.ShouldNotBeNull();

        message.MapFields.ContainsKey(field).ShouldBeTrue();

        var map = message.MapFields[field];

        map.Count.ShouldBe(2);

        // Keys should be strings
        map.Keys.ShouldAllBe(k => k is string);
        map["hello"].ShouldBe("world");
        map["foo"].ShouldBe("bar");
    }

    [Fact]
    public void ConvertMapKey_IntKey_ReturnsInt()
    {
        // Arrange -- MapFieldsMessage field 3 is map<int32, string> int_key_map
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"intKeyMap": {"1": "one", "2": "two", "42": "forty-two"}}""";

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("int_key_map");

        field.ShouldNotBeNull();

        message.MapFields.ContainsKey(field).ShouldBeTrue();

        var map = message.MapFields[field];

        map.Count.ShouldBe(3);

        // Keys should be converted from string to int32
        map.Keys.ShouldAllBe(k => k is int);

        map[1].ShouldBe("one");
        map[2].ShouldBe("two");
        map[42].ShouldBe("forty-two");
    }

    [Fact]
    public void ConvertMapKey_IntKey_NegativeValue_ReturnsInt()
    {
        // Arrange -- map<int32, string> supports negative keys
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"intKeyMap": {"-1": "negative-one", "0": "zero"}}""";

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("int_key_map");

        field.ShouldNotBeNull();

        message.MapFields.ContainsKey(field).ShouldBeTrue();

        var map = message.MapFields[field];

        map.Count.ShouldBe(2);
        map[-1].ShouldBe("negative-one");
        map[0].ShouldBe("zero");
    }

    [Fact]
    public void ConvertMapKey_StringKeyWithIntValue_ReturnsCorrectTypes()
    {
        // Arrange -- MapFieldsMessage field 2 is map<string, int32> int_map
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"intMap": {"alpha": 10, "beta": 20}}""";

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("int_map");

        field.ShouldNotBeNull();

        message.MapFields.ContainsKey(field).ShouldBeTrue();

        var map = message.MapFields[field];

        map.Count.ShouldBe(2);

        // Keys should remain strings, values should be int32
        map.Keys.ShouldAllBe(k => k is string);
        map["alpha"].ShouldBe(10);
        map["beta"].ShouldBe(20);
    }

    [Fact]
    public void ConvertMapKey_MessageValue_ParsesNestedMessage()
    {
        // Arrange -- MapFieldsMessage field 4 is map<string, Payload> message_map
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"messageMap": {"key1": {"type": "COMPRESSABLE", "body": "dGVzdA=="}}}""";

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("message_map");

        field.ShouldNotBeNull();

        message.MapFields.ContainsKey(field).ShouldBeTrue();

        var map = message.MapFields[field];

        map.Count.ShouldBe(1);

        // Value should be a SimpleDynamicMessage (Payload)
        var payloadMessage = map["key1"].ShouldBeOfType<SimpleDynamicMessage>();
        var typeField = payloadMessage.Descriptor.FindFieldByName("type");

        typeField.ShouldNotBeNull();

        payloadMessage.Fields[typeField].ShouldBe(0); // COMPRESSABLE = 0
    }

    [Fact]
    public void ConvertMapKey_EmptyMap_ParsesCorrectly()
    {
        // Arrange -- empty map
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"stringMap": {}}""";

        // Act
        var message = new SimpleDynamicMessage(descriptor, json);

        // Assert
        var field = descriptor.FindFieldByName("string_map");

        field.ShouldNotBeNull();

        message.MapFields.ContainsKey(field).ShouldBeTrue();
        message.MapFields[field].ShouldBeEmpty();
    }

    #endregion

    #region Map Field ToJson Round-Trip Tests

    [Fact]
    public void MapField_StringKey_ToJson_RoundTrips()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"stringMap": {"key1": "val1"}}""";

        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var outputJson = message.ToJson();

        // Assert
        outputJson.ShouldContain("\"string_map\":{");
        outputJson.ShouldContain("\"key1\":\"val1\"");
    }

    [Fact]
    public void MapField_IntKey_ToJson_RoundTrips()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"intKeyMap": {"42": "answer"}}""";

        var message = new SimpleDynamicMessage(descriptor, json);

        // Act
        var outputJson = message.ToJson();

        // Assert
        outputJson.ShouldContain("\"int_key_map\":{");
        outputJson.ShouldContain("\"42\":\"answer\"");
    }

    [Fact]
    public void MapField_EmptyMap_ToJson_IncludeDefaults_ReturnsEmptyObject()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var outputJson = message.ToJson(includeDefaults: true);

        // Assert
        outputJson.ShouldContain("\"string_map\":{}");
        outputJson.ShouldContain("\"int_map\":{}");
        outputJson.ShouldContain("\"int_key_map\":{}");
        outputJson.ShouldContain("\"message_map\":{}");
    }

    #endregion

    #region InvocationResult Tests

    [Fact]
    public void InvocationResult_Properties_AreSettable()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);
        var headers = new Metadata { { "x-custom-header", "header-value" } };
        var trailers = new Metadata { { "x-custom-trailer", "trailer-value" } };

        // Act
        var result = new InvocationResult
        {
            Response = message,
            ResponseHeaders = headers,
            ResponseTrailers = trailers
        };

        // Assert
        result.Response.ShouldBeSameAs(message);
        result.ResponseHeaders.ShouldBeSameAs(headers);
        result.ResponseTrailers.ShouldBeSameAs(trailers);

        result.ResponseHeaders.ShouldNotBeNull();
        result.ResponseHeaders[0].Key.ShouldBe("x-custom-header");
        result.ResponseHeaders[0].Value.ShouldBe("header-value");

        result.ResponseTrailers.ShouldNotBeNull();
        result.ResponseTrailers[0].Key.ShouldBe("x-custom-trailer");
        result.ResponseTrailers[0].Value.ShouldBe("trailer-value");
    }

    [Fact]
    public void InvocationResult_DefaultValues_AreCorrect()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act -- only set the required Response property
        var result = new InvocationResult
        {
            Response = message
        };

        // Assert -- optional properties should default to null
        result.Response.ShouldNotBeNull();
        result.ResponseHeaders.ShouldBeNull();
        result.ResponseTrailers.ShouldBeNull();
    }

    [Fact]
    public void InvocationResult_MultipleHeaders_AreAccessible()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);
        var headers = new Metadata
        {
            { "content-type", "application/grpc" },
            { "x-request-id", "abc-123" },
            { "authorization", "Bearer token" }
        };

        // Act
        var result = new InvocationResult
        {
            Response = message,
            ResponseHeaders = headers
        };

        // Assert
        result.ResponseHeaders.ShouldNotBeNull();
        result.ResponseHeaders.Count.ShouldBe(3);
    }

    [Fact]
    public void InvocationResult_ResponseIsRequired()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.SimpleRequest;
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var result = new InvocationResult
        {
            Response = message
        };

        // Assert -- Response is required and should never be null once constructed
        result.Response.ShouldNotBeNull();
        result.Response.Descriptor.ShouldBe(descriptor);
    }

    #endregion

    #region DynamicInvoker.CreateMessageFromJson with Map Fields

    [Fact]
    public void CreateMessageFromJson_MapField_ParsesViaStaticMethod()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string json = """{"stringMap": {"a": "1"}, "intKeyMap": {"5": "five"}}""";

        // Act
        var message = DynamicInvoker.CreateMessageFromJson(descriptor, json);

        // Assert
        message.ShouldNotBeNull();

        var dynamicMessage = (SimpleDynamicMessage)message;

        var stringMapField = descriptor.FindFieldByName("string_map");

        stringMapField.ShouldNotBeNull();

        dynamicMessage.MapFields[stringMapField]["a"].ShouldBe("1");

        var intKeyMapField = descriptor.FindFieldByName("int_key_map");

        intKeyMapField.ShouldNotBeNull();

        dynamicMessage.MapFields[intKeyMapField][5].ShouldBe("five");
    }

    #endregion

    #region DynamicInvoker.MessageToJson with Map Fields

    [Fact]
    public void MessageToJson_MapField_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string inputJson = """{"stringMap": {"key": "value"}}""";

        var message = DynamicInvoker.CreateMessageFromJson(descriptor, inputJson);

        // Act
        var outputJson = DynamicInvoker.MessageToJson(message);

        // Assert - pretty-printed JSON uses spaces after colons
        outputJson.ShouldContain("\"string_map\":");
        outputJson.ShouldContain("\"key\": \"value\"");
    }

    [Fact]
    public void MessageToJson_IntKeyMap_SerializesKeysAsStrings()
    {
        // Arrange -- JSON object keys are always strings, even for int-keyed maps
        var descriptor = TestDescriptorProvider.MapFieldsMessage;

        const string inputJson = """{"intKeyMap": {"99": "ninety-nine"}}""";

        var message = DynamicInvoker.CreateMessageFromJson(descriptor, inputJson);

        // Act
        var outputJson = DynamicInvoker.MessageToJson(message);

        // Assert - pretty-printed JSON uses spaces after colons
        outputJson.ShouldContain("\"int_key_map\":");
        outputJson.ShouldContain("\"99\": \"ninety-nine\"");
    }

    #endregion
}
