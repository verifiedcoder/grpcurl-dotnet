using GrpCurl.Net.Studio.ViewModels.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for header-row <c>-bin</c> base64 validation + byte readout (FR-067) and the
///     env-var resolved-value preview with redaction (FR-066).
/// </summary>
public sealed class HeaderRowViewModelTests
{
    [Fact]
    public void A_valid_bin_value_reports_its_byte_count_and_no_error()
    {
        var row = new HeaderRowViewModel { Name = "trace-bin", Value = "AAEC" }; // 3 bytes

        row.IsBin.ShouldBeTrue();
        row.HasBinError.ShouldBeFalse();
        row.BinReadout.ShouldBe("3 bytes");
    }

    [Fact]
    public void An_invalid_bin_value_surfaces_an_error()
    {
        var row = new HeaderRowViewModel { Name = "trace-bin", Value = "not base64!!" };

        row.HasBinError.ShouldBeTrue();
        row.BinError.ShouldNotBeNull();
        row.HasBinReadout.ShouldBeFalse();
    }

    [Fact]
    public void A_non_bin_header_is_never_a_bin_error()
    {
        var row = new HeaderRowViewModel { Name = "x-trace", Value = "anything goes" };

        row.IsBin.ShouldBeFalse();
        row.HasBinError.ShouldBeFalse();
        row.HasBinReadout.ShouldBeFalse();
    }

    [Fact]
    public void Resolved_preview_expands_env_vars()
    {
        Environment.SetEnvironmentVariable("CU1_TEST_TOKEN", "resolved-value");
        try
        {
            var row = new HeaderRowViewModel { Name = "x-region", Value = "Bearer ${CU1_TEST_TOKEN}" };

            row.HasResolvedPreview.ShouldBeTrue();
            row.ResolvedPreview.ShouldBe("Bearer resolved-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CU1_TEST_TOKEN", null);
        }
    }

    [Fact]
    public void Resolved_preview_marks_an_unset_variable()
    {
        var row = new HeaderRowViewModel { Name = "x-region", Value = "${CU1_NO_SUCH_VAR}" };

        row.ResolvedPreview.ShouldBe("<unset:CU1_NO_SUCH_VAR>");
    }

    [Fact]
    public void Resolved_preview_redacts_a_secret_value()
    {
        var row = new HeaderRowViewModel { Name = "authorization", Value = "Bearer hunter2" };

        row.HasResolvedPreview.ShouldBeTrue(); // secret → preview offered
        row.ResolvedPreview!.ShouldNotContain("hunter2");
    }

    [Fact]
    public void A_plain_non_secret_value_has_no_preview()
    {
        var row = new HeaderRowViewModel { Name = "x-trace", Value = "abc" };

        row.HasResolvedPreview.ShouldBeFalse();
        row.ResolvedPreview.ShouldBeNull();
    }
}
