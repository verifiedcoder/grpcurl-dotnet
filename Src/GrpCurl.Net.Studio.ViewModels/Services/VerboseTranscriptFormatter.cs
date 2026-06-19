using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Utilities;
using System.Text;
using static System.FormattableString;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Renders a <see cref="VerboseTranscript" /> as the plain-text Raw-tab transcript (FR-111, CLI
///     <c>-v</c> parity). Header values are redacted via Core's <see cref="SecretRedactor" /> — redaction
///     is on by default everywhere a header value renders (FR-112); the captured/exported text never
///     contains a secret literal.
/// </summary>
public static class VerboseTranscriptFormatter
{
    public static string Format(VerboseTranscript t)
    {
        var sb = new StringBuilder();

        _ = sb.AppendLine(Invariant($"Target:    {t.Target}"));
        _ = sb.AppendLine(Invariant($"Authority: {t.Authority ?? "(default)"}"));
        _ = sb.AppendLine();
        _ = sb.AppendLine("Request headers:");
        AppendHeaders(sb, t.RequestHeaders);
        _ = sb.AppendLine();
        _ = sb.AppendLine("Response headers:");
        AppendHeaders(sb, t.ResponseHeaders);
        _ = sb.AppendLine();
        _ = sb.AppendLine("Response trailers:");
        AppendHeaders(sb, t.ResponseTrailers);
        _ = sb.AppendLine();
        _ = sb.AppendLine(Invariant($"Messages:  {t.RequestMessages} sent, {t.ResponseMessages} received"));

        var detail = string.IsNullOrEmpty(t.Status.Detail) ? string.Empty : $" — {t.Status.Detail}";
        _ = sb.AppendLine(Invariant($"Status:    {t.Status.Code} {t.Status.CodeName}{detail}"));

        return sb.ToString().TrimEnd();
    }

    private static void AppendHeaders(StringBuilder sb, IReadOnlyList<MetadataItem> headers)
    {
        if (headers.Count == 0)
        {
            _ = sb.AppendLine("  (none)");
            return;
        }

        foreach (var h in headers)
        {
            _ = sb.AppendLine(Invariant($"  {h.Name}: {SecretRedactor.FormatValue(h.Name, h.Value, unsafeShowSecrets: false)}"));
        }
    }
}
