namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Gates the first per-field secret reveal in a session (FR-113). The first reveal shows a warning
///     ("the value will be visible on screen — beware screen sharing"); acknowledging it suppresses the
///     warning for the rest of the app run ("don't warn again this session"). Reveal is purely a
///     view-state toggle — it never affects what is logged, exported, or stored.
/// </summary>
public interface IRevealGate
{
    /// <summary>Returns true when the field may be revealed (after the one-time session warning is acknowledged).</summary>
    Task<bool> ConfirmRevealAsync();
}

/// <summary>A gate that reveals without warning — for headless tests and bare view-model construction.</summary>
public sealed class AlwaysRevealGate : IRevealGate
{
    public static AlwaysRevealGate Instance { get; } = new();

    public Task<bool> ConfirmRevealAsync() => Task.FromResult(true);
}
