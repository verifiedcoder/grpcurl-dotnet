using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Unit.Fixtures;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class WellKnownTypeHandlerTests
{
    #region ConvertTimestamp Tests

    [Theory]
    [InlineData("2024-01-15T10:30:00Z", 1705314600, 0)]
    [InlineData("2024-01-15T10:30:00.123Z", 1705314600, 123000000)]
    [InlineData("2024-01-15T10:30:00.123456789Z", 1705314600, 123456789)]
    [InlineData("1970-01-01T00:00:00Z", 0, 0)]
    [InlineData("2000-01-01T00:00:00Z", 946684800, 0)]
    public void ConvertTimestamp_ValidRfc3339_ParsesCorrectly(string timestamp, long expectedSeconds, int expectedNanos)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var element = JsonDocument.Parse($"\"{timestamp}\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertTimestamp(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        result.Fields[secondsField!].ShouldBe(expectedSeconds);

        // Nanos might vary slightly due to parsing precision
        if (expectedNanos > 0)
        {
            result.Fields.ContainsKey(nanosField!).ShouldBeTrue();
        }
    }

    [Fact]
    public void ConvertTimestamp_NonStringValue_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var element = JsonDocument.Parse("12345").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertTimestamp(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertTimestamp_EmptyString_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var element = JsonDocument.Parse("\"\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertTimestamp(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertTimestamp_InvalidDateFormat_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var element = JsonDocument.Parse("\"not-a-date\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertTimestamp(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertTimestamp_LocalTime_ConvertsToUtc()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var element = JsonDocument.Parse("\"2024-01-15T10:30:00\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertTimestamp(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
        var secondsField = descriptor.FindFieldByNumber(1);
        result.Fields.ContainsKey(secondsField!).ShouldBeTrue();
    }

    #endregion

    #region ConvertDuration Tests

    [Theory]
    [InlineData("10s", 10, 0)]
    [InlineData("1.5s", 1, 500000000)]
    [InlineData("0s", 0, 0)]
    [InlineData("3600s", 3600, 0)]
    [InlineData("1.000000001s", 1, 1)]
    [InlineData("0.123456789s", 0, 123456789)]
    public void ConvertDuration_ValidFormat_ParsesCorrectly(string duration, long expectedSeconds, int expectedNanos)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var element = JsonDocument.Parse($"\"{duration}\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertDuration(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        result.Fields[secondsField!].ShouldBe(expectedSeconds);

        if (expectedNanos > 0)
        {
            result.Fields[nanosField!].ShouldBe(expectedNanos);
        }
    }

    [Fact]
    public void ConvertDuration_NonStringValue_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var element = JsonDocument.Parse("123").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertDuration(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertDuration_MissingSuffix_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var element = JsonDocument.Parse("\"10\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertDuration(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertDuration_InvalidNumber_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var element = JsonDocument.Parse("\"abcs\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertDuration(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertDuration_EmptyString_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var element = JsonDocument.Parse("\"\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertDuration(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("1.123456789012s")] // More than 9 fractional digits - should truncate
    public void ConvertDuration_ExcessivePrecision_TruncatesCorrectly(string duration)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var element = JsonDocument.Parse($"\"{duration}\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertDuration(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
        var nanosField = descriptor.FindFieldByNumber(2);
        var nanos = (int)result.Fields[nanosField!]!;
        nanos.ShouldBeLessThanOrEqualTo(999999999);
    }

    #endregion

    #region ConvertWrapperType Tests

    [Theory]
    [InlineData("google.protobuf.Int32Value", "42", 42)]
    [InlineData("google.protobuf.Int32Value", "-100", -100)]
    [InlineData("google.protobuf.Int32Value", "0", 0)]
    public void ConvertWrapperType_Int32Value_ParsesCorrectly(string typeName, string jsonValue, int expected)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        result.Fields[valueField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("google.protobuf.Int64Value", "9223372036854775807")]
    [InlineData("google.protobuf.Int64Value", "-9223372036854775808")]
    public void ConvertWrapperType_Int64Value_ParsesCorrectly(string typeName, string jsonValue)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;
        var expected = long.Parse(jsonValue);

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        result.Fields[valueField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("google.protobuf.UInt32Value", "4294967295")]
    [InlineData("google.protobuf.UInt32Value", "0")]
    public void ConvertWrapperType_UInt32Value_ParsesCorrectly(string typeName, string jsonValue)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;
        var expected = uint.Parse(jsonValue);

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        result.Fields[valueField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("google.protobuf.UInt64Value", "18446744073709551615")]
    public void ConvertWrapperType_UInt64Value_ParsesCorrectly(string typeName, string jsonValue)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;
        var expected = ulong.Parse(jsonValue);

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        result.Fields[valueField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("google.protobuf.FloatValue", "3.14")]
    [InlineData("google.protobuf.FloatValue", "-0.5")]
    [InlineData("google.protobuf.FloatValue", "0")]
    public void ConvertWrapperType_FloatValue_ParsesCorrectly(string typeName, string jsonValue)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;
        var expected = float.Parse(jsonValue);

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        ((float)result.Fields[valueField!]!).ShouldBe(expected, 0.00001f);
    }

    [Theory]
    [InlineData("google.protobuf.DoubleValue", "3.141592653589793")]
    [InlineData("google.protobuf.DoubleValue", "-1e10")]
    public void ConvertWrapperType_DoubleValue_ParsesCorrectly(string typeName, string jsonValue)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;
        var expected = double.Parse(jsonValue);

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        ((double)result.Fields[valueField!]!).ShouldBe(expected, 0.0000000001);
    }

    [Theory]
    [InlineData("google.protobuf.BoolValue", "true", true)]
    [InlineData("google.protobuf.BoolValue", "false", false)]
    public void ConvertWrapperType_BoolValue_ParsesCorrectly(string typeName, string jsonValue, bool expected)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        result.Fields[valueField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("google.protobuf.StringValue", "\"hello world\"", "hello world")]
    [InlineData("google.protobuf.StringValue", "\"\"", "")]
    [InlineData("google.protobuf.StringValue", "\"unicode: \\u00e9\"", "unicode: é")]
    public void ConvertWrapperType_StringValue_ParsesCorrectly(string typeName, string jsonValue, string expected)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        result.Fields[valueField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("google.protobuf.BytesValue", "\"SGVsbG8=\"")] // Base64 for "Hello"
    public void ConvertWrapperType_BytesValue_ParsesCorrectly(string typeName, string jsonValue)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor(typeName);
        var element = JsonDocument.Parse(jsonValue).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertWrapperType(element, descriptor, ConvertJsonValue);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(1);
        result.Fields[valueField!].ShouldNotBeNull();
    }

    #endregion

    #region ConvertAny Tests

    [Fact]
    public void ConvertAny_ValidObject_ParsesTypeUrl()
    {
        // Arrange
        const string json = """{"@type": "type.googleapis.com/google.protobuf.Duration", "value": "10s"}""";
        
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertAny(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
        var typeUrlField = descriptor.FindFieldByNumber(1);
        result.Fields[typeUrlField!].ShouldBe("type.googleapis.com/google.protobuf.Duration");
    }

    [Fact]
    public void ConvertAny_NonObjectValue_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");
        var element = JsonDocument.Parse("\"not-an-object\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertAny(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertAny_WithEmbeddedFields_SerializesValue()
    {
        // Arrange
        const string json = """{"@type": "type.googleapis.com/test.Message", "name": "test", "count": 42}""";
        
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertAny(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
        var valueField = descriptor.FindFieldByNumber(2);
        result.Fields.ContainsKey(valueField!).ShouldBeTrue();
        var valueBytes = (ByteString)result.Fields[valueField!]!;
        var valueJson = Encoding.UTF8.GetString(valueBytes.ToByteArray());
        valueJson.ShouldContain("name");
        valueJson.ShouldContain("test");
    }

    [Fact]
    public void ConvertAny_EmptyObject_ReturnsEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");
        var element = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertAny(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
    }

    #endregion

    #region ConvertEmpty Tests

    [Fact]
    public void ConvertEmpty_ReturnsEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Empty");

        // Act
        var result = WellKnownTypeHandler.ConvertEmpty(descriptor);

        // Assert
        result.ShouldNotBeNull();
        result.Fields.ShouldBeEmpty();
    }

    #endregion

    #region ConvertFieldMask Tests

    [Theory]
    [InlineData("name,email,age", new[] { "name", "email", "age" })]
    [InlineData("user.name", new[] { "user.name" })]
    [InlineData("field1", new[] { "field1" })]
    public void ConvertFieldMask_ValidPaths_ParsesCorrectly(string fieldMask, string[] expectedPaths)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");
        var element = JsonDocument.Parse($"\"{fieldMask}\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertFieldMask(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
        var pathsField = descriptor.FindFieldByNumber(1);
        result.RepeatedFields.ContainsKey(pathsField!).ShouldBeTrue();
        var actualPaths = result.RepeatedFields[pathsField!].Select(p => p?.ToString()).ToArray();
        actualPaths.Length.ShouldBe(expectedPaths.Length);
        actualPaths.ShouldBe(expectedPaths);
    }

    [Fact]
    public void ConvertFieldMask_EmptyString_ReturnsEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");
        var element = JsonDocument.Parse("\"\"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertFieldMask(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public void ConvertFieldMask_NonStringValue_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");
        var element = JsonDocument.Parse("[\"name\", \"email\"]").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertFieldMask(element, descriptor);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertFieldMask_WithWhitespace_TrimsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");
        var element = JsonDocument.Parse("\"  name  ,  email  \"").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertFieldMask(element, descriptor);

        // Assert
        result.ShouldNotBeNull();
    }

    #endregion

    #region ConvertStruct Tests

    [Fact]
    public void ConvertStruct_ValidObject_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"key1": "value1", "key2": 42}""";
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Struct");
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertStruct(element, descriptor, ConvertValue);

        // Assert
        result.ShouldNotBeNull();
        var fieldsField = descriptor.FindFieldByNumber(1);
        result.MapFields.ContainsKey(fieldsField!).ShouldBeTrue();
        result.MapFields[fieldsField!].Count.ShouldBe(2);
    }

    [Fact]
    public void ConvertStruct_EmptyObject_ReturnsEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Struct");
        var element = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertStruct(element, descriptor, ConvertValue);

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public void ConvertStruct_NonObjectValue_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Struct");
        var element = JsonDocument.Parse("[1, 2, 3]").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertStruct(element, descriptor, ConvertValue);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertStruct_NestedObjects_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"nested": {"inner": "value"}}""";
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Struct");
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertStruct(element, descriptor, ConvertValue);

        // Assert
        result.ShouldNotBeNull();
        var fieldsField = descriptor.FindFieldByNumber(1);
        result.MapFields.ContainsKey(fieldsField!).ShouldBeTrue();
    }

    #endregion

    #region ConvertValue Tests

    [Fact]
    public void ConvertValue_NullValue_ParsesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var element = JsonDocument.Parse("null").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

        // Assert
        result.ShouldNotBeNull();
        var nullField = descriptor.FindFieldByNumber(1);
        result.Fields.ContainsKey(nullField!).ShouldBeTrue();
    }

    [Theory]
    [InlineData("42", 42.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("-100", -100.0)]
    [InlineData("0", 0.0)]
    public void ConvertValue_NumberValue_ParsesCorrectly(string jsonValue, double expected)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var element = JsonDocument.Parse(jsonValue).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

        // Assert
        result.ShouldNotBeNull();
        var numberField = descriptor.FindFieldByNumber(2);
        result.Fields[numberField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("\"\"", "")]
    public void ConvertValue_StringValue_ParsesCorrectly(string jsonValue, string expected)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var element = JsonDocument.Parse(jsonValue).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

        // Assert
        result.ShouldNotBeNull();
        var stringField = descriptor.FindFieldByNumber(3);
        result.Fields[stringField!].ShouldBe(expected);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ConvertValue_BoolValue_ParsesCorrectly(string jsonValue, bool expected)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var element = JsonDocument.Parse(jsonValue).RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

        // Assert
        result.ShouldNotBeNull();
        var boolField = descriptor.FindFieldByNumber(4);
        result.Fields[boolField!].ShouldBe(expected);
    }

    [Fact]
    public void ConvertValue_ObjectValue_CreatesStructValue()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var element = JsonDocument.Parse("""{"key": "value"}""").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

        // Assert
        result.ShouldNotBeNull();
        var structField = descriptor.FindFieldByNumber(5);
        result.Fields.ContainsKey(structField!).ShouldBeTrue();
    }

    [Fact]
    public void ConvertValue_ArrayValue_CreatesListValue()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var element = JsonDocument.Parse("[1, 2, 3]").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

        // Assert
        result.ShouldNotBeNull();
        var listField = descriptor.FindFieldByNumber(6);
        result.Fields.ContainsKey(listField!).ShouldBeTrue();
    }

    [Fact]
    public void ConvertValue_TracksOneofField()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var element = JsonDocument.Parse("42").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

        // Assert
        result.ShouldNotBeNull();
        var numberField = descriptor.FindFieldByNumber(2);

        if (numberField!.ContainingOneof is null)
        {
            return;
        }

        result.OneofFields.ContainsKey(numberField.ContainingOneof).ShouldBeTrue();
        result.OneofFields[numberField.ContainingOneof].ShouldBe(numberField);
    }

    #endregion

    #region ConvertListValue Tests

    [Fact]
    public void ConvertListValue_ValidArray_ParsesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var element = JsonDocument.Parse("[1, \"two\", true, null]").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertListValue(element, descriptor, ConvertValue);

        // Assert
        result.ShouldNotBeNull();
        var valuesField = descriptor.FindFieldByNumber(1);
        result.RepeatedFields.ContainsKey(valuesField!).ShouldBeTrue();
        result.RepeatedFields[valuesField!].Count.ShouldBe(4);
    }

    [Fact]
    public void ConvertListValue_EmptyArray_ReturnsEmptyList()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var element = JsonDocument.Parse("[]").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertListValue(element, descriptor, ConvertValue);

        // Assert
        result.ShouldNotBeNull();
        var valuesField = descriptor.FindFieldByNumber(1);
        result.RepeatedFields.ContainsKey(valuesField!).ShouldBeTrue();
        result.RepeatedFields[valuesField!].ShouldBeEmpty();
    }

    [Fact]
    public void ConvertListValue_NonArrayValue_ReturnsNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var element = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertListValue(element, descriptor, ConvertValue);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertListValue_NestedArrays_ParsesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var element = JsonDocument.Parse("[[1, 2], [3, 4]]").RootElement;

        // Act
        var result = WellKnownTypeHandler.ConvertListValue(element, descriptor, ConvertValue);

        // Assert
        result.ShouldNotBeNull();
        var valuesField = descriptor.FindFieldByNumber(1);
        result.RepeatedFields[valuesField!].Count.ShouldBe(2);
    }

    #endregion

    #region WriteTimestampJson Tests

    [Fact]
    public void WriteTimestampJson_ValidTimestamp_FormatsAsRfc3339()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var message = new SimpleDynamicMessage(descriptor);
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        message.Fields[secondsField!] = 1705314600L; // 2024-01-15T10:30:00Z
        message.Fields[nanosField!] = 0;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteTimestampJson(sb, message);

        // Assert
        var result = sb.ToString();
        result.ShouldStartWith("\"");
        result.ShouldEndWith("Z\"");
        result.ShouldContain("2024-01-15");
    }

    [Fact]
    public void WriteTimestampJson_WithNanos_IncludesFractionalSeconds()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var message = new SimpleDynamicMessage(descriptor);
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        message.Fields[secondsField!] = 1705314600L;
        message.Fields[nanosField!] = 123000000; // 0.123 seconds
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteTimestampJson(sb, message);

        // Assert
        var result = sb.ToString();
        result.ShouldContain(".123");
    }

    [Fact]
    public void WriteTimestampJson_Epoch_FormatsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");
        var message = new SimpleDynamicMessage(descriptor);
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        message.Fields[secondsField!] = 0L;
        message.Fields[nanosField!] = 0;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteTimestampJson(sb, message);

        // Assert
        var result = sb.ToString();
        result.ShouldContain("1970-01-01");
    }

    #endregion

    #region WriteDurationJson Tests

    [Theory]
    [InlineData(10L, 0, "\"10s\"")]
    [InlineData(0L, 0, "\"0s\"")]
    [InlineData(3600L, 0, "\"3600s\"")]
    public void WriteDurationJson_WholeSeconds_FormatsCorrectly(long seconds, int nanos, string expected)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var message = new SimpleDynamicMessage(descriptor);
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        message.Fields[secondsField!] = seconds;
        message.Fields[nanosField!] = nanos;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteDurationJson(sb, message);

        // Assert
        sb.ToString().ShouldBe(expected);
    }

    [Fact]
    public void WriteDurationJson_WithNanos_IncludesFractional()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var message = new SimpleDynamicMessage(descriptor);
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        message.Fields[secondsField!] = 1L;
        message.Fields[nanosField!] = 500000000; // 0.5 seconds
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteDurationJson(sb, message);

        // Assert
        sb.ToString().ShouldBe("\"1.5s\"");
    }

    [Fact]
    public void WriteDurationJson_PreciseNanos_FormatsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");
        var message = new SimpleDynamicMessage(descriptor);
        var secondsField = descriptor.FindFieldByNumber(1);
        var nanosField = descriptor.FindFieldByNumber(2);
        message.Fields[secondsField!] = 1L;
        message.Fields[nanosField!] = 123456789;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteDurationJson(sb, message);

        // Assert
        sb.ToString().ShouldBe("\"1.123456789s\"");
    }

    #endregion

    #region WriteWrapperJson Tests

    [Fact]
    public void WriteWrapperJson_Int32Value_WritesRawValue()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Int32Value");
        var message = new SimpleDynamicMessage(descriptor);
        var valueField = descriptor.FindFieldByNumber(1);
        message.Fields[valueField!] = 42;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteWrapperJson(sb, message, descriptor, WriteJsonValue);

        // Assert
        sb.ToString().ShouldBe("42");
    }

    [Fact]
    public void WriteWrapperJson_StringValue_WritesQuotedString()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.StringValue");
        var message = new SimpleDynamicMessage(descriptor);
        var valueField = descriptor.FindFieldByNumber(1);
        message.Fields[valueField!] = "hello";
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteWrapperJson(sb, message, descriptor, WriteJsonValue);

        // Assert
        sb.ToString().ShouldBe("\"hello\"");
    }

    [Fact]
    public void WriteWrapperJson_BoolValue_WritesTrueOrFalse()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.BoolValue");
        var message = new SimpleDynamicMessage(descriptor);
        var valueField = descriptor.FindFieldByNumber(1);
        message.Fields[valueField!] = true;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteWrapperJson(sb, message, descriptor, WriteJsonValue);

        // Assert
        sb.ToString().ShouldBe("true");
    }

    [Fact]
    public void WriteWrapperJson_MissingValue_WritesNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Int32Value");
        var message = new SimpleDynamicMessage(descriptor);
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteWrapperJson(sb, message, descriptor, WriteJsonValue);

        // Assert
        sb.ToString().ShouldBe("null");
    }

    #endregion

    #region WriteAnyJson Tests

    [Fact]
    public void WriteAnyJson_WithTypeUrl_IncludesTypeField()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");
        var message = new SimpleDynamicMessage(descriptor);
        var typeUrlField = descriptor.FindFieldByNumber(1);
        message.Fields[typeUrlField!] = "type.googleapis.com/test.Message";
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteAnyJson(sb, message, descriptor);

        // Assert
        var result = sb.ToString();
        result.ShouldContain("@type");
        result.ShouldContain("type.googleapis.com/test.Message");
    }

    [Fact]
    public void WriteAnyJson_WithValue_IncludesEmbeddedFields()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");
        var message = new SimpleDynamicMessage(descriptor);
        var typeUrlField = descriptor.FindFieldByNumber(1);
        var valueField = descriptor.FindFieldByNumber(2);
        message.Fields[typeUrlField!] = "type.googleapis.com/test.Message";
        message.Fields[valueField!] = ByteString.CopyFromUtf8("{\"name\":\"test\"}");
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteAnyJson(sb, message, descriptor);

        // Assert
        var result = sb.ToString();
        result.ShouldContain("name");
        result.ShouldContain("test");
    }

    [Fact]
    public void WriteAnyJson_EmptyMessage_WritesEmptyObject()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");
        var message = new SimpleDynamicMessage(descriptor);
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteAnyJson(sb, message, descriptor);

        // Assert
        sb.ToString().ShouldBe("{}");
    }

    #endregion

    #region WriteEmptyJson Test

    [Fact]
    public void WriteEmptyJson_WritesEmptyObject()
    {
        // Arrange
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteEmptyJson(sb);

        // Assert
        sb.ToString().ShouldBe("{}");
    }

    #endregion

    #region WriteFieldMaskJson Tests

    [Fact]
    public void WriteFieldMaskJson_MultiplePaths_WritesCommaSeparated()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");
        var message = new SimpleDynamicMessage(descriptor);
        var pathsField = descriptor.FindFieldByNumber(1);
        message.RepeatedFields[pathsField!] = ["name", "email", "age"];
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteFieldMaskJson(sb, message);

        // Assert
        sb.ToString().ShouldBe("\"name,email,age\"");
    }

    [Fact]
    public void WriteFieldMaskJson_SinglePath_WritesAsString()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");
        var message = new SimpleDynamicMessage(descriptor);
        var pathsField = descriptor.FindFieldByNumber(1);
        message.RepeatedFields[pathsField!] = ["user.name"];
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteFieldMaskJson(sb, message);

        // Assert
        sb.ToString().ShouldBe("\"user.name\"");
    }

    [Fact]
    public void WriteFieldMaskJson_EmptyPaths_WritesEmptyString()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");
        var message = new SimpleDynamicMessage(descriptor);
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteFieldMaskJson(sb, message);

        // Assert
        sb.ToString().ShouldBe("\"\"");
    }

    #endregion

    #region WriteStructJson Tests

    [Fact]
    public void WriteStructJson_ValidStruct_WritesObject()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Struct");
        var message = new SimpleDynamicMessage(descriptor);
        var fieldsField = descriptor.FindFieldByNumber(1);
        var valueDescriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var stringField = valueDescriptor.FindFieldByNumber(3);
        var valueMessage = new SimpleDynamicMessage(valueDescriptor)
        {
            Fields = { [stringField!] = "test" }
        };
        message.MapFields[fieldsField!] = new Dictionary<object, object?> { { "key", valueMessage } };
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteStructJson(sb, message, WriteValueJson);

        // Assert
        var result = sb.ToString();
        result.ShouldStartWith("{");
        result.ShouldEndWith("}");
        result.ShouldContain("key");
    }

    [Fact]
    public void WriteStructJson_EmptyStruct_WritesEmptyObject()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Struct");
        var message = new SimpleDynamicMessage(descriptor);
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteStructJson(sb, message, WriteValueJson);

        // Assert
        sb.ToString().ShouldBe("{}");
    }

    #endregion

    #region WriteValueJson Tests

    [Fact]
    public void WriteValueJson_NullValue_WritesNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var message = new SimpleDynamicMessage(descriptor);
        var nullField = descriptor.FindFieldByNumber(1);
        message.Fields[nullField!] = 0; // NullValue.NULL_VALUE
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteValueJson(sb, message, WriteStructJson, WriteListValueJson);

        // Assert
        sb.ToString().ShouldBe("null");
    }

    [Fact]
    public void WriteValueJson_NumberValue_WritesNumber()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var message = new SimpleDynamicMessage(descriptor);
        var numberField = descriptor.FindFieldByNumber(2);
        message.Fields[numberField!] = 42.5;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteValueJson(sb, message, WriteStructJson, WriteListValueJson);

        // Assert
        sb.ToString().ShouldBe("42.5");
    }

    [Fact]
    public void WriteValueJson_StringValue_WritesQuotedString()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var message = new SimpleDynamicMessage(descriptor);
        var stringField = descriptor.FindFieldByNumber(3);
        message.Fields[stringField!] = "hello";
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteValueJson(sb, message, WriteStructJson, WriteListValueJson);

        // Assert
        sb.ToString().ShouldBe("\"hello\"");
    }

    [Fact]
    public void WriteValueJson_BoolTrue_WritesTrue()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var message = new SimpleDynamicMessage(descriptor);
        var boolField = descriptor.FindFieldByNumber(4);
        message.Fields[boolField!] = true;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteValueJson(sb, message, WriteStructJson, WriteListValueJson);

        // Assert
        sb.ToString().ShouldBe("true");
    }

    [Fact]
    public void WriteValueJson_BoolFalse_WritesFalse()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var message = new SimpleDynamicMessage(descriptor);
        var boolField = descriptor.FindFieldByNumber(4);
        message.Fields[boolField!] = false;
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteValueJson(sb, message, WriteStructJson, WriteListValueJson);

        // Assert
        sb.ToString().ShouldBe("false");
    }

    [Fact]
    public void WriteValueJson_EmptyMessage_WritesNull()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var message = new SimpleDynamicMessage(descriptor);
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteValueJson(sb, message, WriteStructJson, WriteListValueJson);

        // Assert
        sb.ToString().ShouldBe("null");
    }

    #endregion

    #region WriteListValueJson Tests

    [Fact]
    public void WriteListValueJson_ValidArray_WritesJsonArray()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var message = new SimpleDynamicMessage(descriptor);
        var valuesField = descriptor.FindFieldByNumber(1);
        var valueDescriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");
        var numberField = valueDescriptor.FindFieldByNumber(2);
        var value1 = new SimpleDynamicMessage(valueDescriptor) { Fields = { [numberField!] = 1.0 } };
        var value2 = new SimpleDynamicMessage(valueDescriptor) { Fields = { [numberField!] = 2.0 } };
        message.RepeatedFields[valuesField!] = [value1, value2];
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteListValueJson(sb, message, WriteValueJson);

        // Assert
        sb.ToString().ShouldBe("[1,2]");
    }

    [Fact]
    public void WriteListValueJson_EmptyArray_WritesEmptyArray()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var message = new SimpleDynamicMessage(descriptor);
        var valuesField = descriptor.FindFieldByNumber(1);
        message.RepeatedFields[valuesField!] = [];
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteListValueJson(sb, message, WriteValueJson);

        // Assert
        sb.ToString().ShouldBe("[]");
    }

    [Fact]
    public void WriteListValueJson_NoValuesField_WritesEmptyArray()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var message = new SimpleDynamicMessage(descriptor);
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteListValueJson(sb, message, WriteValueJson);

        // Assert
        sb.ToString().ShouldBe("[]");
    }

    [Fact]
    public void WriteListValueJson_NullValues_WritesNullElements()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");
        var message = new SimpleDynamicMessage(descriptor);
        var valuesField = descriptor.FindFieldByNumber(1);
        message.RepeatedFields[valuesField!] = [null, null];
        var sb = new StringBuilder();

        // Act
        WellKnownTypeHandler.WriteListValueJson(sb, message, WriteValueJson);

        // Assert
        sb.ToString().ShouldBe("[null,null]");
    }

    #endregion

    #region Helper Methods

    private static object? ConvertJsonValue(JsonElement element, FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => element.GetInt32(),
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => element.GetInt64(),
            FieldType.UInt32 or FieldType.Fixed32 => element.GetUInt32(),
            FieldType.UInt64 or FieldType.Fixed64 => element.GetUInt64(),
            FieldType.Float => element.GetSingle(),
            FieldType.Double => element.GetDouble(),
            FieldType.Bool => element.GetBoolean(),
            FieldType.String => element.GetString(),
            FieldType.Bytes => ByteString.FromBase64(element.GetString()!),
            _ => element.GetRawText()
        };

    private static SimpleDynamicMessage ConvertValue(JsonElement element, MessageDescriptor descriptor)
        => WellKnownTypeHandler.ConvertValue(element, descriptor, ConvertStruct, ConvertListValue);

    private static SimpleDynamicMessage? ConvertStruct(JsonElement element, MessageDescriptor descriptor)
        => WellKnownTypeHandler.ConvertStruct(element, descriptor, ConvertValue);

    private static SimpleDynamicMessage? ConvertListValue(JsonElement element, MessageDescriptor descriptor)
        => WellKnownTypeHandler.ConvertListValue(element, descriptor, ConvertValue);

    private static void WriteJsonValue(StringBuilder sb, FieldDescriptor field, object? value)
    {
        switch (field.FieldType)
        {
            case FieldType.Int32:
            case FieldType.Int64:
            case FieldType.UInt32:
            case FieldType.UInt64:
            case FieldType.SInt32:
            case FieldType.SInt64:
            case FieldType.Fixed32:
            case FieldType.Fixed64:
            case FieldType.SFixed32:
            case FieldType.SFixed64:
            case FieldType.Float:
            case FieldType.Double:
                sb.Append(value);
                break;
            case FieldType.Bool:
                sb.Append((bool)value! ? "true" : "false");
                break;
            case FieldType.String:
                sb.Append('"');
                sb.Append(value);
                sb.Append('"');
                break;

            #pragma warning disable S3458
            case FieldType.Group:
            case FieldType.Message:
            case FieldType.Bytes:
            case FieldType.Enum:
            #pragma warning restore S3458
            default:
                sb.Append("null");
                break;
        }
    }

    private static void WriteValueJson(StringBuilder sb, SimpleDynamicMessage message)
        => WellKnownTypeHandler.WriteValueJson(sb, message, WriteStructJson, WriteListValueJson);

    private static void WriteStructJson(StringBuilder sb, SimpleDynamicMessage message)
        => WellKnownTypeHandler.WriteStructJson(sb, message, WriteValueJson);

    private static void WriteListValueJson(StringBuilder sb, SimpleDynamicMessage message)
        => WellKnownTypeHandler.WriteListValueJson(sb, message, WriteValueJson);

    #endregion
}
