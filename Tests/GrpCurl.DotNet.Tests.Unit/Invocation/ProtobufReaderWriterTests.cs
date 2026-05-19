using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class ProtobufReaderWriterTests
{
    #region ProtobufWriter Tests - Scalar Fields

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void WriteTo_Int32Field_SerializesCorrectly(int value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var message = new SimpleDynamicMessage(descriptor);
        var codeField = descriptor.FindFieldByNumber(1);

        message.Fields[codeField!] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[codeField!].ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("hello world with special chars: éàü")]
    [InlineData("unicode: 日本語")]
    public void WriteTo_StringField_SerializesCorrectly(string value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var message = new SimpleDynamicMessage(descriptor);
        var messageField = descriptor.FindFieldByNumber(2);

        message.Fields[messageField!] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[messageField!].ShouldBe(value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteTo_BoolField_SerializesCorrectly(bool value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var message = new SimpleDynamicMessage(descriptor);
        var boolField = descriptor.FindFieldByNumber(4);

        message.Fields[boolField!] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[boolField!].ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WriteTo_EnumField_SerializesCorrectly(int enumValue)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var message = new SimpleDynamicMessage(descriptor);
        var enumField = descriptor.FindFieldByNumber(1);

        message.Fields[enumField!] = enumValue;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[enumField!].ShouldBe(enumValue);
    }

    [Fact]
    public void WriteTo_BytesField_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");
        var message = new SimpleDynamicMessage(descriptor);
        var bytesField = descriptor.FindFieldByNumber(2);
        var originalBytes = ByteString.CopyFromUtf8("binary data here");

        message.Fields[bytesField!] = originalBytes;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[bytesField!].ShouldBe(originalBytes);
    }

    [Fact]
    public void WriteTo_EmptyBytesField_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");
        var message = new SimpleDynamicMessage(descriptor);
        var bytesField = descriptor.FindFieldByNumber(2);
        var emptyBytes = ByteString.Empty;

        message.Fields[bytesField!] = emptyBytes;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[bytesField!].ShouldBe(emptyBytes);
    }

    #endregion

    #region ProtobufWriter Tests - Nested Messages

    [Fact]
    public void WriteTo_NestedMessage_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var payloadDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");

        var message = new SimpleDynamicMessage(descriptor);
        var payloadField = descriptor.FindFieldByNumber(3);

        var nestedPayload = new SimpleDynamicMessage(payloadDescriptor);
        var typeField = payloadDescriptor.FindFieldByNumber(1);
        var bodyField = payloadDescriptor.FindFieldByNumber(2);

        nestedPayload.Fields[typeField!] = 1; // UNCOMPRESSABLE
        nestedPayload.Fields[bodyField!] = ByteString.CopyFromUtf8("test data");

        message.Fields[payloadField!] = nestedPayload;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields.ContainsKey(payloadField!).ShouldBeTrue();

        var deserializedPayload = deserialized.Fields[payloadField!].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedPayload.Fields[typeField!].ShouldBe(1);
        deserializedPayload.Fields[bodyField!].ShouldBe(ByteString.CopyFromUtf8("test data"));
    }

    [Fact]
    public void WriteTo_DeeplyNestedMessage_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var statusDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");

        var message = new SimpleDynamicMessage(descriptor);
        var statusField = descriptor.FindFieldByNumber(7);

        var nestedStatus = new SimpleDynamicMessage(statusDescriptor);
        var codeField = statusDescriptor.FindFieldByNumber(1);
        var msgField = statusDescriptor.FindFieldByNumber(2);

        nestedStatus.Fields[codeField!] = 404;
        nestedStatus.Fields[msgField!] = "Not Found";

        message.Fields[statusField!] = nestedStatus;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        var deserializedStatus = deserialized.Fields[statusField!].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedStatus.Fields[codeField!].ShouldBe(404);
        deserializedStatus.Fields[msgField!].ShouldBe("Not Found");
    }

    #endregion

    #region ProtobufWriter Tests - Repeated Fields

    [Fact]
    public void WriteTo_RepeatedMessageField_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.StreamingOutputCallRequest");
        var paramDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.ResponseParameters");

        var message = new SimpleDynamicMessage(descriptor);
        var paramsField = descriptor.FindFieldByNumber(2);
        var sizeField = paramDescriptor.FindFieldByNumber(1);
        var intervalField = paramDescriptor.FindFieldByNumber(2);

        var param1 = new SimpleDynamicMessage(paramDescriptor)
        {
            Fields =
            {
                [sizeField!] = 100,
                [intervalField!] = 1000
            }
        };

        var param2 = new SimpleDynamicMessage(paramDescriptor)
        {
            Fields =
            {
                [sizeField!] = 200,
                [intervalField!] = 2000
            }
        };

        message.RepeatedFields[paramsField!] = [param1, param2];

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(paramsField!).ShouldBeTrue();
        deserialized.RepeatedFields[paramsField!].Count.ShouldBe(2);

        var deserializedParam1 = deserialized.RepeatedFields[paramsField!][0].ShouldBeOfType<SimpleDynamicMessage>();
        var deserializedParam2 = deserialized.RepeatedFields[paramsField!][1].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedParam1.Fields[sizeField!].ShouldBe(100);
        deserializedParam1.Fields[intervalField!].ShouldBe(1000);
        deserializedParam2.Fields[sizeField!].ShouldBe(200);
        deserializedParam2.Fields[intervalField!].ShouldBe(2000);
    }

    [Fact]
    public void WriteTo_EmptyRepeatedField_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.StreamingOutputCallRequest");
        var message = new SimpleDynamicMessage(descriptor);
        var paramsField = descriptor.FindFieldByNumber(2);

        message.RepeatedFields[paramsField!] = [];

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        // Empty repeated fields may or may not be present in deserialized message
        if (deserialized.RepeatedFields.TryGetValue(paramsField!, out var repeatedField))
        {
            repeatedField.ShouldBeEmpty();
        }
    }

    #endregion

    #region ProtobufWriter Tests - CalculateSize

    [Fact]
    public void CalculateSize_EmptyMessage_ReturnsZero()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Empty");
        var message = new SimpleDynamicMessage(descriptor);

        // Act
        var size = ProtobufWriter.CalculateSize(message);

        // Assert
        size.ShouldBe(0);
    }

    [Fact]
    public void CalculateSize_WithScalarFields_CalculatesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var message = new SimpleDynamicMessage(descriptor);
        var codeField = descriptor.FindFieldByNumber(1);
        var msgField = descriptor.FindFieldByNumber(2);

        message.Fields[codeField!] = 42;
        message.Fields[msgField!] = "test";

        // Act
        var calculatedSize = ProtobufWriter.CalculateSize(message);
        var actualBytes = SerializeMessage(message);

        // Assert
        actualBytes.Length.ShouldBe(calculatedSize);
    }

    [Fact]
    public void CalculateSize_WithNestedMessage_CalculatesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var payloadDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");

        var message = new SimpleDynamicMessage(descriptor);
        var payloadField = descriptor.FindFieldByNumber(3);

        var nestedPayload = new SimpleDynamicMessage(payloadDescriptor);
        var typeField = payloadDescriptor.FindFieldByNumber(1);

        nestedPayload.Fields[typeField!] = 1;

        message.Fields[payloadField!] = nestedPayload;

        // Act
        var calculatedSize = ProtobufWriter.CalculateSize(message);
        var actualBytes = SerializeMessage(message);

        // Assert
        actualBytes.Length.ShouldBe(calculatedSize);
    }

    [Fact]
    public void CalculateSize_WithRepeatedField_CalculatesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.StreamingOutputCallRequest");
        var paramDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.ResponseParameters");

        var message = new SimpleDynamicMessage(descriptor);
        var paramsField = descriptor.FindFieldByNumber(2);
        var sizeField = paramDescriptor.FindFieldByNumber(1);

        var param1 = new SimpleDynamicMessage(paramDescriptor)
        {
            Fields = { [sizeField!] = 100 }
        };

        var param2 = new SimpleDynamicMessage(paramDescriptor)
        {
            Fields = { [sizeField!] = 200 }
        };

        message.RepeatedFields[paramsField!] = [param1, param2];

        // Act
        var calculatedSize = ProtobufWriter.CalculateSize(message);
        var actualBytes = SerializeMessage(message);

        // Assert
        actualBytes.Length.ShouldBe(calculatedSize);
    }

    #endregion

    #region ProtobufReader Tests - Scalar Fields

    [Fact]
    public void MergeFrom_ValidInt32_ReadsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var codeField = descriptor.FindFieldByNumber(1);

        // Create protobuf bytes manually: tag (field 1, varint) + value 42
        // Tag = (1 << 3) | 0 = 8, Value = 42
        var bytes = new byte[] { 8, 42 };

        var message = new SimpleDynamicMessage(descriptor);

        // Act
        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        // Assert
        message.Fields[codeField!].ShouldBe(42);
    }

    [Fact]
    public void MergeFrom_ValidString_ReadsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var msgField = descriptor.FindFieldByNumber(2);

        // Create protobuf bytes: tag (field 2, length-delimited) + length + "test"
        // Tag = (2 << 3) | 2 = 18, Length = 4, Data = "test"
        var bytes = new byte[] { 18, 4, (byte)'t', (byte)'e', (byte)'s', (byte)'t' };

        var message = new SimpleDynamicMessage(descriptor);

        // Act
        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        // Assert
        message.Fields[msgField!].ShouldBe("test");
    }

    [Fact]
    public void MergeFrom_ValidBool_ReadsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var boolField = descriptor.FindFieldByNumber(4); // fill_username

        // Create message with bool field = true using writer then read it back
        var original = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [boolField!] = true
            }
        };

        var bytes = SerializeMessage(original);

        var message = new SimpleDynamicMessage(descriptor);

        // Act
        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        // Assert
        message.Fields[boolField!].ShouldBe(true);
    }

    [Fact]
    public void MergeFrom_UnknownField_SkipsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var codeField = descriptor.FindFieldByNumber(1);

        // Create bytes with field 1 (valid) and field 99 (unknown)
        using var ms = new MemoryStream();
        using var output = new CodedOutputStream(ms);

        output.WriteTag(1, WireFormat.WireType.Varint);
        output.WriteInt32(42);
        output.WriteTag(99, WireFormat.WireType.Varint);
        output.WriteInt32(123);
        output.Flush();

        var bytes = ms.ToArray();

        var message = new SimpleDynamicMessage(descriptor);

        // Act
        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        // Assert
        message.Fields[codeField!].ShouldBe(42);
        message.Fields.ShouldHaveSingleItem(); // Only one field should be present
    }

    #endregion

    #region ProtobufReader Tests - Nested Messages

    [Fact]
    public void MergeFrom_NestedMessage_ReadsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var payloadDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");

        var payloadField = descriptor.FindFieldByNumber(3);
        var typeField = payloadDescriptor.FindFieldByNumber(1);

        // Create original message with nested payload
        var original = new SimpleDynamicMessage(descriptor);
        var nestedPayload = new SimpleDynamicMessage(payloadDescriptor)
        {
            Fields =
            {
                [typeField!] = 1
            }
        };

        original.Fields[payloadField!] = nestedPayload;

        var bytes = SerializeMessage(original);

        var message = new SimpleDynamicMessage(descriptor);

        // Act
        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        // Assert
        var readPayload = message.Fields[payloadField!].ShouldBeOfType<SimpleDynamicMessage>();

        readPayload.Fields[typeField!].ShouldBe(1);
    }

    #endregion

    #region ProtobufReader Tests - Repeated Fields

    [Fact]
    public void MergeFrom_RepeatedMessageField_ReadsCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.StreamingOutputCallRequest");
        var paramDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.ResponseParameters");

        var paramsField = descriptor.FindFieldByNumber(2);
        var sizeField = paramDescriptor.FindFieldByNumber(1);

        // Create original message with repeated field
        var original = new SimpleDynamicMessage(descriptor);
        var param1 = new SimpleDynamicMessage(paramDescriptor)
        {
            Fields =
            {
                [sizeField!] = 100
            }
        };

        var param2 = new SimpleDynamicMessage(paramDescriptor)
        {
            Fields =
            {
                [sizeField!] = 200
            }
        };

        original.RepeatedFields[paramsField!] = [param1, param2];

        var bytes = SerializeMessage(original);

        var message = new SimpleDynamicMessage(descriptor);

        // Act
        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        // Assert
        message.RepeatedFields.ContainsKey(paramsField!).ShouldBeTrue();
        message.RepeatedFields[paramsField!].Count.ShouldBe(2);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_EmptyMessage_Succeeds()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Empty");
        var original = new SimpleDynamicMessage(descriptor);

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_AllScalarTypes_Succeeds()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");

        var original = new SimpleDynamicMessage(descriptor);
        var responseTypeField = descriptor.FindFieldByNumber(1); // enum
        var responseSizeField = descriptor.FindFieldByNumber(2); // int32
        var fillUsernameField = descriptor.FindFieldByNumber(4); // bool
        var fillOauthScopeField = descriptor.FindFieldByNumber(5); // bool

        original.Fields[responseTypeField!] = 1;
        original.Fields[responseSizeField!] = 1024;
        original.Fields[fillUsernameField!] = true;
        original.Fields[fillOauthScopeField!] = false;

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[responseTypeField!].ShouldBe(1);
        deserialized.Fields[responseSizeField!].ShouldBe(1024);
        deserialized.Fields[fillUsernameField!].ShouldBe(true);
        deserialized.Fields[fillOauthScopeField!].ShouldBe(false);
    }

    [Fact]
    public void RoundTrip_ComplexMessage_Succeeds()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.SimpleRequest");
        var payloadDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");
        var statusDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");

        var original = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                // Set scalar fields
                [descriptor.FindFieldByNumber(1)!] = 2,   // response_type
                [descriptor.FindFieldByNumber(2)!] = 512, // response_size
                [descriptor.FindFieldByNumber(4)!] = true // fill_username
            }
        };

        // Set nested payload
        var payload = new SimpleDynamicMessage(payloadDescriptor)
        {
            Fields =
            {
                [payloadDescriptor.FindFieldByNumber(1)!] = 0, // type
                [payloadDescriptor.FindFieldByNumber(2)!] = ByteString.CopyFromUtf8("test payload data")
            }
        };

        original.Fields[descriptor.FindFieldByNumber(3)!] = payload;

        // Set nested status
        var status = new SimpleDynamicMessage(statusDescriptor)
        {
            Fields =
            {
                [statusDescriptor.FindFieldByNumber(1)!] = 200,
                [statusDescriptor.FindFieldByNumber(2)!] = "OK"
            }
        };

        original.Fields[descriptor.FindFieldByNumber(7)!] = status;

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        // Verify scalar fields
        deserialized.Fields[descriptor.FindFieldByNumber(1)!].ShouldBe(2);
        deserialized.Fields[descriptor.FindFieldByNumber(2)!].ShouldBe(512);
        deserialized.Fields[descriptor.FindFieldByNumber(4)!].ShouldBe(true);

        // Verify nested payload
        var deserializedPayload = deserialized.Fields[descriptor.FindFieldByNumber(3)!].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedPayload.Fields[payloadDescriptor.FindFieldByNumber(1)!].ShouldBe(0);
        deserializedPayload.Fields[payloadDescriptor.FindFieldByNumber(2)!].ShouldBe(ByteString.CopyFromUtf8("test payload data"));

        // Verify nested status
        var deserializedStatus = deserialized.Fields[descriptor.FindFieldByNumber(7)!].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedStatus.Fields[statusDescriptor.FindFieldByNumber(1)!].ShouldBe(200);
        deserializedStatus.Fields[statusDescriptor.FindFieldByNumber(2)!].ShouldBe("OK");
    }

    [Fact]
    public void RoundTrip_MessageWithRepeatedField_Succeeds()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.StreamingOutputCallRequest");
        var paramDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.ResponseParameters");

        var original = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                // Set enum field
                [descriptor.FindFieldByNumber(1)!] = 1 // response_type
            }
        };

        // Set repeated field with multiple entries
        var paramsField = descriptor.FindFieldByNumber(2)!;
        var entries = new List<object?>();

        for (var i = 1; i <= 5; i++)
        {
            var param = new SimpleDynamicMessage(paramDescriptor)
            {
                Fields =
                {
                    [paramDescriptor.FindFieldByNumber(1)!] = i * 100, // size
                    [paramDescriptor.FindFieldByNumber(2)!] = i * 1000 // interval_us
                }
            };

            entries.Add(param);
        }

        original.RepeatedFields[paramsField] = entries;

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[descriptor.FindFieldByNumber(1)!].ShouldBe(1);
        deserialized.RepeatedFields.ContainsKey(paramsField).ShouldBeTrue();
        deserialized.RepeatedFields[paramsField].Count.ShouldBe(5);

        for (var i = 0; i < 5; i++)
        {
            var param = deserialized.RepeatedFields[paramsField][i].ShouldBeOfType<SimpleDynamicMessage>();

            param.Fields[paramDescriptor.FindFieldByNumber(1)!].ShouldBe((i + 1) * 100);
            param.Fields[paramDescriptor.FindFieldByNumber(2)!].ShouldBe((i + 1) * 1000);
        }
    }

    [Fact]
    public void RoundTrip_LargeByteArray_Succeeds()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");
        var original = new SimpleDynamicMessage(descriptor);

        // Create a large byte array (1MB)
        var largeData = new byte[1024 * 1024];

        new Random(42).NextBytes(largeData);

        original.Fields[descriptor.FindFieldByNumber(2)!] = ByteString.CopyFrom(largeData);

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        var resultBytes = deserialized.Fields[descriptor.FindFieldByNumber(2)!].ShouldBeOfType<ByteString>();

        resultBytes.Length.ShouldBe(largeData.Length);
        resultBytes.ToByteArray().ShouldBe(largeData);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(2097151)]
    [InlineData(2097152)]
    public void RoundTrip_VariousVarintSizes_Succeeds(int value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.ResponseParameters");
        var original = new SimpleDynamicMessage(descriptor);
        var sizeField = descriptor.FindFieldByNumber(1);

        original.Fields[sizeField!] = value;

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[sizeField!].ShouldBe(value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-127)]
    [InlineData(-128)]
    [InlineData(-16383)]
    [InlineData(-2097151)]
    public void RoundTrip_NegativeIntegers_Succeeds(int value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var original = new SimpleDynamicMessage(descriptor);
        var codeField = descriptor.FindFieldByNumber(1);

        original.Fields[codeField!] = value;

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[codeField!].ShouldBe(value);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void WriteTo_NullFieldValue_SkipsField()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var message = new SimpleDynamicMessage(descriptor);
        var codeField = descriptor.FindFieldByNumber(1);

        message.Fields[codeField!] = null;

        // Act
        var bytes = SerializeMessage(message);

        // Assert
        bytes.ShouldBeEmpty(); // Should serialize to empty (no fields)
    }

    [Fact]
    public void MergeFrom_EmptyInput_ReturnsEmptyMessage()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var message = new SimpleDynamicMessage(descriptor);
        var bytes = Array.Empty<byte>();

        // Act
        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        // Assert
        message.Fields.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_MultipleNestedLevels_Succeeds()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.StreamingOutputCallRequest");
        var payloadDescriptor = TestDescriptorProvider.GetMessageDescriptor("testing.Payload");

        var original = new SimpleDynamicMessage(descriptor);

        // Create nested payload
        var payload = new SimpleDynamicMessage(payloadDescriptor)
        {
            Fields =
            {
                [payloadDescriptor.FindFieldByNumber(1)!] = 2, // type = RANDOM
                [payloadDescriptor.FindFieldByNumber(2)!] = ByteString.CopyFromUtf8("nested data")
            }
        };

        original.Fields[descriptor.FindFieldByNumber(3)!] = payload; // payload field

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        var deserializedPayload = deserialized.Fields[descriptor.FindFieldByNumber(3)!].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedPayload.Fields[payloadDescriptor.FindFieldByNumber(1)!].ShouldBe(2);
        deserializedPayload.Fields[payloadDescriptor.FindFieldByNumber(2)!].ShouldBe(ByteString.CopyFromUtf8("nested data"));
    }

    #endregion

    #region Packed Encoding Tests

    [Fact]
    public void RoundTrip_PackedInt32_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var intField = descriptor.FindFieldByNumber(1)!; // repeated int32 int_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [intField] = [1, 2, 3, -1, 0, int.MaxValue, int.MinValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(intField).ShouldBeTrue();

        var values = deserialized.RepeatedFields[intField];

        values.Count.ShouldBe(7);
        values[0].ShouldBe(1);
        values[1].ShouldBe(2);
        values[2].ShouldBe(3);
        values[3].ShouldBe(-1);
        values[4].ShouldBe(0);
        values[5].ShouldBe(int.MaxValue);
        values[6].ShouldBe(int.MinValue);
    }

    [Fact]
    public void RoundTrip_PackedBool_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var boolField = descriptor.FindFieldByNumber(2)!; // repeated bool bool_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [boolField] = [true, false, true, true, false]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(boolField).ShouldBeTrue();

        var values = deserialized.RepeatedFields[boolField];

        values.Count.ShouldBe(5);
        values[0].ShouldBe(true);
        values[1].ShouldBe(false);
        values[2].ShouldBe(true);
        values[3].ShouldBe(true);
        values[4].ShouldBe(false);
    }

    [Fact]
    public void RoundTrip_PackedEnum_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var enumField = descriptor.FindFieldByNumber(3)!; // repeated PayloadType enum_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [enumField] = [0, 1, 2, 0] // COMPRESSABLE, UNCOMPRESSABLE, RANDOM, COMPRESSABLE
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(enumField).ShouldBeTrue();

        var values = deserialized.RepeatedFields[enumField];

        values.Count.ShouldBe(4);
        values[0].ShouldBe(0);
        values[1].ShouldBe(1);
        values[2].ShouldBe(2);
        values[3].ShouldBe(0);
    }

    [Fact]
    public void RoundTrip_PackedDouble_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var doubleField = descriptor.FindFieldByNumber(4)!; // repeated double double_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [doubleField] = [1.5, -2.7, 0.0, double.MaxValue, double.MinValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(doubleField).ShouldBeTrue();

        var values = deserialized.RepeatedFields[doubleField];

        values.Count.ShouldBe(5);
        values[0].ShouldBe(1.5);
        values[1].ShouldBe(-2.7);
        values[2].ShouldBe(0.0);
        values[3].ShouldBe(double.MaxValue);
        values[4].ShouldBe(double.MinValue);
    }

    [Fact]
    public void RoundTrip_PackedFixed32_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var fixedField = descriptor.FindFieldByNumber(5)!; // repeated fixed32 fixed_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [fixedField] = [0u, 1u, 42u, uint.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(fixedField).ShouldBeTrue();

        var values = deserialized.RepeatedFields[fixedField];

        values.Count.ShouldBe(4);
        values[0].ShouldBe(0u);
        values[1].ShouldBe(1u);
        values[2].ShouldBe(42u);
        values[3].ShouldBe(uint.MaxValue);
    }

    [Fact]
    public void RoundTrip_PackedEmptyList_ProducesNoOutput()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var intField = descriptor.FindFieldByNumber(1)!;
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [intField] = []
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert - empty packed field should produce empty bytes
        bytes.ShouldBeEmpty();

        deserialized.RepeatedFields.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_PackedSingleValue_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var intField = descriptor.FindFieldByNumber(1)!;
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [intField] = [42]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields[intField].ShouldHaveSingleItem().ShouldBe(42);
    }

    [Fact]
    public void RoundTrip_MultiplePackedFields_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var intField = descriptor.FindFieldByNumber(1)!;
        var boolField = descriptor.FindFieldByNumber(2)!;
        var doubleField = descriptor.FindFieldByNumber(4)!;

        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [intField] = [10, 20],
                [boolField] = [true, false],
                [doubleField] = [3.14]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields[intField].Count.ShouldBe(2);
        deserialized.RepeatedFields[boolField].Count.ShouldBe(2);
        deserialized.RepeatedFields[doubleField].ShouldHaveSingleItem().ShouldBe(3.14);
    }

    [Fact]
    public void CalculateSize_PackedRepeatedInt32_CalculatesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var intField = descriptor.FindFieldByNumber(1)!;
        var message = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [intField] = [1, 2, 3]
            }
        };

        // Act
        var calculatedSize = ProtobufWriter.CalculateSize(message);
        var actualBytes = SerializeMessage(message);

        // Assert
        actualBytes.Length.ShouldBe(calculatedSize);
    }

    [Fact]
    public void MergeFrom_ManualPackedInt32Bytes_ReadsCorrectly()
    {
        // Arrange - manually construct packed int32 bytes
        // Field 1, wire type 2 (length-delimited) = tag 0x0A
        // Length = 3 (three 1-byte varints: 1, 2, 3)
        // Values: 1, 2, 3
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var intField = descriptor.FindFieldByNumber(1)!;
        var bytes = new byte[] { 0x0A, 0x03, 0x01, 0x02, 0x03 };

        // Act
        var message = DeserializeMessage(bytes, descriptor);

        // Assert
        message.RepeatedFields.ContainsKey(intField).ShouldBeTrue();

        var values = message.RepeatedFields[intField];

        values.Count.ShouldBe(3);
        values[0].ShouldBe(1);
        values[1].ShouldBe(2);
        values[2].ShouldBe(3);
    }

    #endregion

    #region All Scalar Types (AllScalarsMessage)

    [Theory]
    [InlineData(0f)]
    [InlineData(1.5f)]
    [InlineData(-3.14f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void RoundTrip_FloatField_SerializesCorrectly(float value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(2)!; // float_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1.5)]
    [InlineData(-2.7)]
    [InlineData(double.MaxValue)]
    [InlineData(double.Epsilon)]
    public void RoundTrip_DoubleField_SerializesCorrectly(double value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(1)!; // double_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(-100L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void RoundTrip_Int64Field_SerializesCorrectly(long value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(4)!; // int64_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(42u)]
    [InlineData(uint.MaxValue)]
    public void RoundTrip_UInt32Field_SerializesCorrectly(uint value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(5)!; // uint32_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(42UL)]
    [InlineData(ulong.MaxValue)]
    public void RoundTrip_UInt64Field_SerializesCorrectly(ulong value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(6)!; // uint64_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void RoundTrip_SInt32Field_SerializesCorrectly(int value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(7)!; // sint32_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(-100L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void RoundTrip_SInt64Field_SerializesCorrectly(long value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(8)!; // sint64_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(42u)]
    [InlineData(uint.MaxValue)]
    public void RoundTrip_Fixed32Field_SerializesCorrectly(uint value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(9)!; // fixed32_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(42UL)]
    [InlineData(ulong.MaxValue)]
    public void RoundTrip_Fixed64Field_SerializesCorrectly(ulong value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(10)!; // fixed64_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void RoundTrip_SFixed32Field_SerializesCorrectly(int value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(11)!; // sfixed32_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(-100L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void RoundTrip_SFixed64Field_SerializesCorrectly(long value)
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);
        var field = descriptor.FindFieldByNumber(12)!; // sfixed64_val

        message.Fields[field] = value;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[field].ShouldBe(value);
    }

    [Fact]
    public void RoundTrip_AllScalarFields_AllPopulated()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor);

        var doubleField = descriptor.FindFieldByNumber(1)!;
        var floatField = descriptor.FindFieldByNumber(2)!;
        var int32Field = descriptor.FindFieldByNumber(3)!;
        var int64Field = descriptor.FindFieldByNumber(4)!;
        var uint32Field = descriptor.FindFieldByNumber(5)!;
        var uint64Field = descriptor.FindFieldByNumber(6)!;
        var sint32Field = descriptor.FindFieldByNumber(7)!;
        var sint64Field = descriptor.FindFieldByNumber(8)!;
        var fixed32Field = descriptor.FindFieldByNumber(9)!;
        var fixed64Field = descriptor.FindFieldByNumber(10)!;
        var sfixed32Field = descriptor.FindFieldByNumber(11)!;
        var sfixed64Field = descriptor.FindFieldByNumber(12)!;
        var boolField = descriptor.FindFieldByNumber(13)!;
        var stringField = descriptor.FindFieldByNumber(14)!;
        var bytesField = descriptor.FindFieldByNumber(15)!;

        message.Fields[doubleField] = 3.14;
        message.Fields[floatField] = 2.71f;
        message.Fields[int32Field] = 42;
        message.Fields[int64Field] = 123456789L;
        message.Fields[uint32Field] = 100u;
        message.Fields[uint64Field] = 200UL;
        message.Fields[sint32Field] = -50;
        message.Fields[sint64Field] = -999L;
        message.Fields[fixed32Field] = 777u;
        message.Fields[fixed64Field] = 888UL;
        message.Fields[sfixed32Field] = -333;
        message.Fields[sfixed64Field] = -444L;
        message.Fields[boolField] = true;
        message.Fields[stringField] = "hello scalars";
        message.Fields[bytesField] = ByteString.CopyFromUtf8("binary data");

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[doubleField].ShouldBe(3.14);
        deserialized.Fields[floatField].ShouldBe(2.71f);
        deserialized.Fields[int32Field].ShouldBe(42);
        deserialized.Fields[int64Field].ShouldBe(123456789L);
        deserialized.Fields[uint32Field].ShouldBe(100u);
        deserialized.Fields[uint64Field].ShouldBe(200UL);
        deserialized.Fields[sint32Field].ShouldBe(-50);
        deserialized.Fields[sint64Field].ShouldBe(-999L);
        deserialized.Fields[fixed32Field].ShouldBe(777u);
        deserialized.Fields[fixed64Field].ShouldBe(888UL);
        deserialized.Fields[sfixed32Field].ShouldBe(-333);
        deserialized.Fields[sfixed64Field].ShouldBe(-444L);
        deserialized.Fields[boolField].ShouldBe(true);
        deserialized.Fields[stringField].ShouldBe("hello scalars");
        deserialized.Fields[bytesField].ShouldBe(ByteString.CopyFromUtf8("binary data"));
    }

    [Fact]
    public void CalculateSize_AllScalarTypes_MatchesActual()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.AllScalarsMessage");
        var message = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [descriptor.FindFieldByNumber(1)!] = 3.14,                             // double
                [descriptor.FindFieldByNumber(2)!] = 2.71f,                            // float
                [descriptor.FindFieldByNumber(3)!] = 42,                               // int32
                [descriptor.FindFieldByNumber(4)!] = 123456789L,                       // int64
                [descriptor.FindFieldByNumber(5)!] = 100u,                             // uint32
                [descriptor.FindFieldByNumber(6)!] = 200UL,                            // uint64
                [descriptor.FindFieldByNumber(7)!] = -50,                              // sint32
                [descriptor.FindFieldByNumber(8)!] = -999L,                            // sint64
                [descriptor.FindFieldByNumber(9)!] = 777u,                             // fixed32
                [descriptor.FindFieldByNumber(10)!] = 888UL,                           // fixed64
                [descriptor.FindFieldByNumber(11)!] = -333,                            // sfixed32
                [descriptor.FindFieldByNumber(12)!] = -444L,                           // sfixed64
                [descriptor.FindFieldByNumber(13)!] = true,                            // bool
                [descriptor.FindFieldByNumber(14)!] = "test string",                   // string
                [descriptor.FindFieldByNumber(15)!] = ByteString.CopyFromUtf8("bytes") // bytes
            }
        };

        // Act
        var calculatedSize = ProtobufWriter.CalculateSize(message);
        var actualBytes = SerializeMessage(message);

        // Assert
        actualBytes.Length.ShouldBe(calculatedSize);
    }

    #endregion

    #region Extended Packed Encoding Tests

    [Fact]
    public void RoundTrip_PackedFloat_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(6)!; // repeated float float_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [1.5f, -2.7f, 0f, float.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(4);
        values[0].ShouldBe(1.5f);
        values[1].ShouldBe(-2.7f);
        values[2].ShouldBe(0f);
        values[3].ShouldBe(float.MaxValue);
    }

    [Fact]
    public void RoundTrip_PackedInt64_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(7)!; // repeated int64 int64_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0L, 42L, -100L, long.MaxValue, long.MinValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(5);
        values[0].ShouldBe(0L);
        values[1].ShouldBe(42L);
        values[2].ShouldBe(-100L);
        values[3].ShouldBe(long.MaxValue);
        values[4].ShouldBe(long.MinValue);
    }

    [Fact]
    public void RoundTrip_PackedUInt32_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(8)!; // repeated uint32 uint32_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0u, 1u, 42u, uint.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(4);
        values[0].ShouldBe(0u);
        values[1].ShouldBe(1u);
        values[2].ShouldBe(42u);
        values[3].ShouldBe(uint.MaxValue);
    }

    [Fact]
    public void RoundTrip_PackedUInt64_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(9)!; // repeated uint64 uint64_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0UL, 42UL, ulong.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(3);
        values[0].ShouldBe(0UL);
        values[1].ShouldBe(42UL);
        values[2].ShouldBe(ulong.MaxValue);
    }

    [Fact]
    public void RoundTrip_PackedSInt32_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(10)!; // repeated sint32 sint32_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0, 42, -100, int.MaxValue, int.MinValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(5);
        values[0].ShouldBe(0);
        values[1].ShouldBe(42);
        values[2].ShouldBe(-100);
        values[3].ShouldBe(int.MaxValue);
        values[4].ShouldBe(int.MinValue);
    }

    [Fact]
    public void RoundTrip_PackedSInt64_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(11)!; // repeated sint64 sint64_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0L, 42L, -100L, long.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(4);
        values[0].ShouldBe(0L);
        values[1].ShouldBe(42L);
        values[2].ShouldBe(-100L);
        values[3].ShouldBe(long.MaxValue);
    }

    [Fact]
    public void RoundTrip_PackedFixed64_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(12)!; // repeated fixed64 fixed64_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0UL, 42UL, ulong.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(3);
        values[0].ShouldBe(0UL);
        values[1].ShouldBe(42UL);
        values[2].ShouldBe(ulong.MaxValue);
    }

    [Fact]
    public void RoundTrip_PackedSFixed32_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(13)!; // repeated sfixed32 sfixed32_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0, 42, -100, int.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(4);
        values[0].ShouldBe(0);
        values[1].ShouldBe(42);
        values[2].ShouldBe(-100);
        values[3].ShouldBe(int.MaxValue);
    }

    [Fact]
    public void RoundTrip_PackedSFixed64_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetMessageDescriptor("testing.RepeatedScalarsTest");
        var field = descriptor.FindFieldByNumber(14)!; // repeated sfixed64 sfixed64_values
        var original = new SimpleDynamicMessage(descriptor)
        {
            RepeatedFields =
            {
                [field] = [0L, 42L, -100L, long.MaxValue]
            }
        };

        // Act
        var bytes = SerializeMessage(original);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.RepeatedFields.ContainsKey(field).ShouldBeTrue();

        var values = deserialized.RepeatedFields[field];

        values.Count.ShouldBe(4);
        values[0].ShouldBe(0L);
        values[1].ShouldBe(42L);
        values[2].ShouldBe(-100L);
        values[3].ShouldBe(long.MaxValue);
    }

    #endregion

    #region Map Field Round-Trip Tests

    [Fact]
    public void RoundTrip_StringToStringMap_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringMapField = descriptor.FindFieldByNumber(1)!;

        message.MapFields[stringMapField] = new Dictionary<object, object?>
        {
            ["key1"] = "val1",
            ["key2"] = "val2"
        };

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.MapFields.ContainsKey(stringMapField).ShouldBeTrue();

        var map = deserialized.MapFields[stringMapField];

        map.Count.ShouldBe(2);
        map["key1"].ShouldBe("val1");
        map["key2"].ShouldBe("val2");
    }

    [Fact]
    public void RoundTrip_StringToIntMap_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var intMapField = descriptor.FindFieldByNumber(2)!;

        message.MapFields[intMapField] = new Dictionary<object, object?>
        {
            ["count"] = 42,
            ["size"] = 100
        };

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.MapFields.ContainsKey(intMapField).ShouldBeTrue();

        var map = deserialized.MapFields[intMapField];

        map.Count.ShouldBe(2);
        map["count"].ShouldBe(42);
        map["size"].ShouldBe(100);
    }

    [Fact]
    public void RoundTrip_IntKeyMap_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var intKeyMapField = descriptor.FindFieldByNumber(3)!;

        message.MapFields[intKeyMapField] = new Dictionary<object, object?>
        {
            [1] = "one",
            [2] = "two"
        };

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.MapFields.ContainsKey(intKeyMapField).ShouldBeTrue();

        var map = deserialized.MapFields[intKeyMapField];

        map.Count.ShouldBe(2);
        map[1].ShouldBe("one");
        map[2].ShouldBe("two");
    }

    [Fact]
    public void RoundTrip_StringToMessageMap_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var payloadDescriptor = TestDescriptorProvider.Payload;
        var message = new SimpleDynamicMessage(descriptor);
        var messageMapField = descriptor.FindFieldByNumber(4)!;

        var payload = new SimpleDynamicMessage(payloadDescriptor);
        var bodyField = payloadDescriptor.FindFieldByNumber(2)!;

        payload.Fields[bodyField] = ByteString.CopyFromUtf8("map payload body");

        message.MapFields[messageMapField] = new Dictionary<object, object?>
        {
            ["item"] = payload
        };

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.MapFields.ContainsKey(messageMapField).ShouldBeTrue();

        var map = deserialized.MapFields[messageMapField];

        map.Count.ShouldBe(1);
        map.ContainsKey("item").ShouldBeTrue();

        var deserializedPayload = map["item"].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedPayload.Fields[bodyField].ShouldBe(ByteString.CopyFromUtf8("map payload body"));
    }

    [Fact]
    public void RoundTrip_EmptyMap_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringMapField = descriptor.FindFieldByNumber(1)!;

        message.MapFields[stringMapField] = [];

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        // Empty map produces no bytes, so the field may not be present after deserialization
        if (deserialized.MapFields.TryGetValue(stringMapField, out var map))
        {
            map.ShouldBeEmpty();
        }
    }

    [Fact]
    public void RoundTrip_SingleEntryMap_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringMapField = descriptor.FindFieldByNumber(1)!;

        message.MapFields[stringMapField] = new Dictionary<object, object?>
        {
            ["only"] = "entry"
        };

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.MapFields.ContainsKey(stringMapField).ShouldBeTrue();
        deserialized.MapFields[stringMapField].ShouldHaveSingleItem();
        deserialized.MapFields[stringMapField]["only"].ShouldBe("entry");
    }

    [Fact]
    public void CalculateSize_MapFields_MatchesActual()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.MapFieldsMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringMapField = descriptor.FindFieldByNumber(1)!;
        var intMapField = descriptor.FindFieldByNumber(2)!;

        message.MapFields[stringMapField] = new Dictionary<object, object?>
        {
            ["key1"] = "val1",
            ["key2"] = "val2"
        };

        message.MapFields[intMapField] = new Dictionary<object, object?>
        {
            ["count"] = 42
        };

        // Act
        var calculatedSize = ProtobufWriter.CalculateSize(message);
        var actualBytes = SerializeMessage(message);

        // Assert
        actualBytes.Length.ShouldBe(calculatedSize);
    }

    #endregion

    #region Oneof Field Round-Trip Tests

    [Fact]
    public void RoundTrip_OneofStringValue_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.OneofMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var stringValueField = descriptor.FindFieldByNumber(1)!;

        message.Fields[stringValueField] = "hello oneof";

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields.ContainsKey(stringValueField).ShouldBeTrue();
        deserialized.Fields[stringValueField].ShouldBe("hello oneof");

        // Other oneof fields should not be present
        var intValueField = descriptor.FindFieldByNumber(2)!;
        var messageValueField = descriptor.FindFieldByNumber(3)!;

        deserialized.Fields.ContainsKey(intValueField).ShouldBeFalse();
        deserialized.Fields.ContainsKey(messageValueField).ShouldBeFalse();
    }

    [Fact]
    public void RoundTrip_OneofIntValue_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.OneofMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var intValueField = descriptor.FindFieldByNumber(2)!;

        message.Fields[intValueField] = 99;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields.ContainsKey(intValueField).ShouldBeTrue();
        deserialized.Fields[intValueField].ShouldBe(99);

        // Other oneof fields should not be present
        var stringValueField = descriptor.FindFieldByNumber(1)!;
        var messageValueField = descriptor.FindFieldByNumber(3)!;

        deserialized.Fields.ContainsKey(stringValueField).ShouldBeFalse();
        deserialized.Fields.ContainsKey(messageValueField).ShouldBeFalse();
    }

    [Fact]
    public void RoundTrip_OneofMessageValue_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.OneofMessage;
        var payloadDescriptor = TestDescriptorProvider.Payload;
        var message = new SimpleDynamicMessage(descriptor);
        var messageValueField = descriptor.FindFieldByNumber(3)!;

        var payload = new SimpleDynamicMessage(payloadDescriptor);
        var bodyField = payloadDescriptor.FindFieldByNumber(2)!;

        payload.Fields[bodyField] = ByteString.CopyFromUtf8("oneof payload");

        message.Fields[messageValueField] = payload;

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields.ContainsKey(messageValueField).ShouldBeTrue();

        var deserializedPayload = deserialized.Fields[messageValueField].ShouldBeOfType<SimpleDynamicMessage>();

        deserializedPayload.Fields[bodyField].ShouldBe(ByteString.CopyFromUtf8("oneof payload"));

        // Other oneof fields should not be present
        var stringValueField = descriptor.FindFieldByNumber(1)!;
        var intValueField = descriptor.FindFieldByNumber(2)!;

        deserialized.Fields.ContainsKey(stringValueField).ShouldBeFalse();
        deserialized.Fields.ContainsKey(intValueField).ShouldBeFalse();
    }

    [Fact]
    public void RoundTrip_OneofWithNonOneofField_SerializesCorrectly()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.OneofMessage;
        var message = new SimpleDynamicMessage(descriptor);
        var intValueField = descriptor.FindFieldByNumber(2)!;
        var nameField = descriptor.FindFieldByNumber(4)!;

        message.Fields[intValueField] = 42;
        message.Fields[nameField] = "test name";

        // Act
        var bytes = SerializeMessage(message);
        var deserialized = DeserializeMessage(bytes, descriptor);

        // Assert
        deserialized.Fields[intValueField].ShouldBe(42);
        deserialized.Fields[nameField].ShouldBe("test name");
    }

    [Fact]
    public void MergeFrom_OneofFieldOverwrite_KeepsLast()
    {
        // Arrange - write string_value first, then int_value; the reader should keep only int_value
        var descriptor = TestDescriptorProvider.OneofMessage;
        var stringValueField = descriptor.FindFieldByNumber(1)!;
        var intValueField = descriptor.FindFieldByNumber(2)!;

        // Build raw bytes with both oneof fields present (string_value then int_value)
        using var ms = new MemoryStream();
        using var output = new CodedOutputStream(ms);

        // Write string_value (field 1, wire type length-delimited)
        output.WriteTag(1, WireFormat.WireType.LengthDelimited);
        output.WriteString("first");

        // Write int_value (field 2, wire type varint)
        output.WriteTag(2, WireFormat.WireType.Varint);
        output.WriteInt32(123);

        output.Flush();
        var bytes = ms.ToArray();

        // Act
        var message = DeserializeMessage(bytes, descriptor);

        // Assert - only the last oneof field (int_value) should remain
        message.Fields.ContainsKey(intValueField).ShouldBeTrue();
        message.Fields[intValueField].ShouldBe(123);
        message.Fields.ContainsKey(stringValueField).ShouldBeFalse();
    }

    #endregion

    #region Helper Methods

    private static byte[] SerializeMessage(SimpleDynamicMessage message)
    {
        using var ms = new MemoryStream();
        using var output = new CodedOutputStream(ms);

        ProtobufWriter.WriteTo(message, output);

        output.Flush();

        return ms.ToArray();
    }

    private static SimpleDynamicMessage DeserializeMessage(byte[] bytes, MessageDescriptor descriptor)
    {
        var message = new SimpleDynamicMessage(descriptor);

        using var input = new CodedInputStream(bytes);

        ProtobufReader.MergeFrom(message, input);

        return message;
    }

    #endregion
}
