using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.ViewModels.Connections;

/// <summary>An editable metadata-header row (name/value); <c>-bin</c> names mark binary metadata.</summary>
public sealed partial class HeaderRowViewModel : ViewModelBase
{
    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvVarPattern();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBin))]
    [NotifyPropertyChangedFor(nameof(IsSecret))]
    [NotifyPropertyChangedFor(nameof(BinError), nameof(BinReadout), nameof(HasBinError), nameof(HasBinReadout))]
    [NotifyPropertyChangedFor(nameof(ResolvedPreview), nameof(HasResolvedPreview))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BinError), nameof(BinReadout), nameof(HasBinError), nameof(HasBinReadout))]
    [NotifyPropertyChangedFor(nameof(ResolvedPreview), nameof(HasResolvedPreview))]
    [NotifyPropertyChangedFor(nameof(ShowRequiresValue))]
    private string _value = string.Empty;

    /// <summary>FR-123: set when restored from a redacted history value — the value must be re-entered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRequiresValue))]
    private bool _requiresValue;

    public HeaderRowViewModel()
    {
    }

    public HeaderRowViewModel(HeaderEntry entry)
    {
        _name = entry.Name;
        _value = entry.Value;
    }

    /// <summary>
    ///     FR-066/FR-133: resolves a <c>${VAR}</c> against the active workspace environment for the preview
    ///     (active env first, then OS). Set by the owning invocation tab; null falls back to OS only.
    /// </summary>
    public Func<string, string?>? ActiveEnvironmentResolver { get; set; }

    /// <summary>Re-raises the resolved-value preview (e.g. after the active environment changes, FR-133).</summary>
    public void RefreshResolvedPreview()
    {
        OnPropertyChanged(nameof(ResolvedPreview));
        OnPropertyChanged(nameof(HasResolvedPreview));
    }

    public bool IsBin => Name.EndsWith("-bin", StringComparison.OrdinalIgnoreCase);

    /// <summary>FR-123: show the "value required" hint while a restored secret header is still blank.</summary>
    public bool ShowRequiresValue => RequiresValue && string.IsNullOrEmpty(Value);

    /// <summary>True for sensitive header names (per Core's <see cref="SecretRedactor" />) — masked in the UI (FR-068).</summary>
    public bool IsSecret => SecretRedactor.ShouldRedact(Name);

    /// <summary>FR-067: a <c>-bin</c> value must be valid base64; otherwise the call is blocked.</summary>
    public string? BinError =>
        IsBin && !string.IsNullOrEmpty(Value) && !TryDecodeBase64(Value, out _)
            ? "Binary (-bin) value must be valid base64."
            : null;

    public bool HasBinError => BinError is not null;

    /// <summary>FR-067: decoded byte-length readout for a valid <c>-bin</c> value.</summary>
    public string? BinReadout =>
        IsBin && BinError is null && !string.IsNullOrEmpty(Value) && TryDecodeBase64(Value, out var bytes)
            ? $"{bytes} bytes"
            : null;

    public bool HasBinReadout => BinReadout is not null;

    /// <summary>True when <see cref="ResolvedPreview" /> would differ from the raw value (env-vars or a secret).</summary>
    public bool HasResolvedPreview => !string.IsNullOrEmpty(Value) && (Value.Contains("${", StringComparison.Ordinal) || IsSecret);

    /// <summary>
    ///     FR-066: the value as it will be sent — <c>${ENV}</c> placeholders expanded (unset shown as
    ///     <c>&lt;unset:NAME&gt;</c>) and secret values redacted (FR-068). Preview only; the call still
    ///     fails at send time on a genuinely-unset variable.
    /// </summary>
    public string? ResolvedPreview
    {
        get
        {
            if (!HasResolvedPreview)
            {
                return null;
            }

            var resolved = EnvVarPattern().Replace(Value, m =>
            {
                var name = m.Groups[1].Value;
                // FR-066/FR-131: active environment first, then the OS process environment.
                return ActiveEnvironmentResolver?.Invoke(name)
                       ?? Environment.GetEnvironmentVariable(name)
                       ?? $"<unset:{name}>";
            });

            return SecretRedactor.FormatValue(Name, resolved, unsafeShowSecrets: false);
        }
    }

    public HeaderEntry ToEntry() => new() { Name = Name, Value = Value, IsBin = IsBin };

    private static bool TryDecodeBase64(string value, out int byteCount)
    {
        var buffer = new byte[value.Length];

        if (Convert.TryFromBase64String(value, buffer, out byteCount))
        {
            return true;
        }

        byteCount = 0;
        return false;
    }
}
