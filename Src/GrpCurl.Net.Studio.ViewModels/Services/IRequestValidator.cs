using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Advisory request validation (FR-063): probes a JSON request body against the method's input
///     descriptor and reports problems. Purely advisory — never blocks Invoke; the server/Core stays
///     the authority. Returns an empty list when the schema cannot be resolved.
/// </summary>
public interface IRequestValidator
{
    Task<IReadOnlyList<ValidationProblem>> ValidateAsync(
        SavedConnection connection,
        string methodSymbol,
        string requestJson,
        bool allowUnknownFields,
        CancellationToken cancellationToken);
}
