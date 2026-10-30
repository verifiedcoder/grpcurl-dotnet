using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

public sealed record WorkspaceFileReference(
    string OwnerId, string Owner, string Field, string OriginalPath, string AbsolutePath,
    string? ResolvedTarget, bool IsDirectory, string? ApprovalReason)
{
    public string ApprovalKey => AbsolutePath + "\n" + (ResolvedTarget ?? AbsolutePath);
    public string Purpose => $"{Owner} — {Field}";
}

/// <summary>A read-only preview. Only its issuing store can commit it, after consent and a fingerprint check.</summary>
public abstract class WorkspacePreparation
{
    public abstract WorkspaceModel Workspace { get; }
    public abstract string Path { get; }
    public abstract IReadOnlyList<WorkspaceFileReference> References { get; }
    public bool RequiresApproval => References.Any(r => r.ApprovalReason is not null);
}

public sealed class WorkspaceTrustException(string message, Exception? inner = null, bool workspaceSaved = false) : InvalidOperationException(message, inner)
{
    public bool WorkspaceSaved { get; } = workspaceSaved;
}

public interface IWorkspaceTrustStore
{
    IReadOnlyList<string> ApprovedWorkspaces { get; }
    string? RecoveryMessage { get; }
    void Revoke(string? workspacePath);
}

public static class WorkspaceApproval
{
    public static async Task<WorkspacePreparation?> PrepareAsync(IWorkspaceStore store, IDialogService? dialogs, string path)
    {
        var preparation = await store.PrepareAsync(path);
        while (preparation.RequiresApproval)
        {
            if (dialogs is null || await dialogs.ShowDialogAsync(new WorkspaceApprovalDialogViewModel(preparation)) is not { } approved)
            {
                return null;
            }
            preparation = await store.ApproveAsync(preparation, approved);
        }
        return preparation;
    }
}
