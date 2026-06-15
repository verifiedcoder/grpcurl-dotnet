namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>The invocation tab's run states (SPEC-010 §1.2).</summary>
public enum RunState
{
    Idle,
    InFlight,
    Completed,
    Failed,
    Cancelled
}
