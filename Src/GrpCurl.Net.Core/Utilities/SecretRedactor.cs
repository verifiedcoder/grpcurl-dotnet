using System.Text.RegularExpressions;
using Grpc.Core;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Decides which metadata header values should be hidden from verbose CLI output so
///     that bearer tokens, cookies, API keys, and other secrets do not leak into CI logs
///     or terminal captures. Redaction is opt-out via <c>--unsafe-show-secrets</c>.
///     Matches the contract documented in CODE-REVIEW.md P2 "Verbose Output Can Leak
///     Credentials".
/// </summary>
internal static partial class SecretRedactor
{
    private const string Placeholder = "[REDACTED]";

    /// <summary>
    ///     Headers that are always sensitive (exact match, case-insensitive).
    /// </summary>
    private static readonly HashSet<string> AlwaysRedact = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
        "x-api-key",
        "x-auth-token",
        "x-access-token",
        "x-csrf-token",
        "x-amz-security-token",
        "x-goog-iam-authorization-token"
    };

    [GeneratedRegex(@"(?:^|[-_])(token|secret|password|api[-_]?key|credential|signature|sig|nonce|jwt)$", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixPattern();

    /// <summary>
    ///     Returns <see langword="true"/> when the header should have its value replaced with
    ///     <see cref="Placeholder"/> in verbose output. Catches:
    ///     <list type="bullet">
    ///       <item><description>Names in <see cref="AlwaysRedact"/>.</description></item>
    ///       <item><description>Names whose final segment is <c>token</c>, <c>secret</c>,
    ///         <c>password</c>, <c>api-key</c>/<c>api_key</c>, <c>credential</c>,
    ///         <c>signature</c>/<c>sig</c>, <c>nonce</c>, or <c>jwt</c>.</description></item>
    ///       <item><description>Any binary metadata (<c>*-bin</c>) — base64-encoded values
    ///         are opaque, so we redact by default.</description></item>
    ///     </list>
    /// </summary>
    public static bool ShouldRedact(string headerName)
    {
        if (string.IsNullOrEmpty(headerName))
        {
            return false;
        }

        if (AlwaysRedact.Contains(headerName))
        {
            return true;
        }

        if (headerName.EndsWith("-bin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SuffixPattern().IsMatch(headerName);
    }

    /// <summary>
    ///     Returns a presentation value: the original if <paramref name="unsafeShowSecrets"/>
    ///     is true or the header is not sensitive, otherwise <see cref="Placeholder"/>.
    /// </summary>
    public static string FormatValue(string headerName, string value, bool unsafeShowSecrets)
        => unsafeShowSecrets || !ShouldRedact(headerName) ? value : Placeholder;

    /// <summary>
    ///     Renders <paramref name="metadata"/> as a sequence of <c>name: value</c> lines with
    ///     sensitive values redacted unless <paramref name="unsafeShowSecrets"/> is set.
    /// </summary>
    public static IEnumerable<string> FormatLines(Metadata metadata, bool unsafeShowSecrets)
    {
        foreach (var entry in metadata)
        {
            var value = entry.IsBinary
                ? Convert.ToBase64String(entry.ValueBytes)
                : entry.Value;

            yield return $"{entry.Key}: {FormatValue(entry.Key, value, unsafeShowSecrets)}";
        }
    }
}
