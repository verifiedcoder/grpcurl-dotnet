using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using GrpCurl.Net.Invocation;
using System.Text.Json;
using Type = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Type;
using Label = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Label;

namespace GrpCurl.Net.Tests.Unit.Invocation;

/// <summary>
///     Verifies that <c>google.protobuf.Any</c> is (de)serialized as binary protobuf per the
///     spec — the embedded type resolved by <c>@type</c> — rather than treated as opaque JSON
///     text (review finding F2).
/// </summary>
public sealed class AnyWireFormatTests
{
    // Self-contained schema: a holder with an Any field, and a sibling message used as the
    // embedded payload (same file → resolvable from the holder's descriptor closure).
    private static readonly MessageDescriptor AnyHolder = BuildSchema();

    private static MessageDescriptor EmbeddedDescriptor => AnyHolder.File.MessageTypes.Single(m => m.Name == "Embedded");

    private static MessageDescriptor AnyDescriptor => AnyHolder.FindFieldByNumber(1).MessageType;

    [Fact]
    public void Json_to_wire_writes_binary_not_json_text()
    {
        // Arrange
        const string json = """{"detail":{"@type":"type.googleapis.com/anytest.Embedded","body":"hello","n":42}}""";

        // Act
        var holder = new SimpleDynamicMessage(AnyHolder, json);

        // Assert — the Any value field holds protobuf bytes, not UTF-8 JSON.
        var detail = (SimpleDynamicMessage)holder.Fields[AnyHolder.FindFieldByNumber(1)]!;
        var valueBytes = ((ByteString)detail.Fields[AnyDescriptor.FindFieldByNumber(2)]!).ToByteArray();

        valueBytes.Length.ShouldBeGreaterThan(0);
        valueBytes[0].ShouldNotBe((byte)'{'); // old bug wrote JSON text starting with '{'
        valueBytes[0].ShouldBe((byte)0x0A);    // field 1 (body), wire type 2 (length-delimited)
    }

    [Fact]
    public void Round_trips_resolvable_embedded_message()
    {
        // Arrange
        const string json = """{"detail":{"@type":"type.googleapis.com/anytest.Embedded","body":"hello","n":42}}""";

        // Act — JSON → message → wire → message → JSON
        var original = new SimpleDynamicMessage(AnyHolder, json);
        var roundTripped = new SimpleDynamicMessage(AnyHolder);

        using (var input = new CodedInputStream(original.ToByteArray()))
        {
            roundTripped.MergeFrom(input);
        }

        var output = JsonDocument.Parse(roundTripped.ToJson()).RootElement.GetProperty("detail");

        // Assert
        output.GetProperty("@type").GetString().ShouldBe("type.googleapis.com/anytest.Embedded");
        output.GetProperty("body").GetString().ShouldBe("hello");
        output.GetProperty("n").GetInt32().ShouldBe(42);
    }

    [Fact]
    public void Unresolvable_type_on_write_falls_back_to_base64()
    {
        // Arrange — build an Any holding a type not in the pool.
        var payload = ByteString.CopyFrom(1, 2, 3, 4);
        var any = new SimpleDynamicMessage(AnyDescriptor);
        any.Fields[AnyDescriptor.FindFieldByNumber(1)] = "type.googleapis.com/unknown.NotLoaded";
        any.Fields[AnyDescriptor.FindFieldByNumber(2)] = payload;

        var holder = new SimpleDynamicMessage(AnyHolder);
        holder.Fields[AnyHolder.FindFieldByNumber(1)] = any;

        // Act
        var detail = JsonDocument.Parse(holder.ToJson()).RootElement.GetProperty("detail");

        // Assert
        detail.GetProperty("@type").GetString().ShouldBe("type.googleapis.com/unknown.NotLoaded");
        detail.GetProperty("@error").GetString().ShouldBe("type not found");
        detail.GetProperty("value").GetString().ShouldBe(Convert.ToBase64String(payload.ToByteArray()));
    }

    [Fact]
    public void Unresolvable_type_on_read_input_throws()
    {
        // Arrange
        const string json = """{"detail":{"@type":"type.googleapis.com/unknown.NotLoaded","x":1}}""";

        // Act / Assert
        Should.Throw<ArgumentException>(() => new SimpleDynamicMessage(AnyHolder, json));
    }

    [Fact]
    public void Well_known_type_inside_any_uses_special_form_on_read()
    {
        // Arrange — an Any wrapping a Duration, with real Duration binary in value.
        var duration = new Duration { Seconds = 10 };
        var any = new SimpleDynamicMessage(AnyDescriptor);
        any.Fields[AnyDescriptor.FindFieldByNumber(1)] = "type.googleapis.com/google.protobuf.Duration";
        any.Fields[AnyDescriptor.FindFieldByNumber(2)] = duration.ToByteString();

        var holder = new SimpleDynamicMessage(AnyHolder);
        holder.Fields[AnyHolder.FindFieldByNumber(1)] = any;

        // Act
        var detail = JsonDocument.Parse(holder.ToJson()).RootElement.GetProperty("detail");

        // Assert — Duration's special JSON form, wrapped as {"@type":.., "value": "10s"}
        detail.GetProperty("@type").GetString().ShouldBe("type.googleapis.com/google.protobuf.Duration");
        detail.GetProperty("value").GetString().ShouldBe("10s");
    }

    [Fact]
    public void Well_known_type_inside_any_round_trips_from_json()
    {
        // Arrange
        const string json = """{"detail":{"@type":"type.googleapis.com/google.protobuf.Timestamp","value":"2020-01-01T00:00:00Z"}}""";

        // Act
        var holder = new SimpleDynamicMessage(AnyHolder, json);
        var roundTripped = new SimpleDynamicMessage(AnyHolder);

        using (var input = new CodedInputStream(holder.ToByteArray()))
        {
            roundTripped.MergeFrom(input);
        }

        var detail = JsonDocument.Parse(roundTripped.ToJson()).RootElement.GetProperty("detail");

        // Assert
        detail.GetProperty("@type").GetString().ShouldBe("type.googleapis.com/google.protobuf.Timestamp");
        detail.GetProperty("value").GetString().ShouldBe("2020-01-01T00:00:00Z");
    }

    private static MessageDescriptor BuildSchema()
    {
        var anyFileProto = Any.Descriptor.File.ToProto();

        var schema = new FileDescriptorProto
        {
            Name = "any_test.proto",
            Package = "anytest",
            Syntax = "proto3",
            Dependency = { "google/protobuf/any.proto" },
            MessageType =
            {
                new DescriptorProto
                {
                    Name = "Embedded",
                    Field =
                    {
                        new FieldDescriptorProto { Name = "body", Number = 1, Type = Type.String, Label = Label.Optional },
                        new FieldDescriptorProto { Name = "n", Number = 2, Type = Type.Int32, Label = Label.Optional }
                    }
                },
                new DescriptorProto
                {
                    Name = "AnyHolder",
                    Field =
                    {
                        new FieldDescriptorProto
                        {
                            Name = "detail", Number = 1, Type = Type.Message,
                            TypeName = ".google.protobuf.Any", Label = Label.Optional
                        }
                    }
                }
            }
        };

        var files = FileDescriptor.BuildFromByteStrings([anyFileProto.ToByteString(), schema.ToByteString()]);
        var schemaFile = files.Single(f => f.Name == "any_test.proto");

        return schemaFile.MessageTypes.Single(m => m.Name == "AnyHolder");
    }
}
