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
