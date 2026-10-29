using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services;

internal sealed record HistoryPolicy(bool AllowHistory = true, bool AllowRequests = true,
    bool AllowResponses = true, string? Message = null) : IHistoryPolicy
{
    internal static string DefaultPath => Path.Combine(Path.GetDirectoryName(SecretFallbackPolicy.DefaultPath)!, "history-policy.json");

    internal static HistoryPolicy Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bytes = new byte[4097];
            var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > 4096) { return Invalid(); }
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            if (document.RootElement.ValueKind != JsonValueKind.Object) { return Invalid(); }
            var root = document.RootElement.EnumerateObject().ToArray();
            if (root.Length == 0) { return new(); }
            if (root.Length != 1 || root[0].Name != "history" || root[0].Value.ValueKind != JsonValueKind.Object) { return Invalid(); }
            var values = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var property in root[0].Value.EnumerateObject())
            {
                if (property.Name is not ("enabled" or "captureRequestBodies" or "captureResponses")
                    || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !values.TryAdd(property.Name, property.Value.GetBoolean())) { return Invalid(); }
            }
            var enabled = values.GetValueOrDefault("enabled", true);
            var requests = enabled && values.GetValueOrDefault("captureRequestBodies", true);
            var responses = enabled && values.GetValueOrDefault("captureResponses", true);
            return new(enabled, requests, responses, enabled && requests && responses ? null : "History capture is restricted by your organization.");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Invalid(); }
    }

    private static HistoryPolicy Invalid() => new(false, false, false,
        "History policy is invalid or unreadable. New history persistence is disabled. Ask your administrator to restore a valid history-policy.json and restart Studio.");
}
