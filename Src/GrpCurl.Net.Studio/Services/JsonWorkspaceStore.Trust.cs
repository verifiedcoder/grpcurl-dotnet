using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Security.Cryptography;
using System.Text;

namespace GrpCurl.Net.Studio.Services;

internal sealed partial class JsonWorkspaceStore
{
    private readonly WorkspaceTrustStore _trust;
    private string? _admittedFingerprint;
    private long _persistenceVersion;
    private string? _admittedLocation;
    private string? _admissionId;
    private string _generation = "missing";
    private HashSet<string> _activeTargets = new(WorkspaceReferenceAssessment.Comparer);
    private HashSet<string> _activeApprovals = new(WorkspaceReferenceAssessment.Comparer);
    internal IDialogService? ApprovalDialogs { get; set; }
    internal IUiDispatcher? Dispatcher { get; set; }
    public IWorkspaceTrustStore Trust => _trust;
    public string? PendingStartupPath { get; private set; }
    public string? TrustMessage { get; private set; }
    internal string AuthorizationPath => CurrentPath ?? Path.Combine(Path.GetDirectoryName(_defaultPath)!, "untitled-" + Current.Id + ".json");

    private sealed class PreparedDocument(JsonWorkspaceStore issuer, string path, string location, string fingerprint,
        WorkspaceModel source, string generation, string? rememberedId, HashSet<string> approvals,
        IReadOnlyList<WorkspaceFileReference> references) : WorkspacePreparation
    {
        internal JsonWorkspaceStore Issuer { get; } = issuer;
        public override string Path { get; } = path;
        internal string Location { get; } = location;
        internal string Fingerprint { get; } = fingerprint;
        internal WorkspaceModel Source { get; } = source;
        internal string Generation { get; } = generation;
        internal string? RememberedId { get; } = rememberedId;
        internal HashSet<string> Approvals { get; } = approvals;
        public override IReadOnlyList<WorkspaceFileReference> References { get; } = references;
        public override WorkspaceModel Workspace => WorkspacePathPortability.ToAbsolute(
            WorkspaceSerializer.Deserialize(WorkspaceSerializer.Serialize(Source)), DirectoryOf(Path));
    }

    private static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Location(string path) => WorkspaceReferenceAssessment.ResolveTarget(Path.GetFullPath(path), _ => true).Target;

    public async Task<WorkspacePreparation> PrepareAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var model = WorkspaceSerializer.Deserialize(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        var fingerprint = Fingerprint(bytes);
        var location = Location(path);
        var data = _trust.Read();
        TrustMessage = _trust.RecoveryMessage;
        var record = data.Workspaces.FirstOrDefault(w => w.Location.Equals(location, WorkspaceReferenceAssessment.Comparison) && w.Fingerprint == fingerprint);
        var approvals = new HashSet<string>(record?.Approvals ?? [], WorkspaceReferenceAssessment.Comparer);
        var references = WorkspaceReferenceAssessment.Assess(model, path, approvals);
        if (record is not null)
        {
            RemoveRetargetedApprovals(approvals, references, record.Targets);
            references = WorkspaceReferenceAssessment.Assess(model, path, approvals);
        }
        return new PreparedDocument(this, path, location, fingerprint, model, data.Generation, record?.AdmissionId, approvals, references);
    }

    public async Task<WorkspacePreparation> ApproveAsync(WorkspacePreparation preparation, IReadOnlyList<string> approvedKeys, CancellationToken cancellationToken = default)
    {
        var document = Owned(preparation);
        await CheckDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        var requested = document.References.Where(r => r.ApprovalReason is not null).Select(r => r.ApprovalKey).ToHashSet(WorkspaceReferenceAssessment.Comparer);
        if (!requested.SetEquals(approvedKeys))
        {
            throw new WorkspaceTrustException("Every required reference must be explicitly approved. The workspace was not opened or imported.");
        }
        var approvals = new HashSet<string>(document.Approvals, WorkspaceReferenceAssessment.Comparer);
        approvals.UnionWith(approvedKeys);
        return new PreparedDocument(this, document.Path, document.Location, document.Fingerprint, document.Source,
            document.Generation, document.RememberedId, approvals,
            WorkspaceReferenceAssessment.Assess(document.Source, document.Path, approvals));
    }

    private PreparedDocument Owned(WorkspacePreparation preparation)
        => preparation is PreparedDocument document && ReferenceEquals(document.Issuer, this) ? document
            : throw new WorkspaceTrustException("This workspace preparation belongs to another store. Select the file again.");

    private async Task CheckDocumentAsync(PreparedDocument document, CancellationToken cancellationToken)
    {
        if (Fingerprint(await File.ReadAllBytesAsync(document.Path, cancellationToken).ConfigureAwait(false)) != document.Fingerprint
            || !Location(document.Path).Equals(document.Location, WorkspaceReferenceAssessment.Comparison))
        {
            throw new WorkspaceTrustException("The selected workspace changed during review. Select it again to assess the new document; nothing was activated.");
        }
        var data = _trust.Read();
        if (data.Generation != document.Generation || document.RememberedId is not null
            && !data.Workspaces.Any(w => w.AdmissionId == document.RememberedId))
        {
            throw new WorkspaceTrustException("Workspace approval changed or was revoked during review. Select the file again.");
        }
    }

    public async Task ValidatePreparationAsync(WorkspacePreparation preparation, CancellationToken cancellationToken = default)
    {
        var document = Owned(preparation);
        await CheckDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        var references = WorkspaceReferenceAssessment.Assess(document.Source, document.Path, document.Approvals);
        if (document.RequiresApproval || references.Any(r => r.ApprovalReason is not null)
            || !references.Select(r => r.ApprovalKey).ToHashSet(WorkspaceReferenceAssessment.Comparer)
                .SetEquals(document.References.Select(r => r.ApprovalKey)))
        {
            throw new WorkspaceTrustException("Workspace file references need approval or their link targets changed. Open the workspace again to review each path.");
        }
    }

    public async Task<WorkspaceModel> CommitOpenAsync(WorkspacePreparation preparation, CancellationToken cancellationToken = default)
    {
        await ValidatePreparationAsync(preparation, cancellationToken).ConfigureAwait(false);
        var document = Owned(preparation);
        WorkspaceTrustRecord? record = null;
        if (document.Approvals.Count > 0)
        {
            record = _trust.Remember(document.Location, document.Fingerprint, document.Approvals, document.References.Select(r => r.ApprovalKey), document.Generation, document.RememberedId);
        }
        // Final check after approval persistence, immediately before the activation commit point.
        await CheckDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        CancelPendingFlush();
        ReleaseHeldLock();
        Current = document.Workspace;
        CurrentPath = document.Path;
        _admittedLocation = document.Location;
        _admittedFingerprint = document.Fingerprint;
        _admissionId = record?.AdmissionId;
        _activeApprovals = new(document.Approvals, WorkspaceReferenceAssessment.Comparer);
        _activeTargets = document.References.Select(r => r.ApprovalKey).ToHashSet(WorkspaceReferenceAssessment.Comparer);
        _generation = document.Generation;
        PendingStartupPath = null;
        SetDirty(false);
        RefreshReadOnly(CurrentPath);
        AcquireLockFor(CurrentPath);
        await PromoteRecentAsync(CurrentPath, cancellationToken).ConfigureAwait(false);
        return Current;
    }

    public async Task CommitImportAsync(WorkspacePreparation preparation, CancellationToken cancellationToken = default)
    {
        await ValidatePreparationAsync(preparation, cancellationToken).ConfigureAwait(false);
        ValidateActiveAuthorization();
        var document = Owned(preparation);
        var (merged, _) = WorkspaceMerger.Merge(Current, document.Workspace);
        // Bind the admitted source locations to the final IDs and portable paths. Contained source
        // references were admitted automatically, and become explicit local grants in the destination.
        var transferred = new HashSet<string>(_activeApprovals, WorkspaceReferenceAssessment.Comparer);
        transferred.UnionWith(document.Approvals);
        transferred.UnionWith(document.References.Select(r => r.ApprovalKey));
        var previousApprovals = _activeApprovals;
        var previousTargets = _activeTargets;
        var previousGeneration = _generation;
        var previousPersistence = _persistenceVersion;
        try
        {
            await AuthorizeMutationAsync(merged, AuthorizationPath, transferred, cancellationToken).ConfigureAwait(false);
            await ValidatePreparationAsync(preparation, cancellationToken).ConfigureAwait(false);
            if (CurrentPath is not null && _haveLock && !_lock.StillOwned(CurrentPath))
            {
                SetLocked(true, WorkspaceLockManager.Holder(CurrentPath));
            }
            var canPersist = CurrentPath is not null && !_isReadOnly && !_isLockedByAnother;
            if (canPersist)
            {
                await PersistAuthorizedAsync(CurrentPath!, merged, cancellationToken, preparation).ConfigureAwait(false);
            }
            Current = merged;
            CancelPendingFlush();
            SetDirty(!canPersist);
        }
        catch
        {
            if (_persistenceVersion != previousPersistence)
            {
                // A committed workspace write followed by failed trust persistence is a partial result.
                Current = merged;
                CancelPendingFlush();
                SetDirty(false);
            }
            else
            {
                _activeApprovals = previousApprovals;
                _activeTargets = previousTargets;
                _generation = previousGeneration;
            }
            throw;
        }
    }

    internal void ValidateActiveAuthorization()
    {
        var data = _trust.Read();
        TrustMessage = _trust.RecoveryMessage;
        if (_activeApprovals.Count > 0 && (_admissionId is null && data.Generation != _generation
            || _admissionId is not null && !data.Workspaces.Any(w => w.AdmissionId == _admissionId
                && w.Fingerprint == _admittedFingerprint && w.Location.Equals(_admittedLocation, WorkspaceReferenceAssessment.Comparison))))
        {
            throw new WorkspaceTrustException(TrustMessage ?? "Workspace approval was revoked. Open the workspace again to approve its file references.");
        }
        if (_admittedFingerprint is not null && CurrentPath is not null
            && (Fingerprint(File.ReadAllBytes(CurrentPath)) != _admittedFingerprint
                || !Location(CurrentPath).Equals(_admittedLocation, WorkspaceReferenceAssessment.Comparison)))
        {
            throw new WorkspaceTrustException("The workspace file changed outside Studio. Reload it to review its references before accessing files or saving.");
        }
    }

    internal IReadOnlyList<WorkspaceFileReference> AuthorizeConnection(SavedConnection connection)
    {
        ValidateActiveAuthorization();
        var model = new WorkspaceModel
        {
            Connections = [connection],
            TlsProfiles = Current.TlsProfiles.Where(p => p.Id == connection.TlsProfileId).ToList()
        };
        var references = WorkspaceReferenceAssessment.Assess(model, AuthorizationPath, _activeApprovals);
        if (references.FirstOrDefault(r => r.ApprovalReason is not null
            || _activeApprovals.Any(a => a.StartsWith(r.AbsolutePath + "\n", WorkspaceReferenceAssessment.Comparison))
                && !_activeTargets.Contains(r.ApprovalKey)) is { } denied)
        {
            throw new WorkspaceTrustException($"File access needs approval: {denied.Purpose}: '{denied.AbsolutePath}' → '{denied.ResolvedTarget}'. Save or reopen the workspace to review this reference.");
        }
        return references;
    }

    private async Task AuthorizeMutationAsync(WorkspaceModel model, string path, HashSet<string>? transferred, CancellationToken cancellationToken)
    {
        ValidateActiveAuthorization();
        var approvals = new HashSet<string>(transferred ?? (path == AuthorizationPath ? _activeApprovals : []), WorkspaceReferenceAssessment.Comparer);
        var generation = _trust.Read().Generation;
        if (transferred is null)
        {
            RemoveRetargetedApprovals(approvals, WorkspaceReferenceAssessment.Assess(model, path, approvals), _activeTargets);
        }
        IReadOnlyList<WorkspaceFileReference> references;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            references = WorkspaceReferenceAssessment.Assess(model, path, approvals);
            if (references.All(r => r.ApprovalReason is null)) { break; }
            if (ApprovalDialogs is null) { throw new WorkspaceTrustException("External workspace file references require approval before saving. Open the workspace in Studio to review its paths."); }
            var preview = new PreparedDocument(this, path, path, "", model, generation, null, approvals, references);
            Task<IReadOnlyList<string>?> Show() => ApprovalDialogs.ShowDialogAsync(new WorkspaceApprovalDialogViewModel(preview));
            var approved = Dispatcher is null ? await Show().ConfigureAwait(false) : await (await Dispatcher.InvokeAsync(Show).ConfigureAwait(false)).ConfigureAwait(false);
            if (approved is null || !references.Where(r => r.ApprovalReason is not null).All(r => approved.Contains(r.ApprovalKey)))
            {
                throw new WorkspaceTrustException("File reference approval was cancelled. Existing workspace files were retained; your edits remain available.");
            }
            approvals.UnionWith(approved);
        }
        if (_trust.Read().Generation != generation) { throw new WorkspaceTrustException("Approvals were revoked during review. Try again."); }
        _activeApprovals = approvals;
        _activeTargets = references.Select(r => r.ApprovalKey).ToHashSet(WorkspaceReferenceAssessment.Comparer);
        _generation = generation;
    }

    private static void RemoveRetargetedApprovals(HashSet<string> approvals, IReadOnlyList<WorkspaceFileReference> references, IEnumerable<string> boundTargets)
    {
        var targets = boundTargets.ToHashSet(WorkspaceReferenceAssessment.Comparer);
        foreach (var reference in references.Where(r => !targets.Contains(r.ApprovalKey)))
        {
            _ = approvals.RemoveWhere(a => a.StartsWith(reference.AbsolutePath + "\n", WorkspaceReferenceAssessment.Comparison));
        }
    }

    private async Task PersistAuthorizedAsync(string path, WorkspaceModel model, CancellationToken cancellationToken, WorkspacePreparation? source = null)
    {
        ValidateActiveAuthorization();
        var references = WorkspaceReferenceAssessment.Assess(model, path, _activeApprovals);
        if (references.Any(r => r.ApprovalReason is not null) || !_activeTargets.SetEquals(references.Select(r => r.ApprovalKey)))
        {
            throw new WorkspaceTrustException("A file reference or link target changed before saving. Review the references again.");
        }
        var paths = references.Select(r => r.AbsolutePath).ToHashSet(WorkspaceReferenceAssessment.Comparer);
        _activeApprovals = _activeApprovals.Where(a => paths.Contains(a.Split('\n')[0])).ToHashSet(WorkspaceReferenceAssessment.Comparer);
        if (source is not null) { await ValidatePreparationAsync(source, cancellationToken).ConfigureAwait(false); }
        var committedFingerprint = await WriteAtomicAsync(path, model, cancellationToken).ConfigureAwait(false);
        _persistenceVersion++;
        SetDirty(false);
        // Workspace persistence has committed. A later trust-store failure cannot roll it back.
        _admittedFingerprint = committedFingerprint;
        try
        {
            _admittedLocation = Location(path);
            if (_activeApprovals.Count > 0)
            {
                var record = _trust.Remember(_admittedLocation, _admittedFingerprint, _activeApprovals, _activeTargets, _generation, _admissionId);
                _admissionId = record.AdmissionId;
            }
        }
        catch (Exception ex) when (ex is WorkspaceTrustException or IOException)
        {
            _activeApprovals = new(WorkspaceReferenceAssessment.Comparer);
            _admissionId = null;
            TrustMessage = "The workspace was saved, but its approvals were not saved. No new approval is remembered; open it again to approve access. " + ex.Message;
            throw new WorkspaceTrustException(TrustMessage, ex, workspaceSaved: true);
        }
    }
}
