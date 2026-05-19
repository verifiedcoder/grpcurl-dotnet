using System.Reflection;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Produces the default <c>User-Agent</c> header string for outbound gRPC requests by reading
///     the executing assembly's informational version (set via the <c>Version</c> MSBuild property).
///     Falls back to <c>"grpcurl-dotnet/0.0.0"</c> when no informational version is available.
/// </summary>
internal static class UserAgentProvider
{
    private const string ProductName = "grpcurl-dotnet";

    private static readonly Lazy<string> DefaultValue = new(ComputeDefault);

    /// <summary>The user-agent that outbound requests use when the CLI does not override it.</summary>
    public static string Default => DefaultValue.Value;

    private static string ComputeDefault()
    {
        var assembly = typeof(UserAgentProvider).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+', 2)[0];

        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        return $"{ProductName}/{version}";
    }
}