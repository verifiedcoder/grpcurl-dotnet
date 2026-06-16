using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for response-metadata redaction + per-field reveal (FR-112/113) and the session
///     reveal gate's one-time warning.
/// </summary>
public sealed class MetadataRowAndRevealTests
{
    private static MetadataRowViewModel Row(string name, string value, IRevealGate gate)
        => new(new MetadataItem(name, value, IsBinary: false), gate);

    [Fact]
    public void A_secret_header_is_redacted_by_default()
    {
        var row = Row("authorization", "Bearer secret-token", new FakeRevealGate());

        row.IsSecret.ShouldBeTrue();
        row.IsRevealed.ShouldBeFalse();
        row.DisplayValue.ShouldNotContain("secret-token");
    }

    [Fact]
    public void A_non_secret_header_shows_its_value_and_offers_no_reveal()
    {
        var row = Row("content-type", "application/grpc", new FakeRevealGate());

        row.IsSecret.ShouldBeFalse();
        row.DisplayValue.ShouldBe("application/grpc");
        row.ToggleRevealCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Revealing_a_secret_shows_the_value_once_the_gate_allows()
    {
        var gate = new FakeRevealGate { Allow = true };
        var row = Row("authorization", "Bearer secret-token", gate);

        await row.ToggleRevealCommand.ExecuteAsync(null);

        gate.ConfirmCount.ShouldBe(1);
        row.IsRevealed.ShouldBeTrue();
        row.DisplayValue.ShouldBe("Bearer secret-token");

        // Toggling again hides it without re-prompting.
        await row.ToggleRevealCommand.ExecuteAsync(null);
        row.IsRevealed.ShouldBeFalse();
        gate.ConfirmCount.ShouldBe(1);
    }

    [Fact]
    public async Task Declining_the_gate_keeps_the_value_redacted()
    {
        var row = Row("authorization", "Bearer secret-token", new FakeRevealGate { Allow = false });

        await row.ToggleRevealCommand.ExecuteAsync(null);

        row.IsRevealed.ShouldBeFalse();
        row.DisplayValue.ShouldNotContain("secret-token");
    }

    [Fact]
    public async Task The_reveal_gate_warns_only_once_per_session()
    {
        var dialog = new FakeDialogService { ConfirmResult = true };
        var gate = new RevealGate(dialog);

        (await gate.ConfirmRevealAsync()).ShouldBeTrue();
        (await gate.ConfirmRevealAsync()).ShouldBeTrue();

        dialog.ConfirmCount.ShouldBe(1); // acknowledged once, then silent
    }

    [Fact]
    public async Task Declining_the_warning_does_not_acknowledge()
    {
        var dialog = new FakeDialogService { ConfirmResult = false };
        var gate = new RevealGate(dialog);

        (await gate.ConfirmRevealAsync()).ShouldBeFalse();
        (await gate.ConfirmRevealAsync()).ShouldBeFalse();

        dialog.ConfirmCount.ShouldBe(2); // still warns since never acknowledged
    }
}
