using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Default <see cref="IEnvironmentService" /> (FR-130..134). Reads environments from the live
///     workspace, holds the active selection, and resolves <c>${VAR}</c> active-environment-first then OS.
///     Secret-typed variables are fetched from <see cref="ISecretStore" /> at use time; an unresolved
///     variable raises an error rather than expanding to empty.
/// </summary>
internal sealed partial class EnvironmentService(IWorkspaceStore workspace, ISecretStore secrets) : IEnvironmentService
{
    private string? _activeId;

    public IReadOnlyList<WorkspaceEnvironment> Environments => workspace.Current.Environments;

    public string? ActiveId => _activeId;

    public WorkspaceEnvironment? Active
        => _activeId is { } id ? Environments.FirstOrDefault(e => e.Id == id) : null;

    public event EventHandler? ActiveChanged;

    public void SetActive(string? environmentId)
    {
        if (_activeId == environmentId)
        {
            return;
        }

        _activeId = environmentId;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        if (Active?.Variables.FirstOrDefault(v => v.Name == name) is { } variable)
        {
            return variable.Value.SecretRef is { } keyRef
                ? await secrets.GetAsync(keyRef, cancellationToken).ConfigureAwait(false)
                : variable.Value.Literal ?? string.Empty;
        }

        return Environment.GetEnvironmentVariable(name);
    }

    public async Task<string> ExpandAsync(string value, CancellationToken cancellationToken = default)
    {
        var matches = PlaceholderPattern().Matches(value);

        if (matches.Count == 0)
        {
            return value;
        }

        var builder = new StringBuilder();
        var last = 0;

        foreach (Match match in matches)
        {
            _ = builder.Append(value, last, match.Index - last);

            var name = match.Groups[1].Value;
            var resolved = await ResolveAsync(name, cancellationToken).ConfigureAwait(false);

            _ = builder.Append(resolved ?? throw new InvalidOperationException(
                $"Variable '{name}' is not set in the active environment "
                + $"'{Active?.Name ?? "(none)"}' or the OS environment."));

            last = match.Index + match.Length;
        }

        _ = builder.Append(value, last, value.Length - last);
        return builder.ToString();
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderPattern();
}
