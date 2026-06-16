using System.Text.Json;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Splits a streaming request body into individual message JSONs (FR-082 "Load batch…"), accepting
///     either a JSON array of messages <c>[{…},{…}]</c> or concatenated top-level objects
///     <c>{…} {…}</c> — the same grammar the CLI's <c>-d</c> streaming input uses.
/// </summary>
public static class JsonMessageSplitter
{
    public static IReadOnlyList<string> Split(string input)
    {
        var text = input.Trim();

        if (text.Length == 0)
        {
            return [];
        }

        if (text[0] == '[')
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray().Select(e => e.GetRawText()).ToList();
            }
        }

        // Concatenated top-level values: split on depth-0 boundaries, respecting strings/escapes.
        var results = new List<string>();
        var depth = 0;
        var start = -1;
        var inString = false;
        var escape = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (start < 0 && !char.IsWhiteSpace(c))
            {
                start = i;
            }

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']':
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        results.Add(text[start..(i + 1)]);
                        start = -1;
                    }

                    break;
            }
        }

        if (results.Count == 0)
        {
            results.Add(text);
        }

        return results;
    }
}
