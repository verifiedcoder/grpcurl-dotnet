using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Utility for exporting FileDescriptorSet to files with transitive dependencies.
/// </summary>
internal static class ProtosetExporter
{
    /// <summary>
    ///     Writes a FileDescriptorSet to a file, including transitive dependencies.
    /// </summary>
    /// <param name="descriptorSource">Source of descriptors</param>
    /// <param name="outputPath">Output file path</param>
    /// <param name="symbols">Symbols to include (services, messages, etc.)</param>
    public static async Task WriteProtosetAsync(
        IDescriptorSource descriptorSource,
        string? outputPath,
        params string[] symbols)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            return; // Nothing to do
        }

        var fileDescriptorSet = await BuildFileDescriptorSetAsync(descriptorSource, symbols);

        // Serialize FileDescriptorSet to bytes using protobuf WriteTo
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, true))
        {
            fileDescriptorSet.WriteTo(output);
        }

        await File.WriteAllBytesAsync(outputPath, stream.ToArray());
    }

    /// <summary>
    ///     Builds a FileDescriptorSet containing specified symbols and their transitive dependencies.
    /// </summary>
    /// <param name="descriptorSource">Source of descriptors</param>
    /// <param name="symbols">Symbols to include</param>
    /// <returns>FileDescriptorSet with all files in topological order</returns>
    private static async Task<FileDescriptorSet> BuildFileDescriptorSetAsync(
        IDescriptorSource descriptorSource,
        string[] symbols)
    {
        var fds = new FileDescriptorSet();

        HashSet<string> processed = [];

        // If no symbols specified, include all files from source
        if (symbols.Length == 0)
        {
            var sourceFds = descriptorSource.FileDescriptorSet;

            return sourceFds ?? fds; // Empty if source has nothing
        }

        // For each symbol, find its descriptor and add its file with dependencies
        foreach (var symbol in symbols)
        {
            var descriptor = await descriptorSource.FindSymbolAsync(symbol);

            if (descriptor is not null)
            {
                AddFileWithDependencies(fds, processed, descriptor.File);
            }
        }

        return fds;
    }

    /// <summary>
    ///     Recursively adds a file and all its dependencies to the FileDescriptorSet.
    ///     Files are added in topological order (dependencies before dependents).
    /// </summary>
    /// <param name="fds">FileDescriptorSet to populate</param>
    /// <param name="processed">Set of already processed file names</param>
    /// <param name="fileDescriptor">File to add</param>
    private static void AddFileWithDependencies(
        FileDescriptorSet fds,
        HashSet<string> processed,
        FileDescriptor fileDescriptor)
    {
        // Skip if already processed
        if (processed.Contains(fileDescriptor.Name))
        {
            return;
        }

        // Add dependencies first (topological order - dependencies before dependents)
        foreach (var dependency in fileDescriptor.Dependencies)
        {
            AddFileWithDependencies(fds, processed, dependency);
        }

        // Now add this file - parse FileDescriptorProto from serialized data
        var fileProto = FileDescriptorProto.Parser.ParseFrom(fileDescriptor.SerializedData);

        fds.File.Add(fileProto);

        processed.Add(fileDescriptor.Name);
    }
}