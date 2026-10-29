using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Visitors;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrpCurl.Net.Studio.Services;

internal static class HistorySanitizer
{
    internal static async Task<HistoryEntry> SanitizeAsync(HistoryEntry entry, HistorySettings settings, IHistoryPolicy policy, CancellationToken token, bool truncate = true)
    {
        var request = entry.Request;
        string? body = null;
        var disposition = request.BodyDisposition;
        if (settings.CaptureRequestBodies && policy.AllowRequests && disposition != "omitted")
        {
            if (request.BodyFormat == "graphql")
            {
                body = await GraphQlAsync(request.Body, token).ConfigureAwait(false);
                disposition = "redacted";
            }
            else if (request.BodyFormat == "json")
            {
                body = Json(request.Body, settings.RedactFields, out var redacted);
                if (redacted) { disposition = "redacted"; }
            }
        }
        if (body is null) { disposition = "omitted"; }
        var capped = Cap(body ?? string.Empty, truncate ? settings.ResponseCapBytes : int.MaxValue, out var truncated);
        var response = settings.CaptureResponses && policy.AllowResponses && entry.Outcome.ResponseBody is { } text
            ? Json(text, settings.RedactFields, out _) : null;
        var responseTruncated = false;
        if (response is not null) { response = Cap(response, truncate ? settings.ResponseCapBytes : int.MaxValue, out responseTruncated); }
        var error = settings.CaptureRequestBodies && policy.AllowRequests ? entry.Outcome.ErrorMessage : null;
        if (error is not null) { error = Cap(error, truncate ? settings.ResponseCapBytes : int.MaxValue, out _); }
        return entry with
        {
            Request = request with { Body = capped, BodyDisposition = disposition,
                BodyTruncated = truncated || request.BodyTruncated,
                Headers = request.Headers.Select(h => SecretRedactor.ShouldRedact(h.Name) ? h with { Value = HistoryEntry.RedactedMarker } : h).ToArray() },
            Outcome = entry.Outcome with { ResponseBody = response, ResponseTruncated = responseTruncated || entry.Outcome.ResponseTruncated, ErrorMessage = error }
        };
    }

    private static string? Json(string text, IReadOnlyList<string>? fields, out bool redacted)
    {
        redacted = false;
        try
        {
            var node = JsonNode.Parse(text);
            var changed = false;
            void Visit(JsonNode? current)
            {
                if (current is JsonObject obj)
                {
                    foreach (var property in obj.ToArray())
                    {
                        if (SecretRedactor.ShouldRedact(property.Key) || (fields?.Contains(property.Key, StringComparer.OrdinalIgnoreCase) ?? false))
                        { obj[property.Key] = HistoryEntry.RedactedMarker; changed = true; }
                        else { Visit(property.Value); }
                    }
                }
                else if (current is JsonArray array) { foreach (var item in array) { Visit(item); } }
            }
            Visit(node);
            redacted = changed;
            return changed ? node?.ToJsonString() ?? "null" : text;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return null; }
    }

    private sealed class Context(CancellationToken token) : IASTVisitorContext
    {
        public CancellationToken CancellationToken => token;
        internal List<(int Start, int End, string Replacement)> Spans { get; } = [];
    }

    private sealed class LiteralVisitor : ASTVisitor<Context>
    {
        private static ValueTask Hide(ASTNode node, Context context, string replacement = "\"[redacted]\"")
        {
            context.Spans.Add((node.Location.Start, node.Location.End, replacement));
            return ValueTask.CompletedTask;
        }
        protected override ValueTask VisitStringValueAsync(GraphQLStringValue node, Context context) => Hide(node, context);
        protected override ValueTask VisitIntValueAsync(GraphQLIntValue node, Context context) => Hide(node, context);
        protected override ValueTask VisitFloatValueAsync(GraphQLFloatValue node, Context context) => Hide(node, context);
        protected override ValueTask VisitBooleanValueAsync(GraphQLBooleanValue node, Context context) => Hide(node, context);
        protected override ValueTask VisitEnumValueAsync(GraphQLEnumValue node, Context context) => Hide(node, context);
        protected override ValueTask VisitNullValueAsync(GraphQLNullValue node, Context context) => Hide(node, context);
    }

    private static async Task<string?> GraphQlAsync(string text, CancellationToken token)
    {
        try
        {
            var document = Parser.Parse(text, new ParserOptions { Ignore = IgnoreOptions.None, MaxDepth = 64 });
            // History accepts executable documents only; descriptions in schema definitions are not payloads we retain.
            if (document.Definitions.Any(d => d is not (GraphQLOperationDefinition or GraphQLFragmentDefinition))) { return null; }
            var context = new Context(token);
            await new LiteralVisitor().VisitAsync(document, context).ConfigureAwait(false);
            // Comments may be attached to scalar nodes or left unattached at EOF. The lexer
            // sees all of them, including comments adjacent to block strings, without regex guesses.
            for (var next = Lexer.Lex(text, 0); next.Kind != TokenKind.EOF; next = Lexer.Lex(text, next.End))
            {
                token.ThrowIfCancellationRequested();
                // The lexer includes a virtual terminator for a comment at EOF.
                if (next.Kind == TokenKind.COMMENT) { context.Spans.Add((next.Start, Math.Min(next.End, text.Length), string.Empty)); }
            }
            var builder = new StringBuilder(text);
            foreach (var span in context.Spans.Distinct().OrderByDescending(s => s.Start))
            { _ = builder.Remove(span.Start, span.End - span.Start).Insert(span.Start, span.Replacement); }
            return builder.ToString();
        }
        catch (GraphQLParser.Exceptions.GraphQLParserException) { return null; }
    }

    private static string Cap(string text, int limit, out bool truncated)
    {
        limit = Math.Max(0, limit);
        truncated = Encoding.UTF8.GetByteCount(text) > limit;
        if (!truncated) { return text; }
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > limit) { break; }
            bytes += rune.Utf8SequenceLength;
            _ = result.Append(rune.ToString());
        }
        return result.ToString();
    }
}
