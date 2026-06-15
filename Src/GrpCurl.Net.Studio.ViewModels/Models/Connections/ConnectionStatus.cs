namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>Live connection status shown by the sidebar dot (FR-019).</summary>
public enum ConnectionStatus
{
    /// <summary>Never probed, or the channel is idle.</summary>
    Unknown,

    /// <summary>A probe or call is in progress.</summary>
    Connecting,

    /// <summary>The last probe or call on the current channel succeeded.</summary>
    Connected,

    /// <summary>The last operation failed; the error is available in the tooltip/inspector.</summary>
    Error
}

/// <summary>
///     Outcome of a test-connection probe (FR-018). For reflection sources, <see cref="ServiceCount" />
///     is populated on success; for file-based sources it is null (handshake-only probe).
/// </summary>
public sealed record TestConnectionResult(bool Ok, int? ServiceCount, string Message)
{
    public static TestConnectionResult Success(int serviceCount)
        => new(true, serviceCount, serviceCount == 1
            ? "Connected — 1 service via reflection."
            : $"Connected — {serviceCount} services via reflection.");

    public static TestConnectionResult Failure(string message) => new(false, null, message);
}
