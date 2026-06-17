using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>
///     One editable row in the environment editor: a variable name, a plain/secret toggle, and its value
///     (FR-130). A secret value is masked with per-field reveal (FR-132/FR-113); like the TLS editor's
///     PKCS12 password, an existing secret is never fetched back into the field — a blank value leaves the
///     stored secret unchanged, and the editor only writes <see cref="ISecretStore" /> when a new value is
///     typed. <see cref="OriginalSecretRef" /> carries the loaded keyref so the editor can reuse or purge it.
/// </summary>
public sealed partial class EnvironmentVariableRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueWatermark))]
    private bool _isSecret;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueWatermark))]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isRevealed;

    /// <summary>Creates a blank plain row (the "Add variable" affordance).</summary>
    public EnvironmentVariableRowViewModel()
    {
    }

    /// <summary>Loads an existing variable; a secret's value stays in the store, so the field starts blank.</summary>
    public EnvironmentVariableRowViewModel(EnvironmentVariable variable)
    {
        _name = variable.Name;
        _isSecret = variable.IsSecret;
        OriginalSecretRef = variable.Value.SecretRef;
        HadStoredSecret = variable.IsSecret;

        // Plain values are shown for editing; a secret value is never read back (it may not even be
        // retrievable on this machine), so the field is left empty with an "unchanged" watermark.
        if (!variable.IsSecret)
        {
            _value = variable.Value.Literal ?? string.Empty;
        }
    }

    /// <summary>The secret keyref this row loaded with, or null for a new/plain row.</summary>
    public string? OriginalSecretRef { get; }

    /// <summary>Whether this row began as a secret with a value already stored (drives the watermark).</summary>
    public bool HadStoredSecret { get; }

    public string ValueWatermark => IsSecret
        ? HadStoredSecret && string.IsNullOrEmpty(Value) ? "•••••• (unchanged)" : "secret value"
        : "value or ${VAR}";
}
