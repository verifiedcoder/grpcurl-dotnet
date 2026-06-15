using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;
using System.Collections.Concurrent;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Resolves a <c>google.protobuf.Any</c> <c>type_url</c> to the <see cref="MessageDescriptor" />
///     of the embedded message, so the payload can be (de)serialized as binary protobuf per the
///     spec rather than mishandled as opaque JSON text.
/// </summary>
/// <remarks>
///     The pool is the transitive <see cref="FileDescriptor" /> closure of a context message
///     (typically the request/response type being processed) plus the built-in well-known types.
///     An <c>Any</c> whose payload type lives outside that closure (it is not statically linked
///     from the containing type) is unresolvable; callers fall back to a base64 rendering. Resolvers
///     are cached per context file since the closure is stable.
/// </remarks>
internal sealed class AnyTypeResolver
{
    private static readonly ConcurrentDictionary<FileDescriptor, AnyTypeResolver> Cache = new();

    private readonly Dictionary<string, MessageDescriptor> _byFullName = new(StringComparer.Ordinal);

    private AnyTypeResolver(MessageDescriptor context)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);

        IndexFile(context.File, visited);

        // Well-known types (Timestamp, Duration, wrappers, …) may appear in an Any even when
        // the context file does not import them directly.
        foreach (var file in WellKnownTypeRegistry.Descriptors.Values)
        {
            IndexFile(file, visited);
        }
    }

    public static AnyTypeResolver ForContext(MessageDescriptor context)
        => Cache.GetOrAdd(context.File, _ => new AnyTypeResolver(context));

    /// <summary>
    ///     Resolves the descriptor for an Any <c>type_url</c> (e.g.
    ///     <c>type.googleapis.com/pkg.Msg</c>), or <see langword="null" /> if the type is not in
    ///     the pool.
    /// </summary>
    public MessageDescriptor? Resolve(string? typeUrl)
    {
        if (string.IsNullOrEmpty(typeUrl))
        {
            return null;
        }

        var fullName = typeUrl.Contains('/', StringComparison.Ordinal)
            ? typeUrl[(typeUrl.LastIndexOf('/') + 1)..]
            : typeUrl;

        return _byFullName.GetValueOrDefault(fullName);
    }

    private void IndexFile(FileDescriptor file, HashSet<string> visited)
    {
        if (!visited.Add(file.Name))
        {
            return;
        }

        foreach (var message in file.MessageTypes)
        {
            IndexMessage(message);
        }

        foreach (var dependency in file.Dependencies)
        {
            IndexFile(dependency, visited);
        }
    }

    private void IndexMessage(MessageDescriptor message)
    {
        _byFullName[message.FullName] = message;

        foreach (var nested in message.NestedTypes)
        {
            IndexMessage(nested);
        }
    }
}
