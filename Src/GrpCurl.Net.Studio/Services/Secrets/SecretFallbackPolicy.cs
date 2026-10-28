using GrpCurl.Net.Studio.ViewModels.Models;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services.Secrets;

/// <summary>One machine-wide policy key, read once per session. Invalid policy never grants fallback.</summary>
internal sealed record SecretFallbackPolicy(SecretFallbackMode Mode, string? Problem = null)
{
    internal static string DefaultPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GrpCurlNet.Studio", "secret-policy.json")
        : OperatingSystem.IsMacOS()
            ? "/Library/Application Support/GrpCurlNet.Studio/secret-policy.json"
            : "/etc/grpcurl-studio/secret-policy.json";

    internal static SecretFallbackPolicy Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            // Enforce the bound while reading, including a growing file.
            var bytes = new byte[4097];
            var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > 4096)
            {
                return Invalid();
            }

            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid();
            }

            var properties = document.RootElement.EnumerateObject().ToList();
            if (properties.Count != 1 || properties[0].Name != "secretFallback"
                || properties[0].Value.ValueKind != JsonValueKind.String)
            {
                return Invalid();
            }

            return properties[0].Value.GetString() switch
            {
                "allow" => new(SecretFallbackMode.Allow),
                "block" => new(SecretFallbackMode.Block),
                "requireConsent" => new(SecretFallbackMode.RequireConsent),
                _ => Invalid()
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(SecretFallbackMode.RequireConsent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Invalid();
        }

        static SecretFallbackPolicy Invalid() => new(SecretFallbackMode.Block,
            "The machine secret policy cannot be read or is invalid. Ask your administrator to repair secret-policy.json. Fallback is blocked.");
    }
}
