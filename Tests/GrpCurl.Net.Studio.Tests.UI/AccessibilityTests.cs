using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Models.History;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views;
using GrpCurl.Net.Studio.Views.Documents;
using GrpCurl.Net.Studio.Views.Panes;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Accessibility guard (SPEC-020 §6 / NFR-A1, NFR-A6): walks the realized visual tree of every
///     interactive view and fails if any user-placed interactive control lacks an accessible name. The
///     effective name is read via the control's automation peer, so a name supplied through content/header
///     text (e.g. a menu item's <c>Header</c>) counts — only genuinely unnamed controls fail.
///     <para>
///         Each view is built with a populated view model so templated rows (history entries, describe
///         type-links, the inspector message box) are realized and checked too — those are exactly the
///         controls that were previously named only by their bound content, not an explicit name.
///     </para>
/// </summary>
public sealed class AccessibilityTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    public static TheoryData<string> InteractiveViews =>
    [
        "Shell",
        "ConnectionEditor",
        "ServiceExplorer",
        "Inspector",
        "Console",
        "Describe",
        "GraphQl",
        "History",
        "Settings",
    ];

    [Theory]
    [MemberData(nameof(InteractiveViews))]
    public Task Every_interactive_control_has_an_accessible_name(string view) => RunOnUiThread(() =>
    {
        var window = BuildWindow(view);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var unnamed = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsInteractive)
            // Templated sub-parts (e.g. a ComboBox's PART_EditableTextBox) carry the parent's
            // accessible name, not their own — only user-placed controls are required to be named.
            .Where(control => control.TemplatedParent is null)
            .Where(control => string.IsNullOrWhiteSpace(EffectiveName(control)))
            .Select(Describe)
            .ToList();

        unnamed.ShouldBeEmpty($"{view}: interactive controls without an accessible name: " + string.Join(", ", unnamed));
    });

    private static Window BuildWindow(string view) => view switch
    {
        "Shell" => new MainWindow { DataContext = ShellViewModel() },
        "ConnectionEditor" => Host(new Views.Connections.ConnectionEditorView { DataContext = ConnectionEditor() }),
        "ServiceExplorer" => Host(new ServiceExplorerView { DataContext = LoadedExplorer() }),
        "Inspector" => Host(new InspectorView { DataContext = MessageInspector() }),
        "Console" => Host(new ConsoleView { DataContext = ConsoleWithCall() }),
        "Describe" => Host(new DescribeDocumentView { DataContext = LoadedMessageDocument() }),
        "GraphQl" => Host(new GraphQlDocumentView { DataContext = GraphQlDocument() }),
        "History" => Host(new HistoryDocumentView { DataContext = LoadedHistory() }),
        "Settings" => Host(new SettingsDocumentView { DataContext = Settings() }),
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "unknown view key"),
    };

    private static Window Host(Control content) => new() { Content = content, Width = 900, Height = 600 };

    private static MainWindowViewModel ShellViewModel()
        => new(
            new FakeThemeService(),
            new ConnectionsPaneViewModel(new FakeWorkspaceStore(), new FakeConnectionRegistry(), new FakeDialogService(), new ConnectionSelection()),
            new ServiceExplorerViewModel(new FakeDescriptorService(), new ConnectionSelection(), new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost()),
            new ConsoleViewModel(),
            new InspectorViewModel(),
            new DocumentsViewModel(new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), new InMemorySettingsStore(), new FakeThemeService()));

    private static ConnectionEditorViewModel ConnectionEditor()
    {
        var editor = new ConnectionEditorViewModel(new FakeConnectionRegistry());
        editor.AddHeaderCommand.Execute(null); // realize a header row's controls too
        return editor;
    }

    private static ServiceExplorerViewModel LoadedExplorer()
    {
        var descriptors = new FakeDescriptorService
        {
            Result = DescriptorLoadResult.Success(new ServiceCatalog(
            [
                new ServiceEntry("pkg.Greeter",
                [
                    new ServiceMethod("SayHello", "pkg.Greeter/SayHello", StreamingShape.Unary, "pkg.Req", "pkg.Resp"),
                    new ServiceMethod("Chat", "pkg.Greeter/Chat", StreamingShape.BidiStreaming, "pkg.Msg", "pkg.Msg")
                ])
            ], []))
        };
        var selection = new ConnectionSelection();
        var vm = new ServiceExplorerViewModel(descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost());
        selection.Set(new SavedConnection { Name = "c", Address = "h:1" });
        return vm;
    }

    private static InspectorViewModel MessageInspector()
    {
        var inspector = new InspectorViewModel();
        inspector.ShowMessage(new MessageContent("Streamed message #1", "{\n  \"ok\": true\n}"));
        return inspector;
    }

    private static ConsoleViewModel ConsoleWithCall()
    {
        var console = new ConsoleViewModel(new InspectorViewModel());
        console.AppendCall(new ConsoleCallActivity(
            "pkg.Svc/Go", 0, "OK", IsError: false, "12 ms",
            [new CallTimingPhase("call", "12 ms", 1.0)]));
        return console;
    }

    private static DescribeDocumentViewModel LoadedMessageDocument()
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(DescribeResult.Success(
                symbol == "pkg.Alpha"
                    ? new MessageDescription("pkg.Alpha", "Alpha", "a.proto",
                        [new FieldDescription("beta", 1, ".pkg.Beta", new TypeRef("pkg.Beta", Resolvable: true), FieldLabel.Optional, null)],
                        [], "{\n  \"beta\": {}\n}")
                    : new MessageDescription(symbol, symbol, "b.proto", [], [], "{}")))
        };

        return new DescribeDocumentViewModel(
            new SavedConnection { Name = "c", Address = "h:1" },
            "pkg.Alpha", descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeDocumentHost());
    }

    private static GraphQlDocumentViewModel GraphQlDocument()
    {
        var vm = new GraphQlDocumentViewModel(
            new SavedConnection { Name = "c", Address = "h:1" },
            new FakeGraphQlService(),
            new ImmediateUiDispatcher(),
            new FakeClipboardService())
        {
            ParseDebounce = TimeSpan.Zero
        };
        vm.ApplyParse(new GraphQlParseResult([new GraphQlOperationInfo("Q", GraphQlOperationKind.Query)], []));
        return vm;
    }

    private static HistoryDocumentViewModel LoadedHistory()
    {
        var store = new FakeHistoryStore();
        store.Entries.Add(Entry("e1", "pkg.Svc/First"));
        store.Entries.Add(Entry("e2", "pkg.Svc/Second"));
        var vm = new HistoryDocumentViewModel(
            store, new InMemorySettingsStore(),
            new FakeWorkspaceStore(new WorkspaceModel { Connections = [new SavedConnection { Name = "staging", Address = "h:1" }] }),
            new FakeDocumentHost(), new FakeDialogService(), new ImmediateUiDispatcher(), new FakeFilePickerService());
        vm.LoadAsync().GetAwaiter().GetResult();
        return vm;

        static HistoryEntry Entry(string id, string method) => new(
            HistoryEntry.CurrentVersion, id, DateTimeOffset.UtcNow, HistoryKind.Grpc,
            new HistoryConnection("staging", "h:1", "tls", null), null, method,
            new HistoryRequest("json", "{}", false, [], "10s", false, false, null, null, null),
            new HistoryOutcome("OK", "success", 0, 12, 1, 1, null, false, null));
    }

    private static SettingsDocumentViewModel Settings()
        => new(new InMemorySettingsStore(), new FakeThemeService(), new FakeDialogService(), new FakeProtocService());

    private static bool IsInteractive(Control control) => control switch
    {
        // Templated sub-parts of a named control should not be required to carry their own
        // name; only the user-facing input controls are checked.
        Button or MenuItem or ToggleButton or CheckBox or RadioButton or TextBox or ComboBox or Slider => true,
        _ => false
    };

    private static string? EffectiveName(Control control)
        => ControlAutomationPeer.CreatePeerForElement(control)?.GetName();

    private static string Describe(Control control)
        => $"{control.GetType().Name}#{control.Name ?? "(anonymous)"}";
}
