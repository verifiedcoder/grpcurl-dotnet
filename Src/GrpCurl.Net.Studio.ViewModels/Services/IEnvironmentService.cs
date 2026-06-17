using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Resolves <c>${VAR}</c> placeholders from the active workspace environment, then the OS process
///     environment (FR-131; first match wins). With no active environment only the OS is consulted (the
///     CLI's degenerate case, FR-138). Secret-typed variables resolve through <c>ISecretStore</c> at use
///     time (FR-132). An unresolved variable is an error, never an empty string (FR-134).
/// </summary>
public interface IEnvironmentService
{
    /// <summary>The environments of the active workspace.</summary>
    IReadOnlyList<WorkspaceEnvironment> Environments { get; }

    /// <summary>The active environment, or null for the "No environment" state (FR-138).</summary>
    WorkspaceEnvironment? Active { get; }

    /// <summary>The active environment's id, or null.</summary>
    string? ActiveId { get; }

    /// <summary>Raised when the active environment changes (FR-133).</summary>
    event EventHandler? ActiveChanged;

    /// <summary>Selects the active environment by id; null selects "No environment".</summary>
    void SetActive(string? environmentId);

    /// <summary>Resolves a single variable (active environment first, then OS); null when unset anywhere.</summary>
    Task<string?> ResolveAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Expands every <c>${VAR}</c> in <paramref name="value" />. Throws
    ///     <see cref="InvalidOperationException" /> naming the variable + active environment if any is
    ///     unresolved (FR-134) — the caller fails the send before issuing an RPC.
    /// </summary>
    Task<string> ExpandAsync(string value, CancellationToken cancellationToken = default);
}
