namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>One import's explicit bypass selections, bound to the exact reviewed preparation; never serialized.</summary>
public sealed class WorkspaceImportDecision
{
    private readonly WorkspacePreparation _preparation;

    public WorkspaceImportDecision(WorkspacePreparation preparation, IReadOnlyCollection<string> approvedBypassProfileIds)
    {
        _preparation = preparation;
        ApprovedBypassProfileIds = Array.AsReadOnly(approvedBypassProfileIds.ToArray());
        ValidateFor(preparation);
    }

    public IReadOnlyList<string> ApprovedBypassProfileIds { get; }

    public void ValidateFor(WorkspacePreparation preparation)
    {
        if (!ReferenceEquals(preparation, _preparation))
        {
            throw new WorkspaceTrustException("TLS bypass consent belongs to a different workspace preparation. Review the import again.");
        }
        preparation.ValidateImportSource();
        _ = WorkspaceMerger.ValidateSelections(preparation.Workspace, ApprovedBypassProfileIds);
    }
}
