using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Records the content pushed to the inspector so siblings' routing can be asserted (FR-020/088/114).</summary>
public sealed class FakeInspector : IInspector
{
    public List<InspectorContent> Shown { get; } = [];

    public InspectorContent? Last => Shown.Count == 0 ? null : Shown[^1];

    public int ClearCount { get; private set; }

    public void ShowMethod(MethodSignatureContent method) => Shown.Add(method);

    public void ShowMessage(MessageContent message) => Shown.Add(message);

    public void ShowCallTiming(CallTimingContent timing) => Shown.Add(timing);

    public void Clear() => ClearCount++;
}
