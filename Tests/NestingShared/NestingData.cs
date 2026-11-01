using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using GrpCurl.Net.Invocation;
using Type = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Type;
using Label = Google.Protobuf.Reflection.FieldDescriptorProto.Types.Label;

namespace GrpCurl.Net.TestSupport;

internal static class NestingData
{
    public static readonly MessageDescriptor Descriptor = Build();
    private static MessageDescriptor Build()
    {
        var node = new DescriptorProto { Name = "Node" };
        node.Field.Add(new FieldDescriptorProto { Name = "child", Number = 1, Type = Type.Message, TypeName = ".nest.Node", Label = Label.Optional });
        node.Field.Add(new FieldDescriptorProto { Name = "detail", Number = 2, Type = Type.Message, TypeName = ".google.protobuf.Any", Label = Label.Optional });
        node.Field.Add(new FieldDescriptorProto { Name = "children", Number = 3, Type = Type.Message, TypeName = ".nest.Node", Label = Label.Repeated });
        node.NestedType.Add(new DescriptorProto
        {
            Name = "EntriesEntry", Options = new MessageOptions { MapEntry = true },
            Field =
            {
                new FieldDescriptorProto { Name = "key", Number = 1, Type = Type.String, Label = Label.Optional },
                new FieldDescriptorProto { Name = "value", Number = 2, Type = Type.Message, TypeName = ".nest.Node", Label = Label.Optional }
            }
        });
        node.Field.Add(new FieldDescriptorProto { Name = "entries", Number = 4, Type = Type.Message, TypeName = ".nest.Node.EntriesEntry", Label = Label.Repeated });
        node.Field.Add(new FieldDescriptorProto { Name = "node", Number = 5, Type = Type.Group, TypeName = ".nest.Node", Label = Label.Optional });
        node.Field.Add(new FieldDescriptorProto { Name = "value", Number = 6, Type = Type.Message, TypeName = ".google.protobuf.Value", Label = Label.Optional });
        var schema = new FileDescriptorProto { Name = "nest.proto", Package = "nest", Syntax = "proto2", Dependency = { "google/protobuf/any.proto", "google/protobuf/struct.proto" }, MessageType = { node } };
        schema.Service.Add(new ServiceDescriptorProto
        {
            Name = "Probe", Method =
            {
                new MethodDescriptorProto { Name = "Unary", InputType = ".nest.Node", OutputType = ".nest.Node" },
                new MethodDescriptorProto { Name = "Stream", InputType = ".nest.Node", OutputType = ".nest.Node", ServerStreaming = true }
            }
        });
        return FileDescriptor.BuildFromByteStrings([Any.Descriptor.File.ToProto().ToByteString(), Value.Descriptor.File.ToProto().ToByteString(), schema.ToByteString()]).Single(f => f.Name == "nest.proto").MessageTypes[0];
    }
    public static byte[] Field(int number, byte[] payload)
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, leaveOpen: true))
        {
            output.WriteTag(number, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(ByteString.CopyFrom(payload));
        }
        return stream.ToArray();
    }
    public static byte[] Deep(int levels, bool any, int field = 1)
    {
        byte[] bytes = [];
        for (var i = 0; i < levels; i++)
        {
            bytes = any ? Field(2, new Any { TypeUrl = "type.googleapis.com/nest.Node", Value = ByteString.CopyFrom(bytes) }.ToByteArray()) : Field(field, bytes);
        }
        return bytes;
    }
    public static SimpleDynamicMessage Parse(byte[] bytes)
    {
        var message = new SimpleDynamicMessage(Descriptor);
        using var input = new CodedInputStream(bytes);
        message.MergeFrom(input);
        return message;
    }
}
