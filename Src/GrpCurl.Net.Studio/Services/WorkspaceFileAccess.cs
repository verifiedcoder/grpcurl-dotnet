using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>Admission and consumption checks share the same policy, including cached operations.</summary>
internal sealed class WorkspaceFileAccess(IWorkspaceStore workspace)
{
    private JsonWorkspaceStore Store => workspace as JsonWorkspaceStore
        ?? throw new WorkspaceTrustException("The workspace store does not support file authorization.");

    internal AccessLease Begin(SavedConnection connection, CancellationToken cancellationToken)
    {
        _ = Store.AuthorizeConnection(connection);
        return new AccessLease(Store.ValidateActiveAuthorization, cancellationToken);
    }

    internal DescriptorSourceOptions DescriptorOptions(SavedConnection connection, DescriptorSourceOptions? options = null)
        => (options ?? DescriptorSourceOptions.Default) with { FileAccessGuard = Guard(connection) };

    internal Action<string, bool> Guard(SavedConnection connection) => (path, directory) =>
    {
        var references = Store.AuthorizeConnection(connection);
        var absolute = WorkspacePathPortability.Resolve(path, Path.GetDirectoryName(Store.AuthorizationPath)!);
        var candidates = references.Where(r => r.AbsolutePath.Equals(absolute, WorkspaceReferenceAssessment.Comparison)
            && (!directory || r.IsDirectory) || r.IsDirectory && WorkspaceReferenceAssessment.Contains(r.AbsolutePath, absolute)).ToArray();
        foreach (var candidate in candidates)
        {
            var resolved = WorkspaceReferenceAssessment.ResolveTarget(absolute, target =>
                target.Equals(candidate.AbsolutePath, WorkspaceReferenceAssessment.Comparison)
                || target.Equals(candidate.ResolvedTarget, WorkspaceReferenceAssessment.Comparison)
                || candidate.IsDirectory && (WorkspaceReferenceAssessment.Contains(candidate.AbsolutePath, target)
                    || WorkspaceReferenceAssessment.Contains(candidate.ResolvedTarget!, target)));
            if (resolved.Resolved && (resolved.Target.Equals(candidate.ResolvedTarget, WorkspaceReferenceAssessment.Comparison)
                || candidate.IsDirectory && WorkspaceReferenceAssessment.Contains(candidate.ResolvedTarget!, resolved.Target)))
            {
                return;
            }
        }
        throw new WorkspaceTrustException($"Descriptor or TLS access to '{absolute}' needs separate approval. Add it to the workspace's file references or import directories, then save and review the references.");
    };

    internal sealed class AccessLease : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Timer _monitor;
        private readonly Lock _gate = new();
        private bool _disposed;
        internal AccessLease(Action validate, CancellationToken caller)
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _monitor = new Timer(_ =>
            {
                lock (_gate)
                {
                    if (_disposed || _cancellation.IsCancellationRequested) { return; }
                    try { validate(); }
                    catch (Exception ex) when (ex is WorkspaceTrustException or IOException or UnauthorizedAccessException)
                    {
                        _cancellation.Cancel();
                    }
                }
            }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
        }
        internal CancellationToken Token => _cancellation.Token;
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) { return; }
                _disposed = true;
                _monitor.Dispose();
                _cancellation.Dispose();
            }
        }
    }
}
