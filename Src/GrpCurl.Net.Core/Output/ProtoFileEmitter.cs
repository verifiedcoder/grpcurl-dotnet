using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;
using System.Text;

namespace GrpCurl.Net.Output;

/// <summary>
///     Reconstructs <c>.proto</c> source files from a <see cref="FileDescriptorProto" />
///     graph and writes them to a target directory. Implements upstream grpcurl's
///     <c>-proto-out-dir</c> feature (CODE-REVIEW.md P2). The output is not a byte-for-byte
///     round-trip of the original source — comments and original whitespace are gone — but
///     the schema is semantically equivalent.
/// </summary>
internal static class ProtoFileEmitter
{
    public static async Task WriteAsync(
        IDescriptorSource source,
        string outputDirectory,
        bool force,
        CancellationToken cancellationToken)
    {
        var outputRoot = Path.GetFullPath(outputDirectory);

        Directory.CreateDirectory(outputRoot);

        var emittedFiles = new HashSet<string>(StringComparer.Ordinal);

        // Walk every service to pull in its file plus transitive dependencies.
        foreach (var serviceName in await source.ListServicesAsync(cancellationToken).ConfigureAwait(false))
        {
            var symbol = await source.FindSymbolAsync(serviceName, cancellationToken).ConfigureAwait(false);

            if (symbol is not ServiceDescriptor service)
            {
                continue;
            }

            await EmitFileAndDependenciesAsync(service.File, outputRoot, force, emittedFiles, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EmitFileAndDependenciesAsync(
        FileDescriptor file,
        string outputDirectory,
        bool force,
        HashSet<string> emitted,
        CancellationToken cancellationToken)
    {
        if (!emitted.Add(file.Name))
        {
            return;
        }

        foreach (var dep in file.Dependencies)
        {
            await EmitFileAndDependenciesAsync(dep, outputDirectory, force, emitted, cancellationToken).ConfigureAwait(false);
        }

        var targetPath = ResolveContainedPath(outputDirectory, file.Name);
        var targetDir = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (!force && File.Exists(targetPath))
        {
            throw new IOException(
                $"Refusing to overwrite '{targetPath}'. Pass --force to allow overwriting.");
        }

        var content = EmitFile(file);

        var mode = force ? FileMode.Create : FileMode.CreateNew;

        await using var stream = new FileStream(targetPath, mode, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    internal static string ResolveContainedPath(string outputDirectory, string descriptorName)
    {
        if (string.IsNullOrWhiteSpace(descriptorName))
        {
            throw new InvalidDataException("Descriptor file name cannot be empty.");
        }

        if (IsRootedOrDriveQualified(descriptorName))
        {
            throw new InvalidDataException($"Descriptor file name '{descriptorName}' must be relative.");
        }

        var segments = descriptorName.Split(['/', '\\'], StringSplitOptions.None);

        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
        {
            throw new InvalidDataException($"Descriptor file name '{descriptorName}' contains an unsafe path segment.");
        }

        if (segments.Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException($"Descriptor file name '{descriptorName}' contains invalid path characters.");
        }

        var root = Path.GetFullPath(outputDirectory);
        var targetPath = segments.Aggregate(root, Path.Combine);
        var fullTargetPath = Path.GetFullPath(targetPath);
        var rootWithSeparator = EnsureTrailingSeparator(root);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullTargetPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidDataException($"Descriptor file name '{descriptorName}' resolves outside the output directory.");
        }

        return fullTargetPath;
    }

    private static bool IsRootedOrDriveQualified(string descriptorName)
        => Path.IsPathRooted(descriptorName)
           || descriptorName.StartsWith('/')
           || descriptorName.StartsWith('\\')
           || (descriptorName.Length >= 2 && char.IsAsciiLetter(descriptorName[0]) && descriptorName[1] == ':');

    private static string EnsureTrailingSeparator(string path)
        => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    internal static string EmitFile(FileDescriptor file)
    {
        var sb = new StringBuilder();

        // Edition / syntax pragma: stick with the legacy 'syntax' keyword that grpcurl
        // and most consumers expect. FileDescriptor.Syntax is marked obsolete in newer
        // Google.Protobuf but it's still the most reliable signal of proto2 vs proto3.
        #pragma warning disable CS0618
        sb.Append("syntax = \"").Append(file.Syntax.ToString().ToLowerInvariant()).AppendLine("\";");
        #pragma warning restore CS0618
        sb.AppendLine();

        if (!string.IsNullOrEmpty(file.Package))
        {
            sb.Append("package ").Append(file.Package).AppendLine(";");
            sb.AppendLine();
        }

        foreach (var dep in file.Dependencies)
        {
            sb.Append("import \"").Append(dep.Name).AppendLine("\";");
        }

        if (file.Dependencies.Count > 0)
        {
            sb.AppendLine();
        }

        foreach (var enm in file.EnumTypes)
        {
            EmitEnum(enm, sb, 0);
            sb.AppendLine();
        }

        foreach (var msg in file.MessageTypes)
        {
            EmitMessage(msg, sb, 0);
            sb.AppendLine();
        }

        foreach (var svc in file.Services)
        {
            EmitService(svc, sb);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void EmitMessage(MessageDescriptor msg, StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent * 2);

        sb.Append(pad).Append("message ").Append(msg.Name).AppendLine(" {");

        var oneofFields = new HashSet<FieldDescriptor>();

        foreach (var oneof in msg.Oneofs)
        {
            foreach (var f in oneof.Fields)
            {
                oneofFields.Add(f);
            }
        }

        var printedOneofs = new HashSet<string>();

        foreach (var field in msg.Fields.InDeclarationOrder())
        {
            if (oneofFields.Contains(field))
            {
                var oneof = field.ContainingOneof;

                if (!printedOneofs.Add(oneof.Name))
                {
                    continue;
                }

                sb.Append(pad).Append("  oneof ").Append(oneof.Name).AppendLine(" {");

                foreach (var oneofField in oneof.Fields)
                {
                    sb.Append(pad).Append("    ")
                      .Append(FormatType(oneofField)).Append(' ')
                      .Append(oneofField.Name).Append(" = ")
                      .Append(oneofField.FieldNumber).AppendLine(";");
                }

                sb.Append(pad).AppendLine("  }");
            }
            else
            {
                var label = field is { IsRepeated: true, IsMap: false } 
                    ? "repeated "
                    : "";

                sb.Append(pad).Append("  ").Append(label)
                  .Append(FormatType(field)).Append(' ')
                  .Append(field.Name).Append(" = ")
                  .Append(field.FieldNumber).AppendLine(";");
            }
        }

        foreach (var nestedEnum in msg.EnumTypes)
        {
            EmitEnum(nestedEnum, sb, indent + 1);
        }

        foreach (var nestedMsg in msg.NestedTypes)
        {
            if (nestedMsg.GetOptions()?.MapEntry == true)
            {
                continue;
            }

            EmitMessage(nestedMsg, sb, indent + 1);
        }

        sb.Append(pad).AppendLine("}");
    }

    private static void EmitEnum(EnumDescriptor enm, StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent * 2);

        sb.Append(pad).Append("enum ").Append(enm.Name).AppendLine(" {");

        foreach (var value in enm.Values)
        {
            sb.Append(pad).Append("  ").Append(value.Name).Append(" = ").Append(value.Number).AppendLine(";");
        }

        sb.Append(pad).AppendLine("}");
    }

    private static void EmitService(ServiceDescriptor svc, StringBuilder sb)
    {
        sb.Append("service ").Append(svc.Name).AppendLine(" {");

        foreach (var method in svc.Methods)
        {
            var requestStream = method.IsClientStreaming ? "stream " : "";
            var responseStream = method.IsServerStreaming ? "stream " : "";

            sb.Append("  rpc ").Append(method.Name)
              .Append(" (").Append(requestStream).Append('.').Append(method.InputType.FullName).Append(')')
              .Append(" returns ")
              .Append('(').Append(responseStream).Append('.').Append(method.OutputType.FullName).AppendLine(");");
        }

        sb.AppendLine("}");
    }

    private static string FormatType(FieldDescriptor field)
    {
        if (!field.IsMap)
        {
            return field.FieldType switch
            {
                FieldType.Message => $".{field.MessageType.FullName}",
                FieldType.Enum    => $".{field.EnumType.FullName}",
                _                 => ScalarTypeName(field)
            };
        }

        var mapDescriptor = field.MessageType;
        var keyField = mapDescriptor.FindFieldByNumber(1)!;
        var valueField = mapDescriptor.FindFieldByNumber(2)!;

        return $"map<{ScalarTypeName(keyField)}, {FormatType(valueField)}>";
    }

    private static string ScalarTypeName(FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.Double   => "double",
            FieldType.Float    => "float",
            FieldType.Int64    => "int64",
            FieldType.UInt64   => "uint64",
            FieldType.Int32    => "int32",
            FieldType.Fixed64  => "fixed64",
            FieldType.Fixed32  => "fixed32",
            FieldType.Bool     => "bool",
            FieldType.String   => "string",
            FieldType.Group    => "group",
            FieldType.Message  => $".{field.MessageType.FullName}",
            FieldType.Bytes    => "bytes",
            FieldType.UInt32   => "uint32",
            FieldType.Enum     => $".{field.EnumType.FullName}",
            FieldType.SFixed32 => "sfixed32",
            FieldType.SFixed64 => "sfixed64",
            FieldType.SInt32   => "sint32",
            FieldType.SInt64   => "sint64",
            _                  => "bytes"
        };
}
