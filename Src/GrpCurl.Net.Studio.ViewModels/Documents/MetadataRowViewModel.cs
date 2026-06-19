using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     One response header/trailer row with redaction + per-field reveal (FR-112/113). Secret values
///     (per Core's <see cref="SecretRedactor" />) render as the redaction placeholder until the user
///     reveals them through the session-gated eye toggle; reveal is view-state only — it never changes
///     what is logged, exported, or copied.
/// </summary>
public sealed partial class MetadataRowViewModel : ViewModelBase
{
    private readonly IRevealGate _gate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    public partial bool IsRevealed { get; set; }

    public MetadataRowViewModel(MetadataItem item, IRevealGate gate)
    {
        Item = item;
        _gate = gate;
        IsSecret = SecretRedactor.ShouldRedact(item.Name);
    }

    public MetadataItem Item { get; }

    public string Name => Item.Name;

    public bool IsSecret { get; }

    /// <summary>The value shown: the redaction placeholder for an un-revealed secret, otherwise the raw value.</summary>
    public string DisplayValue => IsSecret && !IsRevealed
        ? SecretRedactor.FormatValue(Item.Name, Item.Value, unsafeShowSecrets: false)
        : Item.Value;

    /// <summary>The eye toggle is offered only for secret values.</summary>
    private bool CanReveal => IsSecret;

    [RelayCommand(CanExecute = nameof(CanReveal))]
    private async Task ToggleReveal()
    {
        if (IsRevealed)
        {
            IsRevealed = false; // collapse — no warning needed to hide
            return;
        }

        if (await _gate.ConfirmRevealAsync())
        {
            IsRevealed = true;
        }
    }
}
