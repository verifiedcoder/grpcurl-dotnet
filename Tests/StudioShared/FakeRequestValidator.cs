using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

public sealed class FakeRequestValidator : IRequestValidator
{
    public IReadOnlyList<ValidationProblem> Problems { get; set; } = [];

    public Func<string, IReadOnlyList<ValidationProblem>>? OnValidate { get; set; }

    public int ValidateCount { get; private set; }

    public Task<IReadOnlyList<ValidationProblem>> ValidateAsync(
        SavedConnection connection, string methodSymbol, string requestJson, bool allowUnknownFields, CancellationToken cancellationToken)
    {
        ValidateCount++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OnValidate?.Invoke(requestJson) ?? Problems);
    }
}
