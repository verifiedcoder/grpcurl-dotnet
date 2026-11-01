namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>PRD-025: the update feed may only direct the OS browser to HTTPS on GitHub.</summary>
internal static class UpdateUrlPolicy
{
    internal static bool IsAllowed(string? url)
        => !string.IsNullOrWhiteSpace(url)
           && Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase));
}
