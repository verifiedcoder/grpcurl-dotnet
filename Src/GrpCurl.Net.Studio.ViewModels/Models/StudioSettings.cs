using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.ViewModels.Models;

/// <summary>
///     Persisted application settings (SPEC-040 §6, FR-150..159). The schema is versioned; unknown
///     keys round-trip via <see cref="Overflow" /> so a newer build's settings survive being opened
///     by an older one. Layout/window state is deliberately NOT here — it lives in a separate
///     per-machine <c>window-state.json</c> (SPEC-040 §2), deferred past E0.2.
/// </summary>
public sealed class StudioSettings
{
    public int SchemaVersion { get; set; } = 1;

    public AppearanceSettings Appearance { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    public EditorSettings Editor { get; set; } = new();

    public NetworkSettings Network { get; set; } = new();

    public ProtocSettings Protoc { get; set; } = new();

    /// <summary>Unknown/forward-compatible keys, preserved on save (SPEC-040 §6).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Overflow { get; set; }

    public static StudioSettings Defaults() => new();
}

/// <summary>Theme + scale (FR-151 theme; the live-switch source of truth is <c>IThemeService</c>).</summary>
public sealed class AppearanceSettings
{
    /// <summary>One of <c>light</c>, <c>dark</c>, <c>system</c> (default).</summary>
    public string Theme { get; set; } = "system";

    public double UiScale { get; set; } = 1.0;
}

/// <summary>FR-151 General: startup behaviour + the default shell dialect for CLI export (FR-163).</summary>
public sealed class GeneralSettings
{
    public StartupBehavior Startup { get; set; } = StartupBehavior.RestoreLastWorkspace;

    public ShellDialect CliShellDialect { get; set; } = ShellDialect.Bash;
}

/// <summary>FR-146 / FR-151 startup behaviour.</summary>
public enum StartupBehavior
{
    RestoreLastWorkspace,
    StartEmpty
}

/// <summary>FR-163 shell dialect for the "Copy as CLI" export.</summary>
public enum ShellDialect
{
    Bash,
    PowerShell,
    Cmd
}

/// <summary>FR-152 Editor: applies to all AvaloniaEdit instances + pretty-print indentation.</summary>
public sealed class EditorSettings
{
    public string FontFamily { get; set; } = "Cascadia Code,Consolas,monospace";

    public double FontSize { get; set; } = 13;

    public int IndentWidth { get; set; } = 2;

    public bool FormatOnPaste { get; set; } = true;
}

/// <summary>
///     FR-153 Network defaults: applied to <em>new</em> connections/tabs as initial values; never
///     retroactively mutating existing ones. Durations use the CLI duration grammar; empty = client
///     default. Default deadline empty = none (honouring the CLI's no-default semantics).
/// </summary>
public sealed class NetworkSettings
{
    public string ConnectTimeout { get; set; } = "10s";

    public string KeepaliveTime { get; set; } = "60s";

    public string KeepaliveTimeout { get; set; } = "30s";

    public string MaxMessageSize { get; set; } = string.Empty;

    public string DefaultDeadline { get; set; } = string.Empty;

    public int BodyImportCapBytes { get; set; } = 16 * 1024 * 1024;

    public int RingBufferSize { get; set; } = 10_000;
}

/// <summary>FR-154 protoc path override: explicit path used instead of PATH lookup (empty = use PATH).</summary>
public sealed class ProtocSettings
{
    public string Path { get; set; } = string.Empty;
}
