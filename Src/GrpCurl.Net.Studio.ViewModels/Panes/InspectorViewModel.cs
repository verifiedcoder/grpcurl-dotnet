using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>
///     Right-hand detail inspector: a shared, context-sensitive surface for the active selection.
///     Holds a single <see cref="InspectorContent" /> the view templates by type — a method signature
///     (FR-020), a streamed message (FR-088), or a call's timing breakdown (FR-114). Siblings push to
///     it through <see cref="IInspector" /> so they stay decoupled from this view model.
/// </summary>
public sealed partial class InspectorViewModel : ViewModelBase, IInspector
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private InspectorContent _content = EmptyInspectorContent.Instance;

    public string Header => "Inspector";

    public bool IsEmpty => Content is EmptyInspectorContent;

    public void ShowMethod(MethodSignatureContent method) => Content = method;

    public void ShowMessage(MessageContent message) => Content = message;

    public void ShowCallTiming(CallTimingContent timing) => Content = timing;

    public void Clear() => Content = EmptyInspectorContent.Instance;
}
