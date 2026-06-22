using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>One row in the environment manager: an environment with a short variable summary.</summary>
public sealed record EnvironmentRow(WorkspaceEnvironment Environment)
{
    public string Display => Environment.Name;

    private int VariableCount => Environment.Variables.Count;

    private int SecretCount => Environment.Variables.Count(v => v.IsSecret);

    public string SummaryText => VariableCount switch
    {
        0 => "no variables",
        _ => SecretCount > 0
            ? $"{VariableCount} variable(s), {SecretCount} secret"
            : $"{VariableCount} variable(s)"
    };
}

/// <summary>
///     Lists the workspace environments and supports create / edit / duplicate / delete (FR-130). Duplicating
///     copies each secret value into its own <see cref="ISecretStore" /> entry so deleting either environment
///     can't strand the other's secrets. Closes with <see langword="true" /> when anything changed so the
///     status-bar switcher can refresh its dropdown.
/// </summary>
public sealed partial class EnvironmentManagerViewModel : DialogViewModel<bool>
{
    public override string Title => "Environments";

    private readonly IEnvironmentStore _store;
    private readonly IDialogService _dialogs;
    private readonly ISecretStore _secrets;

    private bool _changed;

    public EnvironmentManagerViewModel(IEnvironmentStore store, IDialogService dialogs, ISecretStore secrets)
    {
        _store = store;
        _dialogs = dialogs;
        _secrets = secrets;

        Environments = [];
        Reload();
    }

    public ObservableCollection<EnvironmentRow> Environments { get; }

    public bool HasEnvironments => Environments.Count > 0;

    [RelayCommand]
    private async Task NewEnvironment()
    {
        var saved = await _dialogs.ShowDialogAsync(new EnvironmentEditorViewModel(_secrets));

        if (saved is not null)
        {
            await _store.SaveAsync(saved);
            MarkChangedAndReload();
        }
    }

    [RelayCommand]
    private async Task EditEnvironment(EnvironmentRow? row)
    {
        if (row is null)
        {
            return;
        }

        var saved = await _dialogs.ShowDialogAsync(new EnvironmentEditorViewModel(_secrets, row.Environment));

        if (saved is not null)
        {
            await _store.SaveAsync(saved);
            MarkChangedAndReload();
        }
    }

    [RelayCommand]
    private async Task DuplicateEnvironment(EnvironmentRow? row)
    {
        if (row is null)
        {
            return;
        }

        var source = row.Environment;
        var copy = new WorkspaceEnvironment { Id = Guid.NewGuid().ToString("N"), Name = $"{source.Name} (copy)" };

        foreach (var variable in source.Variables)
        {
            StringOrSecret value;

            if (variable.Value.SecretRef is { } sourceRef)
            {
                // Give the copy its own secret entry so deleting either environment leaves the other intact.
                var newRef = Guid.NewGuid().ToString("N");
                var secret = await _secrets.GetAsync(sourceRef);
                await _secrets.SetAsync(newRef, secret ?? string.Empty);
                value = StringOrSecret.Secret(newRef);
            }
            else
            {
                value = variable.Value; // plain values are immutable, safe to share
            }

            copy.Variables.Add(new EnvironmentVariable { Name = variable.Name, Value = value });
        }

        await _store.SaveAsync(copy);
        MarkChangedAndReload();
    }

    [RelayCommand]
    private async Task DeleteEnvironment(EnvironmentRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync(
                "Delete environment",
                $"Delete environment '{row.Environment.Name}'? This cannot be undone."))
        {
            await _store.DeleteAsync(row.Environment.Id);
            MarkChangedAndReload();
        }
    }

    [RelayCommand]
    private void Close() => Close(_changed);

    private void MarkChangedAndReload()
    {
        _changed = true;
        Reload();
    }

    private void Reload()
    {
        Environments.Clear();

        foreach (var environment in _store.Environments)
        {
            Environments.Add(new EnvironmentRow(environment));
        }

        OnPropertyChanged(nameof(HasEnvironments));
    }
}
