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

        // -bin validity is computed against the resolved value, so it must refresh with the
        // active environment too (e.g. trace-bin: ${TRACE_BIN} becomes valid once TRACE_BIN is set).
        OnPropertyChanged(nameof(BinError));
        OnPropertyChanged(nameof(BinReadout));
        OnPropertyChanged(nameof(HasBinError));
        OnPropertyChanged(nameof(HasBinReadout));
    }

    public bool IsBin => Name.EndsWith("-bin", StringComparison.OrdinalIgnoreCase);

    /// <summary>FR-123: show the "value required" hint while a restored secret header is still blank.</summary>
    public bool ShowRequiresValue => RequiresValue && string.IsNullOrEmpty(Value);

    /// <summary>True for sensitive header names (per Core's <see cref="SecretRedactor" />) — masked in the UI (FR-068).</summary>
    public bool IsSecret => SecretRedactor.ShouldRedact(Name);

    /// <summary>
    ///     FR-067: a <c>-bin</c> value must be valid base64; otherwise the call is blocked. Core expands
    ///     <c>${VAR}</c> first and then base64-decodes, so validation runs against the <em>resolved</em>
    ///     value — a header like <c>trace-bin: ${TRACE_BIN}</c> is valid when <c>TRACE_BIN</c> holds valid
    ///     base64, and a value that still references an unset variable can't be judged yet (deferred to
    ///     send time) rather than wrongly rejected.
    /// </summary>
    public string? BinError
    {
        get
        {
            if (!IsBin || string.IsNullOrEmpty(Value))
            {
                return null;
            }

            var (resolved, fullyResolved) = ResolveForValidation();

            if (!fullyResolved)
            {
                return null;
            }

            return TryDecodeBase64(resolved, out _) ? null : "Binary (-bin) value must be valid base64.";
        }
    }

    public bool HasBinError => BinError is not null;

    /// <summary>FR-067: decoded byte-length readout for a valid <c>-bin</c> value (against the resolved value).</summary>
    public string? BinReadout
    {
        get
        {
            if (!IsBin || HasBinError || string.IsNullOrEmpty(Value))
            {
                return null;
            }

            var (resolved, fullyResolved) = ResolveForValidation();

            return fullyResolved && TryDecodeBase64(resolved, out var bytes) ? $"{bytes} bytes" : null;
        }
    }

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

    /// <summary>
    ///     Expands <c>${VAR}</c> the way Core will (active environment first, then OS), reporting whether
    ///     every referenced variable resolved. Used to validate <c>-bin</c> against the value actually sent,
    ///     not the literal editor text. Unlike <see cref="ResolvedPreview" /> this does not redact — the
    ///     result is consumed only by base64 validation, never displayed.
    /// </summary>
    private (string Resolved, bool FullyResolved) ResolveForValidation()
    {
        var fullyResolved = true;

        var resolved = EnvVarPattern().Replace(Value, m =>
        {
            var name = m.Groups[1].Value;
            var value = ActiveEnvironmentResolver?.Invoke(name) ?? Environment.GetEnvironmentVariable(name);

            if (value is null)
            {
                fullyResolved = false;

                return string.Empty;
            }

            return value;
        });

        return (resolved, fullyResolved);
    }

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
