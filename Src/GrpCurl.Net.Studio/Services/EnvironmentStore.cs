using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IEnvironmentStore" />. Reads from and writes to the live
///     <see cref="IWorkspaceStore.Current" />, cloning the workspace so an environment save never drops the
///     connection or TLS-profile lists (the same hazard the connections pane and TLS store guard against).
///     Deletes also purge the secret values of the environment's secret-typed variables (SEC, FR-132).
/// </summary>
internal sealed class EnvironmentStore(IWorkspaceStore workspace, ISecretStore secrets) : IEnvironmentStore
{
    public IReadOnlyList<WorkspaceEnvironment> Environments => workspace.Current.Environments;

    public Task SaveAsync(WorkspaceEnvironment environment, CancellationToken cancellationToken = default)
    {
        // Clone the live workspace so an environment save preserves connections, TLS profiles, and identity.
        var next = workspace.Current.Copy();
        next.Environments = next.Environments.Where(e => e.Id != environment.Id).Append(environment).ToList();

        return workspace.SaveAsync(next, cancellationToken);
    }

    public async Task DeleteAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        var current = workspace.Current;
        var environment = current.Environments.FirstOrDefault(e => e.Id == environmentId);

        if (environment is null)
        {
            return;
        }

        var next = current.Copy();
        next.Environments = next.Environments.Where(e => e.Id != environmentId).ToList();

        await workspace.SaveAsync(next, cancellationToken).ConfigureAwait(false);

        // Secret-typed variables hold their values only in the secret store; remove them once the
        // environment is gone so no orphan secrets linger (SEC-017 parity).
        foreach (var keyRef in environment.Variables
                     .Select(v => v.Value.SecretRef)
                     .Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            await secrets.DeleteAsync(keyRef!, cancellationToken).ConfigureAwait(false);
        }
    }
}
