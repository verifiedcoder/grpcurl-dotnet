using GrpCurl.Net.Commands;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Commands;

[Collection(ConsoleStreamCollection.Name)]
public sealed class DescribeCommandHandlerHelperTests
{
    #region GetScalarTypeName Tests

    [Fact]
    public void GetScalarTypeName_AllTypes_ReturnsCorrectNames()
    {
        // Arrange
        var desc = TestDescriptorProvider.AllScalarsMessage;

        // Act
        // Assert
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(1)!).ShouldBe("double");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(2)!).ShouldBe("float");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(3)!).ShouldBe("int32");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(4)!).ShouldBe("int64");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(5)!).ShouldBe("uint32");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(6)!).ShouldBe("uint64");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(7)!).ShouldBe("sint32");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(8)!).ShouldBe("sint64");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(9)!).ShouldBe("fixed32");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(10)!).ShouldBe("fixed64");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(11)!).ShouldBe("sfixed32");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(12)!).ShouldBe("sfixed64");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(13)!).ShouldBe("bool");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(14)!).ShouldBe("string");
        DescribeCommandHandler.GetScalarTypeName(desc.FindFieldByNumber(15)!).ShouldBe("bytes");
    }

    #endregion

    #region GetProtoTypeName Tests

    [Fact]
    public void GetProtoTypeName_MessageField_ReturnsFullyQualifiedName()
    {
        // Arrange - SimpleRequest field 3 is Payload (message type)
        var field = TestDescriptorProvider.SimpleRequest.FindFieldByNumber(3)!;

        // Act
        var result = DescribeCommandHandler.GetProtoTypeName(field);

        // Assert
        result.ShouldBe(".testing.Payload");
    }

    [Fact]
    public void GetProtoTypeName_EnumField_ReturnsFullyQualifiedName()
    {
        // Arrange - SimpleRequest field 1 is PayloadType (enum type)
        var field = TestDescriptorProvider.SimpleRequest.FindFieldByNumber(1)!;

        // Act
        var result = DescribeCommandHandler.GetProtoTypeName(field);

        // Assert
        result.ShouldBe(".testing.PayloadType");
    }

    [Fact]
    public void GetProtoTypeName_ScalarField_ReturnsTypeName()
    {
        // Arrange - EchoStatus field 1 is int32
        var echoStatus = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");
        var field = echoStatus.FindFieldByNumber(1)!;

        // Act
        var result = DescribeCommandHandler.GetProtoTypeName(field);

        // Assert
        result.ShouldBe("int32");
    }

    [Fact]
    public void GetProtoTypeName_MapField_ReturnsMapSyntax()
    {
        // Arrange - MapFieldsMessage field 1 is map<string, string>
        var field = TestDescriptorProvider.MapFieldsMessage.FindFieldByNumber(1)!;

        // Act
        var result = DescribeCommandHandler.GetProtoTypeName(field);

        // Assert
        result.ShouldBe("map<string, string>");
    }

    #endregion

    #region GetEnumDefault Tests

    [Fact]
    public void GetEnumDefault_ReturnsFirstValue()
    {
        // Arrange - SimpleRequest field 1 is PayloadType enum
        var field = TestDescriptorProvider.SimpleRequest.FindFieldByNumber(1)!;

        // Act
        var result = DescribeCommandHandler.GetEnumDefault(field.EnumType);

        // Assert
        result.ShouldBe("COMPRESSABLE");
    }

    #endregion

    #region GetScalarDefault Tests

    [Fact]
    public void GetScalarDefault_AllTypes_ReturnsCorrectDefaults()
    {
        // Arrange
        var desc = TestDescriptorProvider.AllScalarsMessage;

        // Act
        // Assert
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(1)!).ShouldBe(0);     // double
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(2)!).ShouldBe(0);     // float
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(3)!).ShouldBe(0);     // int32
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(4)!).ShouldBe("0");   // int64 (quoted)
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(5)!).ShouldBe(0);     // uint32
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(6)!).ShouldBe("0");   // uint64 (quoted)
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(7)!).ShouldBe(0);     // sint32
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(8)!).ShouldBe("0");   // sint64 (quoted)
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(9)!).ShouldBe(0);     // fixed32
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(10)!).ShouldBe("0");  // fixed64 (quoted)
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(11)!).ShouldBe(0);    // sfixed32
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(12)!).ShouldBe("0");  // sfixed64 (quoted)
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(13)!).ShouldBe(false); // bool
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(14)!).ShouldBe("");    // string
        DescribeCommandHandler.GetScalarDefault(desc.FindFieldByNumber(15)!).ShouldBe("");    // bytes
    }

    #endregion

    #region CreateMessageTemplate Tests

    [Fact]
    public void CreateMessageTemplate_SimpleMessage_ReturnsAllFields()
    {
        // Arrange - EchoStatus has fields: code (int32) and message (string)
        var echoStatus = TestDescriptorProvider.GetMessageDescriptor("testing.EchoStatus");

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(echoStatus, []);

        // Assert
        template.ShouldContainKey("code");
        template.ShouldContainKey("message");
        template["code"].ShouldBe(0);
        template["message"].ShouldBe("");
    }

    [Fact]
    public void CreateMessageTemplate_NestedMessage_IncludesNestedTemplate()
    {
        // Arrange - SimpleRequest has a Payload field (message type)
        var simpleRequest = TestDescriptorProvider.SimpleRequest;

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(simpleRequest, []);

        // Assert - field names are snake_case (proto field names)
        template.ShouldContainKey("payload");
        template["payload"].ShouldBeOfType<Dictionary<string, object?>>();

        var payloadTemplate = (Dictionary<string, object?>)template["payload"]!;

        payloadTemplate.ShouldContainKey("type");
        payloadTemplate.ShouldContainKey("body");
    }

    [Fact]
    public void CreateMessageTemplate_WithEnum_ReturnsEnumDefault()
    {
        // Arrange - SimpleRequest field 1 is PayloadType enum
        var simpleRequest = TestDescriptorProvider.SimpleRequest;

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(simpleRequest, []);

        // Assert - field name is snake_case (proto field name)
        template.ShouldContainKey("response_type");
        template["response_type"].ShouldBe("COMPRESSABLE");
    }

    [Fact]
    public void CreateMessageTemplate_MapWithMessageValue_ReturnsExpandedTemplate()
    {
        // Arrange - MapFieldsMessage field 4 is map<string, Payload>
        var mapFieldsMessage = TestDescriptorProvider.MapFieldsMessage;

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(mapFieldsMessage, []);

        // Assert - field names are snake_case, map key is type-appropriate default ("" for string keys)
        template.ShouldContainKey("message_map");
        template["message_map"].ShouldBeOfType<Dictionary<string, object?>>();

        var mapTemplate = (Dictionary<string, object?>)template["message_map"]!;

        mapTemplate.ShouldContainKey("");
        mapTemplate[""].ShouldBeOfType<Dictionary<string, object?>>();

        var valueTemplate = (Dictionary<string, object?>)mapTemplate[""]!;

        valueTemplate.ShouldContainKey("type");
        valueTemplate["type"].ShouldBe("COMPRESSABLE");
        valueTemplate.ShouldContainKey("body");
        valueTemplate["body"].ShouldBe("");
    }

    [Fact]
    public void CreateMessageTemplate_MapWithEnumValue_ReturnsEnumDefault()
    {
        // Arrange - MapFieldsMessage field 5 is map<string, PayloadType>
        var mapFieldsMessage = TestDescriptorProvider.MapFieldsMessage;

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(mapFieldsMessage, []);

        // Assert - field names are snake_case, map key is type-appropriate default ("" for string keys)
        template.ShouldContainKey("enum_map");
        template["enum_map"].ShouldBeOfType<Dictionary<string, object?>>();

        var mapTemplate = (Dictionary<string, object?>)template["enum_map"]!;

        mapTemplate.ShouldContainKey("");
        mapTemplate[""].ShouldBe("COMPRESSABLE");
    }

    [Fact]
    public void CreateMessageTemplate_MapWithStringValue_ReturnsScalarDefault()
    {
        // Arrange - MapFieldsMessage field 1 is map<string, string>
        var mapFieldsMessage = TestDescriptorProvider.MapFieldsMessage;

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(mapFieldsMessage, []);

        // Assert - field names are snake_case, map key is type-appropriate default ("" for string keys)
        template.ShouldContainKey("string_map");
        template["string_map"].ShouldBeOfType<Dictionary<string, object?>>();

        var mapTemplate = (Dictionary<string, object?>)template["string_map"]!;

        mapTemplate.ShouldContainKey("");
        mapTemplate[""].ShouldBe("");
    }

    [Fact]
    public void CreateMessageTemplate_MapWithIntValue_ReturnsScalarDefault()
    {
        // Arrange - MapFieldsMessage field 2 is map<string, int32>
        var mapFieldsMessage = TestDescriptorProvider.MapFieldsMessage;

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(mapFieldsMessage, []);

        // Assert - field names are snake_case, map key is type-appropriate default ("" for string keys)
        template.ShouldContainKey("int_map");
        template["int_map"].ShouldBeOfType<Dictionary<string, object?>>();

        var mapTemplate = (Dictionary<string, object?>)template["int_map"]!;

        mapTemplate.ShouldContainKey("");
        mapTemplate[""].ShouldBe(0);
    }

    #endregion

    #region HandleWellKnownType Tests

    [Fact]
    public void HandleWellKnownType_Struct_ReturnsDictionaryWithHint()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Struct");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBeOfType<Dictionary<string, object?>>();

        var dict = (Dictionary<string, object?>)result;

        dict.ShouldContainKey("google.protobuf.Struct");
        dict["google.protobuf.Struct"].ShouldBe("supports arbitrary JSON objects");
    }

    [Fact]
    public void HandleWellKnownType_Value_ReturnsDictionaryWithHint()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Value");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBeOfType<Dictionary<string, object?>>();

        var dict = (Dictionary<string, object?>)result;

        dict.ShouldContainKey("google.protobuf.Value");
        dict["google.protobuf.Value"].ShouldBe("supports arbitrary JSON");
    }

    [Fact]
    public void HandleWellKnownType_ListValue_ReturnsListWithHint()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.ListValue");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBeOfType<List<object?>>();

        var list = (List<object?>)result;

        list.Count.ShouldBe(1);
        list[0].ShouldBeOfType<Dictionary<string, object?>>();

        var hintDict = (Dictionary<string, object?>)list[0]!;

        hintDict.ShouldContainKey("google.protobuf.ListValue");
        hintDict["google.protobuf.ListValue"].ShouldBe("is an array of arbitrary JSON values");
    }

    [Fact]
    public void HandleWellKnownType_Any_ReturnsDictionaryWithTypeUrlAndValue()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Any");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBeOfType<Dictionary<string, object?>>();

        var dict = (Dictionary<string, object?>)result;

        dict.ShouldContainKey("@type");
        dict["@type"].ShouldBe("type.googleapis.com/google.protobuf.Empty");
        dict.ShouldContainKey("value");
        dict["value"].ShouldBeOfType<Dictionary<string, object?>>();

        var valueDict = (Dictionary<string, object?>)dict["value"]!;

        valueDict.ShouldBeEmpty();
    }

    [Fact]
    public void HandleWellKnownType_FieldMask_ReturnsDictionaryWithPaths()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.FieldMask");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBeOfType<Dictionary<string, object?>>();

        var dict = (Dictionary<string, object?>)result;

        dict.ShouldContainKey("paths");
        dict["paths"].ShouldBeOfType<List<object?>>();

        var paths = (List<object?>)dict["paths"]!;

        paths.Count.ShouldBe(1);
        paths[0].ShouldBe("");
    }

    [Fact]
    public void HandleWellKnownType_Timestamp_ReturnsIso8601()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Timestamp");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBe("1970-01-01T00:00:00Z");
    }

    [Fact]
    public void HandleWellKnownType_Duration_ReturnsDurationString()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Duration");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBe("0s");
    }

    [Fact]
    public void HandleWellKnownType_Empty_ReturnsEmptyDictionary()
    {
        // Arrange
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("google.protobuf.Empty");

        // Act
        var result = DescribeCommandHandler.HandleWellKnownType(descriptor, []);

        // Assert
        result.ShouldBeOfType<Dictionary<string, object?>>();

        var dict = (Dictionary<string, object?>)result;

        dict.ShouldBeEmpty();
    }

    #endregion

    #region WellKnownTypesMessage Combined Template Test

    [Fact]
    public void CreateMessageTemplate_WellKnownTypesMessage_ShowsCanonicalFormsForAllWkts()
    {
        // Arrange - wkttesting.WellKnownTypesMessage has fields for all WKTs
        var descriptor = TestDescriptorProvider.GetWellKnownTypeDescriptor("wkttesting.WellKnownTypesMessage");

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(descriptor, []);

        // Assert - Timestamp (field names are snake_case)
        template.ShouldContainKey("timestamp_field");
        template["timestamp_field"].ShouldBe("1970-01-01T00:00:00Z");

        // Assert - Duration
        template.ShouldContainKey("duration_field");
        template["duration_field"].ShouldBe("0s");

        // Assert - Wrapper types
        template.ShouldContainKey("string_value");
        template["string_value"].ShouldBe("");

        template.ShouldContainKey("int32_value");
        template["int32_value"].ShouldBe(0);

        template.ShouldContainKey("int64_value");
        template["int64_value"].ShouldBe("0");

        template.ShouldContainKey("uint32_value");
        template["uint32_value"].ShouldBe(0);

        template.ShouldContainKey("uint64_value");
        template["uint64_value"].ShouldBe("0");

        template.ShouldContainKey("float_value");
        template["float_value"].ShouldBe(0);

        template.ShouldContainKey("double_value");
        template["double_value"].ShouldBe(0);

        template.ShouldContainKey("bool_value");
        template["bool_value"].ShouldBe(false);

        template.ShouldContainKey("bytes_value");
        template["bytes_value"].ShouldBeNull();

        // Assert - Any
        template.ShouldContainKey("any_field");
        template["any_field"].ShouldBeOfType<Dictionary<string, object?>>();

        var anyDict = (Dictionary<string, object?>)template["any_field"]!;

        anyDict.ShouldContainKey("@type");
        anyDict["@type"].ShouldBe("type.googleapis.com/google.protobuf.Empty");
        anyDict.ShouldContainKey("value");
        anyDict["value"].ShouldBeOfType<Dictionary<string, object?>>();

        // Assert - Struct
        template.ShouldContainKey("struct_field");
        template["struct_field"].ShouldBeOfType<Dictionary<string, object?>>();

        var structDict = (Dictionary<string, object?>)template["struct_field"]!;

        structDict.ShouldContainKey("google.protobuf.Struct");
        structDict["google.protobuf.Struct"].ShouldBe("supports arbitrary JSON objects");

        // Assert - Value
        template.ShouldContainKey("value_field");
        template["value_field"].ShouldBeOfType<Dictionary<string, object?>>();

        var valueDict = (Dictionary<string, object?>)template["value_field"]!;

        valueDict.ShouldContainKey("google.protobuf.Value");
        valueDict["google.protobuf.Value"].ShouldBe("supports arbitrary JSON");

        // Assert - ListValue
        template.ShouldContainKey("list_value_field");
        template["list_value_field"].ShouldBeOfType<List<object?>>();

        var listValue = (List<object?>)template["list_value_field"]!;

        listValue.Count.ShouldBe(1);
        listValue[0].ShouldBeOfType<Dictionary<string, object?>>();

        // Assert - FieldMask
        template.ShouldContainKey("field_mask");
        template["field_mask"].ShouldBeOfType<Dictionary<string, object?>>();

        var fieldMaskDict = (Dictionary<string, object?>)template["field_mask"]!;

        fieldMaskDict.ShouldContainKey("paths");
    }

    #endregion

    #region DescribeSymbolAsync Tests

    [Fact]
    public async Task DescribeSymbolAsync_Service_OutputsServiceDefinition()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.TestService",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("service TestService {");
            output.ShouldContain("rpc EmptyCall");
            output.ShouldContain("rpc UnaryCall");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_Message_OutputsMessageDefinition()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.SimpleRequest",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("message SimpleRequest {");
            output.ShouldContain("response_type");
            output.ShouldContain("payload");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_Enum_OutputsEnumDefinition()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.PayloadType",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("enum PayloadType {");
            output.ShouldContain("COMPRESSABLE");
            output.ShouldContain("UNCOMPRESSABLE");
            output.ShouldContain("RANDOM");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_MsgTemplate_OutputsJsonTemplate()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.SimpleRequest",
                verbose: false,
                msgTemplate: true,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert - msg-template outputs proto definition + blank line + "Message template:" + JSON template
            var output = writer.ToString();

            // Proto definition should appear first
            output.ShouldContain("message SimpleRequest {");
            output.ShouldContain("Message template:");

            // Template field names are snake_case
            output.ShouldContain("response_type");
            output.ShouldContain("COMPRESSABLE");
            output.ShouldContain("payload");
            output.ShouldContain("response_size");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_OneofMessage_OutputsOneofBlock()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.OneofMessage",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("message OneofMessage {");
            output.ShouldContain("oneof value {");
            output.ShouldContain("    string string_value = 1;");
            output.ShouldContain("    int32 int_value = 2;");
            output.ShouldContain("    .testing.Payload message_value = 3;");
            output.ShouldContain("  }");
            output.ShouldContain("  string name = 4;");

            // Verify oneof fields are NOT printed as flat fields (2-space indent only)
            // They should only appear with 4-space indent inside the oneof block
            var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            lines.ShouldNotContain("  string string_value = 1;");
            lines.ShouldNotContain("  int32 int_value = 2;");
            lines.ShouldNotContain("  .testing.Payload message_value = 3;");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_MsgTemplate_WellKnownTypesMessage_OutputsCanonicalJson()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "well-known-types.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "wkttesting.WellKnownTypesMessage",
                verbose: false,
                msgTemplate: true,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert - msg-template outputs proto definition + header + JSON template
            var output = writer.ToString();

            // Proto definition should appear first
            output.ShouldContain("message WellKnownTypesMessage {");
            output.ShouldContain("Message template:");

            // Verify canonical JSON representations for WKTs (field names are snake_case)
            output.ShouldContain("\"timestamp_field\": \"1970-01-01T00:00:00Z\"");
            output.ShouldContain("\"duration_field\": \"0s\"");
            output.ShouldContain("\"google.protobuf.Struct\": \"supports arbitrary JSON objects\"");
            output.ShouldContain("\"google.protobuf.Value\": \"supports arbitrary JSON\"");
            output.ShouldContain("\"google.protobuf.ListValue\": \"is an array of arbitrary JSON values\"");
            output.ShouldContain("\"@type\": \"type.googleapis.com/google.protobuf.Empty\"");
            output.ShouldContain("\"paths\"");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_MsgTemplate_MapFieldsMessage_OutputsExpandedMessageMapValue()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.MapFieldsMessage",
                verbose: false,
                msgTemplate: true,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert - msg-template outputs proto definition + header + JSON template
            var output = writer.ToString();

            // Proto definition should appear first
            output.ShouldContain("message MapFieldsMessage {");
            output.ShouldContain("Message template:");

            // The message_map value should be an expanded Payload template (field names are snake_case)
            // Map keys use type-appropriate defaults ("" for string keys)
            output.ShouldContain("\"message_map\"");
            output.ShouldContain("\"\"");
            output.ShouldContain("\"type\": \"COMPRESSABLE\"");
            output.ShouldContain("\"body\": \"\"");

            // The enum_map value should be the enum default name
            output.ShouldContain("\"enum_map\"");
            output.ShouldContain("\"COMPRESSABLE\"");

            // Scalar map values should still work
            output.ShouldContain("\"string_map\"");
            output.ShouldContain("\"int_map\"");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_NestedTypesMessage_OutputsNestedEnumsAndMessages()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.NestedTypesMessage",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("message NestedTypesMessage {");

            // Nested enum should be printed with proper indentation
            output.ShouldContain("  enum Status {");
            output.ShouldContain("    UNKNOWN = 0;");
            output.ShouldContain("    ACTIVE = 1;");
            output.ShouldContain("    INACTIVE = 2;");

            // Nested message should be printed with proper indentation
            output.ShouldContain("  message Details {");
            output.ShouldContain("    string description = 1;");
            output.ShouldContain("    int32 priority = 2;");

            // Regular fields should still be printed
            output.ShouldContain("  .testing.NestedTypesMessage.Details details = 1;");
            output.ShouldContain("  .testing.NestedTypesMessage.Status status = 2;");
            output.ShouldContain("  string name = 3;");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_MapFieldsMessage_DoesNotShowMapEntryTypes()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.MapFieldsMessage",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("message MapFieldsMessage {");

            // Map fields should be displayed as map<K, V> syntax
            output.ShouldContain("map<string, string> string_map = 1;");
            output.ShouldContain("map<string, int32> int_map = 2;");

            // Synthetic map entry types should NOT appear as nested messages
            output.ShouldNotContain("message StringMapEntry");
            output.ShouldNotContain("message IntMapEntry");
            output.ShouldNotContain("message MessageMapEntry");
            output.ShouldNotContain("message EnumMapEntry");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region GetMapKeyDefault Tests

    [Fact]
    public void GetMapKeyDefault_StringKey_ReturnsEmptyString()
    {
        // Arrange - MapFieldsMessage field 1 is map<string, string>
        var mapFieldsMessage = TestDescriptorProvider.MapFieldsMessage;
        var mapKeyField = mapFieldsMessage.FindFieldByNumber(1)!.MessageType.Fields[1]; // Key field

        // Act
        var result = DescribeCommandHandler.GetMapKeyDefault(mapKeyField);

        // Assert
        result.ShouldBe("");
    }

    [Fact]
    public void GetMapKeyDefault_Int32Key_ReturnsZeroString()
    {
        // Arrange - MapFieldsMessage field 3 is map<int32, string>
        var mapFieldsMessage = TestDescriptorProvider.MapFieldsMessage;
        var mapKeyField = mapFieldsMessage.FindFieldByNumber(3)!.MessageType.Fields[1]; // Key field

        // Act
        var result = DescribeCommandHandler.GetMapKeyDefault(mapKeyField);

        // Assert
        result.ShouldBe("0");
    }

    [Fact]
    public void GetMapKeyDefault_Int32KeyMap_UsesZeroKeyInTemplate()
    {
        // Arrange - MapFieldsMessage field 3 is map<int32, string>
        var mapFieldsMessage = TestDescriptorProvider.MapFieldsMessage;

        // Act
        var template = DescribeCommandHandler.CreateMessageTemplate(mapFieldsMessage, []);

        // Assert - int_key_map should use "0" as the key default for int32 keys
        template.ShouldContainKey("int_key_map");
        template["int_key_map"].ShouldBeOfType<Dictionary<string, object?>>();

        var mapTemplate = (Dictionary<string, object?>)template["int_key_map"]!;

        mapTemplate.ShouldContainKey("0");
        mapTemplate["0"].ShouldBe("");
    }

    #endregion

    #region PrintMessageDefinition Tests

    [Fact]
    public async Task PrintMessageDefinition_FieldsBeforeNestedTypes()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.NestedTypesMessage",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert - Fields should appear before nested types in the output
            var output = writer.ToString();
            var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            // Find the positions of a regular field and a nested type
            var fieldLineIndex = Array.FindIndex(lines, l => l.Contains(".testing.NestedTypesMessage.Details details = 1;"));
            var nestedMessageLineIndex = Array.FindIndex(lines, l => l.Contains("message Details {"));
            var nestedEnumLineIndex = Array.FindIndex(lines, l => l.Contains("enum Status {"));

            fieldLineIndex.ShouldBeGreaterThan(-1);
            nestedMessageLineIndex.ShouldBeGreaterThan(-1);
            nestedEnumLineIndex.ShouldBeGreaterThan(-1);

            // Fields should come before nested types
            fieldLineIndex.ShouldBeLessThan(nestedMessageLineIndex);
            fieldLineIndex.ShouldBeLessThan(nestedEnumLineIndex);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region Method Describe Tests

    [Fact]
    public async Task DescribeSymbolAsync_UnaryMethod_OutputsMethodSignature()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.TestService.EmptyCall",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("testing.TestService.EmptyCall is a method:");
            output.ShouldContain("rpc EmptyCall ( .testing.Empty ) returns ( .testing.Empty );");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_ServerStreamingMethod_OutputsStreamKeyword()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.TestService.StreamingOutputCall",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("testing.TestService.StreamingOutputCall is a method:");
            output.ShouldContain("rpc StreamingOutputCall ( .testing.StreamingOutputCallRequest ) returns ( stream .testing.StreamingOutputCallResponse );");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_ClientStreamingMethod_OutputsStreamKeyword()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.TestService.StreamingInputCall",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("testing.TestService.StreamingInputCall is a method:");
            output.ShouldContain("rpc StreamingInputCall ( stream .testing.StreamingInputCallRequest ) returns ( .testing.StreamingInputCallResponse );");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_BidirectionalStreamingMethod_OutputsBothStreamKeywords()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.TestService.FullDuplexCall",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var output = writer.ToString();

            output.ShouldContain("testing.TestService.FullDuplexCall is a method:");
            output.ShouldContain("rpc FullDuplexCall ( stream .testing.StreamingOutputCallRequest ) returns ( stream .testing.StreamingOutputCallResponse );");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task DescribeSymbolAsync_Service_MethodsSortedAlphabetically()
    {
        // Arrange
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);

        var originalOut = Console.Out;

        await using var writer = new StringWriter();

        Console.SetOut(writer);

        try
        {
            // Act
            await DescribeCommandHandler.DescribeSymbolAsync(
                source,
                "testing.TestService",
                verbose: false,
                msgTemplate: false,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert - Methods should be sorted alphabetically
            var output = writer.ToString();
            var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

            var methodLines = lines
                .Where(l => l.TrimStart().StartsWith("rpc "))
                .ToList();

            // Extract method names
            var methodNames = methodLines
                .Select(l => l.Trim().Split(' ')[1])
                .ToList();

            // Verify alphabetical ordering
            var sortedNames = methodNames.OrderBy(n => n).ToList();

            methodNames.ShouldBe(sortedNames);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    #endregion
}
