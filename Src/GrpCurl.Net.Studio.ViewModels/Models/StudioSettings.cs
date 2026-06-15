using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.ViewModels.Models;

/// <summary>
///     Persisted application settings (SPEC-040 §6). The schema is versioned; unknown keys
///     round-trip via <see cref="Overflow" /> so a newer build's settings survive being
///     opened by an older one. Layout/window state is deliberately NOT here — it lives in a
///     separate per-machine <c>window-state.json</c> (SPEC-040 §2), deferred past E0.2.
/// </summary>
public sealed class StudioSettings
{
    public int SchemaVersion { get; set; } = 1;

    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Unknown/forward-compatible keys, preserved on save (SPEC-040 §6).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Overflow { get; set; }

    public static StudioSettings Defaults() => new();
}

public sealed class AppearanceSettings
{
    /// <summary>One of <c>light</c>, <c>dark</c>, <c>system</c> (default).</summary>
    public string Theme { get; set; } = "system";

    public double UiScale { get; set; } = 1.0;
}
