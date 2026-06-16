using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Loads the service/method catalog for a connection through Core's descriptor sources
///     (server reflection in Phase 1; protoset/proto arrive with E2.3). Returns plain data —
///     warnings are collected rather than written to stdio — so the explorer stays headless-
///     testable. Cancellable; failures map to an explorer-friendly <see cref="DescriptorLoadError" />.
/// </summary>
public interface IDescriptorService
{
    /// <summary>
    ///     Builds the catalog using the connection's full channel configuration. User cancellation
    ///     surfaces as <see cref="OperationCanceledException" />; transport/RPC failures surface as
    ///     a failed <see cref="DescriptorLoadResult" />.
    /// </summary>
    Task<DescriptorLoadResult> LoadAsync(SavedConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Describes a single symbol (service/method/message/enum) by fully-qualified name (FR-050),
    ///     including the request-template JSON for messages and methods (FR-052). Same cancellation/
    ///     error contract as <see cref="LoadAsync" />.
    /// </summary>
    Task<DescribeResult> DescribeAsync(SavedConnection connection, string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Exports the connection's active descriptor set to a <c>.protoset</c> file (FR-100, CLI
    ///     <c>--protoset-out</c> parity). Refuses to overwrite an existing file unless
    ///     <paramref name="overwrite" /> is set, returning a <see cref="SchemaExportOutcome.Conflict" />
    ///     so the caller can confirm and retry.
    /// </summary>
    Task<SchemaExportResult> ExportProtosetAsync(SavedConnection connection, string path, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reconstructs <c>.proto</c> source files from the connection's active descriptor set into a
    ///     directory (FR-102, CLI <c>--proto-out-dir</c> parity). Returns a
    ///     <see cref="SchemaExportOutcome.Conflict" /> listing every target that already exists unless
    ///     <paramref name="overwrite" /> is set.
    /// </summary>
    Task<SchemaExportResult> ExportProtosAsync(SavedConnection connection, string directory, bool overwrite, CancellationToken cancellationToken = default);
}
