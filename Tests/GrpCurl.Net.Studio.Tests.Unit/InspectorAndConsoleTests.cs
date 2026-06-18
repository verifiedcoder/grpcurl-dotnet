using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Panes;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>CU-3: the shared inspector surface (FR-020/088/114) and the console call log (FR-114).</summary>
public sealed class InspectorAndConsoleTests
{
    private static CallTimingContent SampleTiming() => new(
        "pkg.Svc/Go — timing", "100 ms", IsError: false,
        [new CallTimingPhase("descriptor", "30 ms", 0.3), new CallTimingPhase("call", "70 ms", 0.7)]);

    [Fact]
    public void Inspector_starts_empty()
    {
        var inspector = new InspectorViewModel();

        inspector.IsEmpty.ShouldBeTrue();
        inspector.Content.ShouldBeOfType<EmptyInspectorContent>();
    }

    [Fact]
    public void Inspector_shows_then_clears_content()
    {
        var inspector = new InspectorViewModel();

        inspector.ShowMessage(new MessageContent("Message #1", "{ }"));
        inspector.IsEmpty.ShouldBeFalse();
        inspector.Content.ShouldBeOfType<MessageContent>().Title.ShouldBe("Message #1");

        inspector.ShowCallTiming(SampleTiming());
        inspector.Content.ShouldBeOfType<CallTimingContent>();

        inspector.Clear();
        inspector.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Method_timing_phase_renders_a_percentage_but_total_does_not()
    {
        new CallTimingPhase("call", "70 ms", 0.7).PercentText.ShouldBe("70%");
        new CallTimingPhase("total", "100 ms", 1.0).PercentText.ShouldBeEmpty();
    }

    // ── FR-114: console call rows ────────────────────────────────────────────

    [Fact]
    public void Appending_a_call_records_a_row_with_an_inline_total()
    {
        var console = new ConsoleViewModel();

        console.HasActivity.ShouldBeFalse();
        console.AppendCall(new ConsoleCallActivity("pkg.Svc/Go", 0, "OK", IsError: false, "12 ms", []));

        console.HasActivity.ShouldBeTrue();
        var row = console.Calls.ShouldHaveSingleItem();
        row.Display.ShouldBe("pkg.Svc/Go · OK · 12 ms");
        row.IsError.ShouldBeFalse();
    }

    [Fact]
    public void Selecting_a_call_row_shows_its_breakdown_in_the_inspector()
    {
        var inspector = new FakeInspector();
        var console = new ConsoleViewModel(inspector);
        console.AppendCall(new ConsoleCallActivity(
            "pkg.Svc/Go", 0, "OK", IsError: false, "100 ms",
            [new CallTimingPhase("call", "70 ms", 0.7)]));

        console.SelectedCall = console.Calls[0];

        var shown = inspector.Last.ShouldBeOfType<CallTimingContent>();
        shown.TotalText.ShouldBe("100 ms");
        shown.Phases.ShouldHaveSingleItem().Phase.ShouldBe("call");
    }

    [Fact]
    public void A_plain_log_line_does_not_become_a_call_row()
    {
        var console = new ConsoleViewModel();

        console.Append("[descriptor] duplicate file a.proto");

        console.Messages.ShouldHaveSingleItem();
        console.Calls.ShouldBeEmpty();
        console.HasActivity.ShouldBeFalse();
    }

    // ── FR-004: kind/time + FR-003 unread ────────────────────────────────────

    [Fact]
    public void A_row_carries_its_kind_label_and_time()
    {
        var console = new ConsoleViewModel();
        console.AppendCall(new ConsoleCallActivity(
            "Describe: alpha", 0, "2 service(s)", IsError: false, "8 ms", [],
            ConsoleActivityKind.Descriptor, new DateTimeOffset(2026, 6, 18, 9, 30, 15, TimeSpan.Zero)));

        var row = console.Calls.ShouldHaveSingleItem();
        row.KindLabel.ShouldBe("describe");
        row.TimeText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Activity_while_collapsed_sets_the_unread_indicator_until_shown()
    {
        var console = new ConsoleViewModel();
        console.SetActive(false); // collapsed

        console.AppendCall(new ConsoleCallActivity("pkg.Svc/Go", 0, "OK", IsError: false, "5 ms", []));

        console.HasUnread.ShouldBeTrue();

        console.SetActive(true); // shown → cleared
        console.HasUnread.ShouldBeFalse();
    }

    [Fact]
    public void Activity_while_visible_does_not_set_unread()
    {
        var console = new ConsoleViewModel(); // active by default

        console.AppendCall(new ConsoleCallActivity("pkg.Svc/Go", 0, "OK", IsError: false, "5 ms", []));

        console.HasUnread.ShouldBeFalse();
    }
}
