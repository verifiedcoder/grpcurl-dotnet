using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     One entry in the connection editor's TLS profile picker: either a real workspace
///     <see cref="TlsProfile" /> or the system-default sentinel (<see cref="Profile" /> is null), which
///     leaves the connection on OS-trust validation.
/// </summary>
public sealed record TlsProfileOption(TlsProfile? Profile)
{
    public string Display => Profile?.Name ?? "System default (no profile)";
}
