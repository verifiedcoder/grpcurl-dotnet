using GrpCurl.Net.Studio.ViewModels.Panes;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     The right-hand inspector as a sink: siblings (explorer, console, the streaming event log) push
///     context-sensitive detail to it without referencing the concrete view model. A small mediator,
///     mirroring <see cref="IConnectionSelection" /> / <see cref="IDocumentHost" />.
/// </summary>
public interface IInspector
{
    /// <summary>FR-020: show the selected method's signature.</summary>
    void ShowMethod(MethodSignatureContent method);

    /// <summary>FR-088: show a single streamed message routed from the event log.</summary>
    void ShowMessage(MessageContent message);

    /// <summary>FR-114: show a completed call's timing breakdown.</summary>
    void ShowCallTiming(CallTimingContent timing);

    /// <summary>Reset to the empty "select an item" state.</summary>
    void Clear();
}
