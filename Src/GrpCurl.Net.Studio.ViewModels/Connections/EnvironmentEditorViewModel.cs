using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     Creates or edits a workspace environment (FR-130): a name and an ordered list of variables. Hosted
///     in a modal dialog; closes with the saved <see cref="WorkspaceEnvironment" /> or <see langword="null" />
///     on cancel. Secret-typed variable values are written to <see cref="ISecretStore" /> on save and only
///     their keyref is carried in the returned model (FR-132). Variables removed or flipped from secret to
///     plain have their orphaned secrets purged, so the store never accumulates stranded values.
/// </summary>
public sealed partial class EnvironmentEditorViewModel : DialogViewModel<WorkspaceEnvironment>
{
    private readonly ISecretStore _secrets;
    private readonly string _id;
    private readonly IReadOnlyList<string> _originalSecretRefs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Name { get; set; } = string.Empty;

    public EnvironmentEditorViewModel(ISecretStore secrets, WorkspaceEnvironment? existing = null)
    {
        _secrets = secrets;
        IsEdit = existing is not null;

        var env = existing ?? new WorkspaceEnvironment { Id = Guid.NewGuid().ToString("N") };
        _id = env.Id;
        Name = env.Name;
        _originalSecretRefs = env.Variables
            .Select(v => v.Value.SecretRef)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .ToList();

        Variables = new ObservableCollection<EnvironmentVariableRowViewModel>(
            env.Variables.Select(v => new EnvironmentVariableRowViewModel(v)));
        Variables.CollectionChanged += OnVariablesChanged;

        foreach (var row in Variables)
        {
            row.PropertyChanged += OnVariableRowChanged;
        }
    }

    public bool IsEdit { get; }

    public string Title => IsEdit ? "Edit environment" : "New environment";

    public ObservableCollection<EnvironmentVariableRowViewModel> Variables { get; }

    public bool HasVariables => Variables.Count > 0;

    public string? NameError => string.IsNullOrWhiteSpace(Name) ? "Name is required." : null;

    /// <summary>Variable names are case-sensitive identifiers and must be unique within the environment.</summary>
    public string? VariableError
    {
        get
        {
            var names = Variables.Select(v => v.Name.Trim()).Where(n => n.Length > 0).ToList();
            return names.Count != names.Distinct(StringComparer.Ordinal).Count()
                ? "Variable names must be unique."
                : null;
        }
    }

    private bool CanSave => NameError is null && VariableError is null;

    [RelayCommand]
    private void AddVariable() => Variables.Add(new EnvironmentVariableRowViewModel());

    [RelayCommand]
    private void RemoveVariable(EnvironmentVariableRowViewModel? row)
    {
        if (row is not null)
        {
            _ = Variables.Remove(row);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        var result = new WorkspaceEnvironment { Id = _id, Name = Name.Trim() };
        var usedRefs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in Variables)
        {
            var name = row.Name.Trim();

            if (name.Length == 0)
            {
                continue; // skip incomplete rows rather than persist a nameless variable
            }

            StringOrSecret value;

            if (row.IsSecret)
            {
                var keyRef = await ResolveSecretRefAsync(row).ConfigureAwait(false);
                value = StringOrSecret.Secret(keyRef);
                _ = usedRefs.Add(keyRef);
            }
            else
            {
                value = StringOrSecret.Plain(row.Value);
            }

            result.Variables.Add(new EnvironmentVariable { Name = name, Value = value });
        }

        // Purge secrets this edit orphaned (rows removed, or flipped secret → plain).
        foreach (var orphan in _originalSecretRefs.Where(r => !usedRefs.Contains(r)))
        {
            await _secrets.DeleteAsync(orphan).ConfigureAwait(false);
        }

        Close(result);
    }

    [RelayCommand]
    private void Cancel() => Close(null);

    /// <summary>
    ///     Returns the keyref for a secret row: a newly typed value overwrites the existing reference (or
    ///     mints one); a blank value leaves an existing secret unchanged. A brand-new secret with no value
    ///     stores an empty string so its reference is real (mirrors the TLS editor's password handling).
    /// </summary>
    private async Task<string> ResolveSecretRefAsync(EnvironmentVariableRowViewModel row)
    {
        if (string.IsNullOrEmpty(row.Value))
        {
            if (row.OriginalSecretRef is { } existing)
            {
                return existing;
            }

            var freshRef = Guid.NewGuid().ToString("N");
            await _secrets.SetAsync(freshRef, string.Empty).ConfigureAwait(false);
            return freshRef;
        }

        var keyRef = row.OriginalSecretRef ?? Guid.NewGuid().ToString("N");
        await _secrets.SetAsync(keyRef, row.Value).ConfigureAwait(false);
        return keyRef;
    }

    private void OnVariablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.OldItems?.OfType<EnvironmentVariableRowViewModel>() ?? [])
        {
            row.PropertyChanged -= OnVariableRowChanged;
        }

        foreach (var row in e.NewItems?.OfType<EnvironmentVariableRowViewModel>() ?? [])
        {
            row.PropertyChanged += OnVariableRowChanged;
        }

        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(VariableError));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnVariableRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EnvironmentVariableRowViewModel.Name))
        {
            OnPropertyChanged(nameof(VariableError));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
}
