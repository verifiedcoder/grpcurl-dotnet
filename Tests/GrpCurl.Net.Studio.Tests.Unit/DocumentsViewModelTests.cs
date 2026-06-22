using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class DocumentsViewModelTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    private static DocumentsViewModel Create(InMemorySettingsStore? settings = null)
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(
                DescribeResult.Success(new MessageDescription(symbol, symbol, "f.proto", [], [], "{}")))
        };

        return new DocumentsViewModel(descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(), new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), settings ?? new InMemorySettingsStore(), new FakeThemeService());
    }

    [Fact]
    public void Open_invocation_seeds_network_defaults_and_dialect()
    {
        var settings = new InMemorySettingsStore();
        settings.Current.Network.DefaultDeadline = "30s";
        settings.Current.Network.MaxMessageSize = "8MB";
        settings.Current.General.CliShellDialect = ShellDialect.PowerShell;
        var docs = Create(settings);

        docs.OpenInvocation(Conn(), "pkg.Svc/Go");

        var tab = docs.Documents.OfType<InvocationDocumentViewModel>().ShouldHaveSingleItem();
        tab.Deadline.ShouldBe("30s");
        tab.MaxMessageSize.ShouldBe("8MB");
        tab.CliDialect.ShouldBe(ShellDialect.PowerShell);
    }

    [Fact]
    public void Open_saved_request_prefills_the_tab_from_the_saved_state()
    {
        var docs = Create();
        var request = new SavedRequest
        {
            Id = "r1", Name = "say hello", ConnectionId = "c", Method = "pkg.Svc/Hello",
            BodyFormat = GrpCurl.Net.Studio.ViewModels.Models.Invocation.RequestBodyFormat.Text,
            Body = "{ \"name\": \"world\" }",
            Headers = [new HeaderEntry { Name = "authorization", Value = "${TOKEN}" }],
            Deadline = "15s",
            EmitDefaults = true,
            AllowUnknownFields = false,
            MaxReceiveBytes = 4096
        };

        docs.OpenSavedRequest(Conn(), request);

        var tab = docs.Documents.OfType<InvocationDocumentViewModel>().ShouldHaveSingleItem();
        tab.Title.ShouldBe("say hello");                 // titled with the request name
        tab.BodyFormat.ShouldBe(GrpCurl.Net.Studio.ViewModels.Models.Invocation.RequestBodyFormat.Text);
        tab.Deadline.ShouldBe("15s");
        tab.EmitDefaults.ShouldBeTrue();
        tab.AllowUnknownFields.ShouldBeFalse();
        tab.MaxMessageSize.ShouldBe("4096");
        var header = tab.Headers.ShouldHaveSingleItem();
        header.Name.ShouldBe("authorization");
        header.Value.ShouldBe("${TOKEN}");
    }

    [Fact]
    public void Open_invocation_with_a_prefill_applies_headers_and_options_without_binding()
    {
        var docs = Create();
        var prefill = new GrpCurl.Net.Studio.ViewModels.Models.Invocation.RequestPrefill(
            "{}", GrpCurl.Net.Studio.ViewModels.Models.Invocation.RequestBodyFormat.Text,
            [
                new GrpCurl.Net.Studio.ViewModels.Models.Invocation.PrefillHeader("x-trace", "abc", IsBin: false),
                new GrpCurl.Net.Studio.ViewModels.Models.Invocation.PrefillHeader("authorization", "", IsBin: false, RequiresValue: true)
            ],
            Deadline: "30s", EmitDefaults: true, AllowUnknownFields: false, MaxMessageSize: "4096");

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", prefill);

        var tab = docs.Documents.OfType<InvocationDocumentViewModel>().ShouldHaveSingleItem();
        tab.BodyFormat.ShouldBe(GrpCurl.Net.Studio.ViewModels.Models.Invocation.RequestBodyFormat.Text);
        tab.Deadline.ShouldBe("30s");
        tab.EmitDefaults.ShouldBeTrue();
        tab.AllowUnknownFields.ShouldBeFalse();
        tab.MaxMessageSize.ShouldBe("4096");
        tab.Headers.Select(h => h.Name).ShouldBe(["x-trace", "authorization"]);
        tab.Headers[1].ShowRequiresValue.ShouldBeTrue(); // FR-123: blank restored secret prompts for a value
        tab.IsSavedRequestDirty.ShouldBeFalse();          // a replay is a plain draft, not bound
    }

    [Fact]
    public void Open_settings_adds_a_single_settings_tab()
    {
        var docs = Create();

        docs.OpenSettings();
        docs.OpenSettings();

        _ = docs.Documents.OfType<SettingsDocumentViewModel>().ShouldHaveSingleItem();
        _ = docs.SelectedDocument.ShouldBeOfType<SettingsDocumentViewModel>();
    }

    [Fact]
    public void Open_describe_adds_a_tab_and_selects_it()
    {
        var docs = Create();

        docs.OpenDescribe(Conn(), "pkg.Alpha");

        _ = docs.Documents.ShouldHaveSingleItem();
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);
        ((DescribeDocumentViewModel)docs.Documents[0]).CurrentSymbol.ShouldBe("pkg.Alpha");
    }

    [Fact]
    public void Opening_the_same_symbol_selects_the_existing_tab()
    {
        var docs = Create();
        var connection = Conn();

        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Alpha");

        _ = docs.Documents.ShouldHaveSingleItem();
    }

    [Fact]
    public void Opening_with_new_tab_creates_a_duplicate()
    {
        var docs = Create();
        var connection = Conn();

        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Alpha", newTab: true);

        docs.Documents.Count.ShouldBe(2);
    }

    [Fact]
    public void Closing_a_tab_removes_it_and_selects_a_neighbour()
    {
        var docs = Create();
        var connection = Conn();
        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Beta");
        var beta = docs.SelectedDocument!;

        beta.CloseCommand.Execute(null);

        _ = docs.Documents.ShouldHaveSingleItem();
        ((DescribeDocumentViewModel)docs.Documents[0]).CurrentSymbol.ShouldBe("pkg.Alpha");
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);
    }

    [Fact]
    public void Open_invocation_adds_a_new_invocation_tab()
    {
        var docs = Create();

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        _ = docs.Documents.ShouldHaveSingleItem().ShouldBeOfType<InvocationDocumentViewModel>();
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);
    }

    [Fact]
    public void Closing_the_last_tab_clears_the_selection()
    {
        var docs = Create();
        docs.OpenDescribe(Conn(), "pkg.Alpha");

        docs.SelectedDocument!.CloseCommand.Execute(null);

        docs.Documents.ShouldBeEmpty();
        docs.SelectedDocument.ShouldBeNull();
    }

    // ── SPEC-020 §5: shell keyboard routing (Ctrl+Tab / Ctrl+W / Ctrl+Enter / Ctrl+.) ──

    [Fact]
    public void Select_next_and_previous_cycle_through_tabs_and_wrap()
    {
        var docs = Create();
        var connection = Conn();
        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Beta");
        docs.OpenDescribe(connection, "pkg.Gamma");
        docs.SelectedDocument.ShouldBe(docs.Documents[2]); // newest tab is active

        docs.SelectNextDocumentCommand.Execute(null);       // wraps past the end → first
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);

        docs.SelectNextDocumentCommand.Execute(null);
        docs.SelectedDocument.ShouldBe(docs.Documents[1]);

        docs.SelectPreviousDocumentCommand.Execute(null);
        docs.SelectedDocument.ShouldBe(docs.Documents[0]);

        docs.SelectPreviousDocumentCommand.Execute(null);   // wraps before the start → last
        docs.SelectedDocument.ShouldBe(docs.Documents[2]);
    }

    [Fact]
    public void Cycling_with_no_tabs_open_is_a_safe_no_op()
    {
        var docs = Create();

        Should.NotThrow(() => docs.SelectNextDocumentCommand.Execute(null));
        Should.NotThrow(() => docs.SelectPreviousDocumentCommand.Execute(null));
        docs.SelectedDocument.ShouldBeNull();
    }

    [Fact]
    public void Close_active_document_closes_the_selected_tab()
    {
        var docs = Create();
        var connection = Conn();
        docs.OpenDescribe(connection, "pkg.Alpha");
        docs.OpenDescribe(connection, "pkg.Beta"); // Beta is active

        docs.CloseActiveDocumentCommand.Execute(null);

        _ = docs.Documents.ShouldHaveSingleItem();
        ((DescribeDocumentViewModel)docs.Documents[0]).CurrentSymbol.ShouldBe("pkg.Alpha");
    }

    [Fact]
    public void Run_and_cancel_active_document_are_safe_no_ops_for_non_runnable_or_idle_tabs()
    {
        var docs = Create();
        docs.OpenSettings(); // a non-runnable tab is selected

        Should.NotThrow(() => docs.RunActiveDocumentCommand.Execute(null));
        Should.NotThrow(() => docs.CancelActiveDocumentCommand.Execute(null));

        // An idle invocation tab: cancel with nothing in flight must not throw or change the tab set.
        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");
        Should.NotThrow(() => docs.CancelActiveDocumentCommand.Execute(null));
        _ = docs.Documents.OfType<InvocationDocumentViewModel>().ShouldHaveSingleItem();
    }
}
