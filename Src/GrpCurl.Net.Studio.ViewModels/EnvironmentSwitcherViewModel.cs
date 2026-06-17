using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>One entry in the status-bar environment dropdown; a null <see cref="Id" /> is "No environment".</summary>
public sealed record EnvironmentOption(string? Id, string Name);

/// <summary>
///     The shell's status-bar environment switcher (FR-133). Lists the workspace environments plus a
///     first-class "No environment" entry (FR-138); selecting one makes it active immediately, so subsequent
///     sends resolve <c>${VAR}</c> against it. Active selection lives in <see cref="IEnvironmentService" />;
///     this view model only mirrors it and offers a "Manage…" entry into the environment manager.
/// </summary>
public sealed partial class EnvironmentSwitcherViewModel : ViewModelBase
{
    private static readonly EnvironmentOption NoneOption = new(null, "No environment");

    private readonly IEnvironmentService _environments;
    private readonly IEnvironmentStore _store;
    private readonly IDialogService _dialogs;
    private readonly ISecretStore _secrets;

    private bool _suppress;

    [ObservableProperty]
    private EnvironmentOption? _selectedOption;

    public EnvironmentSwitcherViewModel(
        IEnvironmentService environments, IEnvironmentStore store, IDialogService dialogs, ISecretStore secrets)
    {
        _environments = environments;
        _store = store;
        _dialogs = dialogs;
        _secrets = secrets;

        Options = [];
        Reload();

        _environments.ActiveChanged += (_, _) => SyncSelection();
    }

    public ObservableCollection<EnvironmentOption> Options { get; }

    /// <summary>FR-138: true in the "No environment" state, so the status bar can render it distinctly.</summary>
    public bool IsNoEnvironment => _environments.Active is null;

    public string DisplayText => _environments.Active?.Name ?? NoneOption.Name;

    /// <summary>Opens the environment manager, then refreshes the dropdown (the list may have changed).</summary>
    [RelayCommand]
    private async Task Manage()
    {
        await _dialogs.ShowDialogAsync(new EnvironmentManagerViewModel(_store, _dialogs, _secrets));
        Reload();
    }

    /// <summary>Rebuilds the dropdown from the live workspace (called on construction and workspace switch).</summary>
    public void Reload()
    {
        // The active environment may have been deleted out from under us → fall back to "No environment".
        if (_environments.ActiveId is { } id && _environments.Environments.All(e => e.Id != id))
        {
            _environments.SetActive(null);
        }

        _suppress = true;
        Options.Clear();
        Options.Add(NoneOption);

        foreach (var environment in _environments.Environments)
        {
            Options.Add(new EnvironmentOption(environment.Id, environment.Name));
        }

        SelectedOption = Options.FirstOrDefault(o => o.Id == _environments.ActiveId) ?? NoneOption;
        _suppress = false;

        OnPropertyChanged(nameof(IsNoEnvironment));
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnSelectedOptionChanged(EnvironmentOption? value)
    {
        if (!_suppress)
        {
            _environments.SetActive(value?.Id);
        }
    }

    private void SyncSelection()
    {
        _suppress = true;
        SelectedOption = Options.FirstOrDefault(o => o.Id == _environments.ActiveId) ?? NoneOption;
        _suppress = false;

        OnPropertyChanged(nameof(IsNoEnvironment));
        OnPropertyChanged(nameof(DisplayText));
    }
}
