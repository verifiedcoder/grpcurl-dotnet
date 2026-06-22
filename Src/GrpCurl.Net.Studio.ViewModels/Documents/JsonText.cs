using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Small helpers for the editor "Format document" action (SPEC-020 §5, Ctrl+Shift+F): pretty-print
///     a JSON payload, leaving anything that isn't valid JSON untouched so a mid-edit body is never lost.
/// </summary>
internal static class JsonText
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>
    ///     Re-serializes <paramref name="json" /> with two-space indentation. Returns false (and leaves
    ///     <paramref name="formatted" /> empty) when the input is blank or not valid JSON, or when it is
    ///     already byte-for-byte identical to the indented form (so the caller can skip a no-op edit).
    /// </summary>
    public static bool TryPrettyPrint(string json, out string formatted)
    {
        formatted = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var node = JsonNode.Parse(json, documentOptions: ParseOptions);
            var indented = node?.ToJsonString(IndentedOptions) ?? string.Empty;

            if (indented.Length == 0 || indented == json)
            {
                return false;
            }

            formatted = indented;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
