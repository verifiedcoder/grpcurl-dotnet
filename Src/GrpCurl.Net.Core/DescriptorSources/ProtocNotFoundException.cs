namespace GrpCurl.Net.DescriptorSources;

/// <summary>
///     Thrown when <c>--proto</c> is used but the <c>protoc</c> compiler cannot be found on
///     PATH. Distinct from <see cref="FileNotFoundException" /> so command handlers can render
///     the purpose-built install guidance instead of a generic "file not found" message.
/// </summary>
/// <param name="message">The error message, including install guidance.</param>
internal sealed class ProtocNotFoundException(string message) : Exception(message);
