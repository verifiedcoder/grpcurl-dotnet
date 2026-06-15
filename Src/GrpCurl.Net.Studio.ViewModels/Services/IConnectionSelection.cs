using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     The "currently selected connection" the connections pane publishes and the service explorer
///     (and, from E1.4, invocation tabs) observe. A small mediator so sibling panes don't reference
///     one another directly.
/// </summary>
public interface IConnectionSelection
{
    /// <summary>The selected connection, or <see langword="null" /> when none is selected.</summary>
    SavedConnection? Current { get; }

    /// <summary>Raised when <see cref="Current" /> changes.</summary>
    event EventHandler? CurrentChanged;

    /// <summary>Publishes the new selection; a no-op if it is the same instance.</summary>
    void Set(SavedConnection? connection);
}
