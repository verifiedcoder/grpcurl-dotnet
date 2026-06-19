using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>SEC-041: the inline supply dialog for secrets a workspace import can't carry.</summary>
public sealed class ImportSecretsDialogViewModelTests
{
    private static ImportSecretsDialogViewModel Create()
        => new([new MissingSecret("TLS profile 'mtls' — password", "ref-1"), new MissingSecret("Environment 'prod' — TOKEN", "ref-2")]);

    [Fact]
    public void It_lists_a_masked_row_per_missing_secret()
    {
        var vm = Create();

        vm.Rows.Count.ShouldBe(2);
        vm.Rows[0].DisplayName.ShouldContain("mtls");
        vm.Rows[0].IsRevealed.ShouldBeFalse(); // masked by default
    }

    [Fact]
    public void Apply_returns_only_the_supplied_values_keyed_by_keyref()
    {
        var vm = Create();
        vm.Rows[0].Value = "p@ss";
        // second row left blank → omitted
        IReadOnlyDictionary<string, string>? result = null;
        vm.CloseRequested += r => result = r;

        vm.ApplyCommand.Execute(null);

        _ = result.ShouldNotBeNull();
        result!.Count.ShouldBe(1);
        result["ref-1"].ShouldBe("p@ss");
    }

    [Fact]
    public void Skip_closes_with_null()
    {
        var vm = Create();
        var closed = false;
        IReadOnlyDictionary<string, string>? result = new Dictionary<string, string>();
        vm.CloseRequested += r => { closed = true; result = r; };

        vm.SkipCommand.Execute(null);

        closed.ShouldBeTrue();
        result.ShouldBeNull();
    }
}
