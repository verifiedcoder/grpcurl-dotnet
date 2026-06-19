using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class StreamComposerViewModelTests
{
    private static StreamComposerViewModel Create(out FakeRequestValidator validator, out FakeFilePickerService picker, string? fileContent = null)
    {
        validator = new FakeRequestValidator();
        picker = new FakeFilePickerService();
        return new StreamComposerViewModel(
            new SavedConnection { Name = "c", Address = "h:1" }, "pkg.Svc/Go", allowUnknownFields: true,
            validator, new ImmediateUiDispatcher(), picker, _ => Task.FromResult(fileContent ?? string.Empty));
    }

    private static async Task<List<string>> Drain(IAsyncEnumerable<string> stream)
    {
        var items = new List<string>();
        await foreach (var s in stream)
        {
            items.Add(s);
        }

        return items;
    }

    [Fact]
    public void Send_is_disabled_until_the_stream_begins()
    {
        var vm = Create(out _, out _);

        vm.CanSend.ShouldBeFalse();
        vm.SendCommand.CanExecute(null).ShouldBeFalse();

        vm.Begin();

        vm.CanSend.ShouldBeTrue();
        vm.SendCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Send_enqueues_to_the_channel_and_logs_a_sent_row()
    {
        var vm = Create(out _, out _);
        var stream = vm.Begin();
        vm.MessageJson = """{ "a": 1 }""";

        vm.SendCommand.Execute(null);
        vm.MessageJson = """{ "b": 2 }""";
        vm.SendCommand.Execute(null);
        vm.CompleteSendingCommand.Execute(null);

        var sent = await Drain(stream);
        sent.ShouldBe(["""{ "a": 1 }""", """{ "b": 2 }"""]);
        vm.SentQueue.Count.ShouldBe(2);
        vm.SentQueue[0].Index.ShouldBe(0);
    }

    [Fact]
    public void Clear_after_send_blanks_the_editor_when_enabled()
    {
        var vm = Create(out _, out _);
        vm.Begin();
        vm.ClearAfterSend = true;
        vm.MessageJson = "{ \"x\": 1 }";

        vm.SendCommand.Execute(null);

        vm.MessageJson.ShouldBeEmpty();
    }

    [Fact]
    public async Task Complete_sending_closes_the_stream_and_disables_send()
    {
        var vm = Create(out _, out _);
        var stream = vm.Begin();

        vm.CompleteSendingCommand.Execute(null);

        vm.SendingComplete.ShouldBeTrue();
        vm.CanSend.ShouldBeFalse();
        vm.SendCommand.CanExecute(null).ShouldBeFalse();
        (await Drain(stream)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Load_batch_enqueues_every_message_from_an_array()
    {
        var vm = Create(out _, out var picker, fileContent: """[{ "a": 1 }, { "b": 2 }, { "c": 3 }]""");
        picker.OpenResult = "/tmp/batch.json";
        var stream = vm.Begin();

        await vm.LoadBatchCommand.ExecuteAsync(null);
        vm.CompleteSendingCommand.Execute(null);

        (await Drain(stream)).Count.ShouldBe(3);
        vm.SentQueue.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Validation_surfaces_problems_without_blocking_send()
    {
        var vm = Create(out var validator, out _);
        vm.Begin();
        validator.Problems = [new ValidationProblem("bad", 1, 1)];

        await vm.RunValidationAsync(TestContext.Current.CancellationToken);

        vm.Problems.ShouldHaveSingleItem();
        vm.HasProblems.ShouldBeTrue();
        vm.SendCommand.CanExecute(null).ShouldBeTrue(); // advisory only
    }

    [Fact]
    public async Task Validation_uses_the_current_allow_unknown_fields_value()
    {
        // P3 fix: the composer no longer captures allowUnknownFields at construction. Toggling it must
        // change the value passed to validation (previously it stayed stuck at the construction value).
        var vm = Create(out var validator, out _); // constructed with allowUnknownFields: true

        await vm.RunValidationAsync(TestContext.Current.CancellationToken);
        validator.LastAllowUnknownFields.ShouldBe(true);

        vm.AllowUnknownFields = false;
        await vm.RunValidationAsync(TestContext.Current.CancellationToken);
        validator.LastAllowUnknownFields.ShouldBe(false);
    }
}
