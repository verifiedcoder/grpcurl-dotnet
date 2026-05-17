using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpCurl.Net.Invocation;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Commands;

/// <summary>
///     Renders successful command results to stdout. Text mode preserves the existing
///     human-readable output; JSON mode emits one-line envelopes (NDJSON for
///     multi-record results) per the documented schema.
/// </summary>
internal static class OutputRenderer
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    /// <summary>Emits the result of a `list` invocation with no service argument.</summary>
    public static void WriteListServices(IReadOnlyList<string> services, OutputFormat format, TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;

        if (format == OutputFormat.Json)
        {
            var envelope = new
            {
                kind = "services",
                services
            };

            w.WriteLine(JsonSerializer.Serialize(envelope, CompactJsonOptions));

            return;
        }

        foreach (var svc in services)
        {
            w.WriteLine(svc);
        }
    }

    /// <summary>Emits the result of `list &lt;service&gt;` (methods of one service).</summary>
    public static void WriteListMethods(string serviceName, ServiceDescriptor descriptor, OutputFormat format, TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;
        var ordered = descriptor.Methods.OrderBy(m => m.Name).ToList();

        if (format == OutputFormat.Json)
        {
            var envelope = new
            {
                kind = "methods",
                service = serviceName,
                methods = ordered.ConvertAll(m => new
                {
                    name = m.Name,
                    fullName = m.FullName,
                    inputType = m.InputType.FullName,
                    outputType = m.OutputType.FullName,
                    clientStreaming = m.IsClientStreaming,
                    serverStreaming = m.IsServerStreaming
                })
            };

            w.WriteLine(JsonSerializer.Serialize(envelope, CompactJsonOptions));

            return;
        }

        foreach (var method in ordered)
        {
            w.WriteLine($"{descriptor.FullName}.{method.Name}");
        }
    }

    /// <summary>
    ///     Emits a `describe` envelope as JSON. Text-mode rendering for describe stays in
    ///     <see cref="DescribeCommandHandler"/> (proto-syntax printers).
    /// </summary>
    public static void WriteDescribeJson(IDescriptor descriptor, bool msgTemplate, TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;

        if (msgTemplate && descriptor is MessageDescriptor msgTmplDesc)
        {
            var template = DescribeCommandHandler.CreateMessageTemplate(msgTmplDesc, []);

            var envelope = new
            {
                kind = "messageTemplate",
                fullName = msgTmplDesc.FullName,
                template
            };

            w.WriteLine(JsonSerializer.Serialize(envelope, CompactJsonOptions));

            return;
        }

        switch (descriptor)
        {
            case ServiceDescriptor svc:

                w.WriteLine(JsonSerializer.Serialize(BuildServiceEnvelope(svc), CompactJsonOptions));

                break;

            case MessageDescriptor msg:

                w.WriteLine(JsonSerializer.Serialize(BuildMessageEnvelope(msg), CompactJsonOptions));

                break;

            case EnumDescriptor enm:

                w.WriteLine(JsonSerializer.Serialize(BuildEnumEnvelope(enm), CompactJsonOptions));

                break;

            case MethodDescriptor method:

                w.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "method",
                    fullName = method.FullName,
                    name = method.Name,
                    service = method.Service.FullName,
                    inputType = method.InputType.FullName,
                    outputType = method.OutputType.FullName,
                    clientStreaming = method.IsClientStreaming,
                    serverStreaming = method.IsServerStreaming
                }, CompactJsonOptions));

                break;

            default:

                w.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "unknown",
                    fullName = descriptor.FullName,
                    type = descriptor.GetType().Name
                }, CompactJsonOptions));

                break;
        }
    }

    /// <summary>Emits a single response message during invoke.</summary>
    public static void WriteInvokeMessage(IMessage message, int index, bool emitDefaults, OutputFormat format, TextWriter? writer = null, bool textFormat = false)
    {
        var w = writer ?? Console.Out;

        if (format == OutputFormat.Json)
        {
            // JSON envelope mode wraps responses regardless of --format. Even when the
            // request was parsed from text format, the envelope still emits JSON so the
            // NDJSON contract is preserved for callers piping to jq.
            var rawMessage = DynamicInvoker.MessageToJson(message, emitDefaults, indent: false);
            var inner = JsonNode.Parse(rawMessage);

            var envelope = new
            {
                kind = "message",
                index,
                message = inner
            };

            w.WriteLine(JsonSerializer.Serialize(envelope, CompactJsonOptions));

            return;
        }

        if (textFormat && message is SimpleDynamicMessage dyn)
        {
            // Pretty protobuf text-format output for parity with upstream grpcurl's
            // -format text. Falls back to JSON when the message isn't a dynamic message
            // (the well-known-type pipeline returns concrete types for some shapes).
            w.WriteLine(DynamicTextFormat.Print(dyn));
            return;
        }

        var pretty = DynamicInvoker.MessageToJson(message, emitDefaults);

        w.WriteLine(pretty);
    }

    private static object BuildServiceEnvelope(ServiceDescriptor svc)
        => new
        {
            kind = "service",
            fullName = svc.FullName,
            name = svc.Name,
            file = svc.File.Name,
            methods = svc.Methods.OrderBy(m => m.Name).Select(m => new
            {
                name = m.Name,
                fullName = m.FullName,
                inputType = m.InputType.FullName,
                outputType = m.OutputType.FullName,
                clientStreaming = m.IsClientStreaming,
                serverStreaming = m.IsServerStreaming
            }).ToList()
        };

    private static object BuildMessageEnvelope(MessageDescriptor msg)
    {
        var oneofFieldSet = new HashSet<FieldDescriptor>();

        foreach (var oneof in msg.Oneofs.Where(o => !o.IsSynthetic))
        {
            foreach (var f in oneof.Fields)
            {
                oneofFieldSet.Add(f);
            }
        }

        var fields = msg.Fields.InDeclarationOrder()
            .Where(f => !oneofFieldSet.Contains(f))
            .Select(BuildFieldDescriptorJson)
            .ToList();

        var oneofs = msg.Oneofs
            .Where(o => !o.IsSynthetic)
            .Select(o => new
            {
                name = o.Name,
                fields = o.Fields.Select(f => f.Name).ToList()
            })
            .ToList();

        // Skip synthetic map-entry types.
        var nestedTypes = msg.NestedTypes
            .Where(n => n.GetOptions()?.MapEntry != true)
            .Select(BuildMessageEnvelope)
            .ToList();

        var nestedEnums = msg.EnumTypes.Select(BuildEnumEnvelope).ToList();

        return new
        {
            kind = "message",
            fullName = msg.FullName,
            name = msg.Name,
            file = msg.File.Name,
            fields,
            oneofs,
            nestedTypes,
            nestedEnums
        };
    }

    private static object BuildEnumEnvelope(EnumDescriptor enm)
        => new
        {
            kind = "enum",
            fullName = enm.FullName,
            name = enm.Name,
            file = enm.File.Name,
            values = enm.Values.Select(v => new
            {
                name = v.Name,
                number = v.Number
            }).ToList()
        };

    private static object BuildFieldDescriptorJson(FieldDescriptor field)
    {
        var label = field switch
        {
            { IsMap: true } => "map",
            { IsRepeated: true } => "repeated",
            _ => "optional"
        };

        return new
        {
            name = field.Name,
            number = field.FieldNumber,
            type = DescribeCommandHandler.GetProtoTypeName(field),
            label,
            jsonName = field.JsonName
        };
    }
}
