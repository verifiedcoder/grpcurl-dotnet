namespace GrpCurl.Net.Studio.ViewModels.Explorer;

/// <summary>The service explorer's mutually-exclusive display states (FR-025/026).</summary>
public enum ExplorerState
{
    /// <summary>No connection is selected.</summary>
    NoConnection,

    /// <summary>A descriptor load is in progress (cancellable).</summary>
    Loading,

    /// <summary>Loaded with one or more services.</summary>
    Loaded,

    /// <summary>Loaded, but the source exposes no services.</summary>
    Empty,

    /// <summary>The load failed.</summary>
    Error
}
