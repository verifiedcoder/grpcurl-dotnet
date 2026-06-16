using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Scriptable <see cref="IRevealGate" />: <see cref="Allow" /> drives the reveal decision.</summary>
public sealed class FakeRevealGate : IRevealGate
{
    public bool Allow { get; set; } = true;

    public int ConfirmCount { get; private set; }

    public Task<bool> ConfirmRevealAsync()
    {
        ConfirmCount++;
        return Task.FromResult(Allow);
    }
}
