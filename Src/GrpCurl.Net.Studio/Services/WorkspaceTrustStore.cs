using GrpCurl.Net.Studio.Services.Secrets;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.Services;

internal sealed record WorkspaceTrustRecord(string Location, string Fingerprint, string AdmissionId, List<string> Approvals, List<string> Targets);
internal sealed class WorkspaceTrustData
{
    public int Version { get; set; } = 1;
    public string Generation { get; set; } = Guid.NewGuid().ToString("N");
    public List<WorkspaceTrustRecord> Workspaces { get; set; } = [];
}

[JsonSerializable(typeof(WorkspaceTrustData))]
internal sealed partial class WorkspaceTrustJsonContext : JsonSerializerContext;

/// <summary>Private local approvals. Atomic replacement and a stable lock serialize cooperating Studio writers.</summary>
internal sealed class WorkspaceTrustStore(string path, SecureSecretFile? files = null) : IWorkspaceTrustStore
{
    private readonly SecureSecretFile _files = files ?? new();
    private readonly Lock _gate = new();
    internal string StoragePath { get; } = path;
    public string? RecoveryMessage { get; private set; }
    public IReadOnlyList<string> ApprovedWorkspaces => Read().Workspaces.Select(w => w.Location).Distinct(WorkspaceReferenceAssessment.Comparer).Order().ToArray();

    internal WorkspaceTrustData Read()
    {
        lock (_gate)
        {
            try
            {
                var json = _files.Read(StoragePath);
                var data = json is null ? new WorkspaceTrustData { Generation = "missing" }
                    : JsonSerializer.Deserialize(json, WorkspaceTrustJsonContext.Default.WorkspaceTrustData);
                if (data is null || data.Version != 1 || string.IsNullOrWhiteSpace(data.Generation)
                    || data.Workspaces is null || data.Workspaces.Any(w => w is null || string.IsNullOrWhiteSpace(w.Location)
                        || !Path.IsPathFullyQualified(w.Location) || w.Fingerprint.Length != 64 || string.IsNullOrWhiteSpace(w.AdmissionId)
                        || w.Targets is null || w.Targets.Any(t => t is null || t.Split('\n').Length != 2)
                        || w.Approvals is null || w.Approvals.Any(a => a is null || a.Split('\n').Length != 2)))
                {
                    throw new JsonException("Unsupported or malformed trust records.");
                }
                RecoveryMessage = null;
                return data;
            }
            catch (Exception ex) when (ex is JsonException or SecretStorageException or IOException or UnauthorizedAccessException or NullReferenceException)
            {
                RecoveryMessage = $"Workspace approvals could not be read from '{StoragePath}'. No remembered approvals were granted. Restore access, or use Settings → Security → Revoke all to reset approval storage. {ex.Message}";
                return new WorkspaceTrustData { Generation = "unavailable" };
            }
        }
    }

    internal WorkspaceTrustRecord? Find(string location, string fingerprint)
        => Read().Workspaces.FirstOrDefault(w => w.Location.Equals(location, WorkspaceReferenceAssessment.Comparison) && w.Fingerprint == fingerprint);

    internal WorkspaceTrustRecord Remember(string location, string fingerprint, IEnumerable<string> approvals, IEnumerable<string> targets, string generation, string? admissionId = null)
    {
        lock (_gate)
        {
            using var held = AcquireLock();
            var data = Read();
            if (RecoveryMessage is not null) { throw new WorkspaceTrustException(RecoveryMessage); }
            if (data.Generation != generation)
            {
                throw new WorkspaceTrustException("Workspace approval was revoked while this operation was pending. Open the workspace again to review its references.");
            }
            var record = new WorkspaceTrustRecord(location, fingerprint, admissionId ?? Guid.NewGuid().ToString("N"), approvals.Distinct(WorkspaceReferenceAssessment.Comparer).ToList(), targets.Distinct(WorkspaceReferenceAssessment.Comparer).ToList());
            _ = data.Workspaces.RemoveAll(w => w.Location.Equals(location, WorkspaceReferenceAssessment.Comparison));
            if (record.Approvals.Count > 0) { data.Workspaces.Add(record); }
            Write(data);
            return record;
        }
    }

    public void Revoke(string? workspacePath)
    {
        lock (_gate)
        {
            using var held = AcquireLock();
            var data = Read();
            if (RecoveryMessage is not null && workspacePath is not null) { throw new WorkspaceTrustException(RecoveryMessage); }
            if (workspacePath is null) { data.Workspaces.Clear(); }
            else
            {
                var location = Path.GetFullPath(workspacePath);
                // Listed canonical records are revoked verbatim even if that file has since become
                // a link. Also accept an original selected alias (e.g. macOS /var → /private/var).
                if (!data.Workspaces.Any(w => w.Location.Equals(location, WorkspaceReferenceAssessment.Comparison)))
                {
                    location = WorkspaceReferenceAssessment.ResolveTarget(location, _ => true).Target;
                }
                _ = data.Workspaces.RemoveAll(w => w.Location.Equals(location, WorkspaceReferenceAssessment.Comparison));
            }
            data.Generation = Guid.NewGuid().ToString("N");
            Write(data);
            RecoveryMessage = null;
        }
    }

    private FileStream AcquireLock()
    {
        try
        {
            return _files.TryOpenExclusive(StoragePath + ".lock")
                ?? throw new WorkspaceTrustException("Workspace approvals are being changed by another Studio instance. Try again.");
        }
        catch (SecretStorageException ex)
        {
            throw new WorkspaceTrustException($"Cannot access workspace approval storage at '{StoragePath}'. Check permissions and retry. No new approval was remembered.", ex);
        }
    }

    private void Write(WorkspaceTrustData data)
    {
        try { _files.Write(StoragePath, JsonSerializer.Serialize(data, WorkspaceTrustJsonContext.Default.WorkspaceTrustData)); }
        catch (SecretStorageException ex)
        {
            throw new WorkspaceTrustException($"Workspace approvals could not be saved to '{StoragePath}'. Check directory permissions and retry. No new approval was remembered.", ex);
        }
    }
}
