using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Probes for a <c>protoc</c> binary (FR-154). <see cref="DetectAsync" /> reports what a PATH
///     lookup currently resolves (path + version); <see cref="VerifyAsync" /> runs <c>--version</c> on
///     an explicit override path.
/// </summary>
public interface IProtocService
{
    Task<ProtocInfo> DetectAsync(CancellationToken cancellationToken = default);

    Task<ProtocInfo> VerifyAsync(string path, CancellationToken cancellationToken = default);
}
