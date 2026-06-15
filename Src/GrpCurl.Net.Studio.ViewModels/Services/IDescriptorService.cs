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
}
