using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace GrpCurl.Net.Tests.Unit.Fixtures;

/// <summary>
/// Provides message descriptors for unit testing by loading protoset files.
/// </summary>
public static class TestDescriptorProvider
{
    private static readonly Lazy<FileDescriptorSet> TestProtoset = new(() =>
    {
        var protosetPath = GetTestProtosetPath("test.protoset");

        using var stream = File.OpenRead(protosetPath);

        return FileDescriptorSet.Parser.ParseFrom(stream);
    });

    private static readonly Lazy<FileDescriptorSet> WellKnownTypesProtoset = new(() =>
    {
        var protosetPath = GetTestProtosetPath("well-known-types.protoset");

        using var stream = File.OpenRead(protosetPath);

        return FileDescriptorSet.Parser.ParseFrom(stream);
    });

    private static readonly Lazy<Dictionary<string, MessageDescriptor>> MessageDescriptors = new(() =>
    {
        var descriptors = new Dictionary<string, MessageDescriptor>();

        // Build file descriptors from the protoset
        var byteStrings = TestProtoset.Value.File.Select(f => f.ToByteString()).ToList();
        var fileDescriptors = FileDescriptor.BuildFromByteStrings(byteStrings);

        foreach (var fileDescriptor in fileDescriptors)
        {
            foreach (var messageType in fileDescriptor.MessageTypes)
            {
                descriptors[messageType.FullName] = messageType;

                AddNestedTypes(messageType, descriptors);
            }
        }

        return descriptors;
    });

    private static readonly Lazy<Dictionary<string, MessageDescriptor>> WktMessageDescriptors = new(() =>
    {
        var descriptors = new Dictionary<string, MessageDescriptor>();

        // Build file descriptors from the well-known types protoset
        var byteStrings = WellKnownTypesProtoset.Value.File.Select(f => f.ToByteString()).ToList();
        var fileDescriptors = FileDescriptor.BuildFromByteStrings(byteStrings);

        foreach (var fileDescriptor in fileDescriptors)
        {
            foreach (var messageType in fileDescriptor.MessageTypes)
            {
                descriptors[messageType.FullName] = messageType;

                AddNestedTypes(messageType, descriptors);
            }
        }

        return descriptors;
    });

    private static void AddNestedTypes(MessageDescriptor parent, Dictionary<string, MessageDescriptor> descriptors)
    {
        foreach (var nested in parent.NestedTypes)
        {
            descriptors[nested.FullName] = nested;

            AddNestedTypes(nested, descriptors);
        }
    }

    private static string GetTestProtosetPath(string filename)
    {
        // Try multiple locations to find the protosets

        // 1. Try from the output directory (where tests are run)
        var assemblyLocation = typeof(TestDescriptorProvider).Assembly.Location;
        var outputDir = Path.GetDirectoryName(assemblyLocation);
        var pathFromOutput = Path.Combine(outputDir ?? "", "TestProtosets", filename);

        if (File.Exists(pathFromOutput))
        {
            return pathFromOutput;
        }

        // 2. Navigate up from assembly location to find Tests directory
        var testDir = outputDir;

        while (testDir is not null)
        {
            var testsPath = Path.Combine(testDir, "Tests", "TestProtosets", filename);

            if (File.Exists(testsPath))
            {
                return testsPath;
            }

            var directPath = Path.Combine(testDir, "TestProtosets", filename);

            if (File.Exists(directPath))
            {
                return directPath;
            }

            var parent = Directory.GetParent(testDir);

            if (parent is null)
            {
                break;
            }

            testDir = parent.FullName;
        }

        // 3. Try from current working directory
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "TestProtosets", filename);

        return File.Exists(cwdPath)
            ? cwdPath :
            throw new FileNotFoundException($"Test protoset not found: {filename}. Searched in output directory and parent directories.");
    }

    /// <summary>
    /// Gets a message descriptor by full name from the test protoset.
    /// </summary>
    public static MessageDescriptor GetMessageDescriptor(string fullName)
        => MessageDescriptors.Value.TryGetValue(fullName, out var descriptor)
            ? descriptor
            : throw new ArgumentException($"Message type '{fullName}' not found in test protoset. Available types: {string.Join(", ", MessageDescriptors.Value.Keys)}");

    /// <summary>
    /// Gets a message descriptor for a well-known type.
    /// </summary>
    public static MessageDescriptor GetWellKnownTypeDescriptor(string fullName)
        => WktMessageDescriptors.Value.TryGetValue(fullName, out var descriptor)
            ? descriptor
            : throw new ArgumentException($"Well-known type '{fullName}' not found in protoset. Available types: {string.Join(", ", WktMessageDescriptors.Value.Keys)}");

    /// <summary>
    /// Gets the SimpleRequest message descriptor for testing.
    /// </summary>
    public static MessageDescriptor SimpleRequest
        => GetMessageDescriptor("testing.SimpleRequest");

    /// <summary>
    /// Gets the Payload message descriptor for testing.
    /// </summary>
    public static MessageDescriptor Payload
        => GetMessageDescriptor("testing.Payload");

    /// <summary>
    /// Gets the StreamingOutputCallRequest message descriptor for testing.
    /// </summary>
    public static MessageDescriptor StreamingOutputCallRequest
        => GetMessageDescriptor("testing.StreamingOutputCallRequest");
}
