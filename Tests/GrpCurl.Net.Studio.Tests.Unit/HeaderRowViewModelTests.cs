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
    public void A_bin_value_referencing_a_resolved_env_var_is_valid()
    {
        // Core expands ${VAR} then base64-decodes; the UI must validate the resolved value, not the
        // literal, so a CLI-compatible `trace-bin: ${TRACE_BIN}` is not wrongly rejected.
        var row = new HeaderRowViewModel
        {
            Name = "trace-bin",
            Value = "${B5_TRACE_BIN}",
            ActiveEnvironmentResolver = name => name == "B5_TRACE_BIN" ? "AAEC" : null // valid base64, 3 bytes
        };

        row.HasBinError.ShouldBeFalse();
        row.BinReadout.ShouldBe("3 bytes");
    }

    [Fact]
    public void A_bin_value_referencing_an_unset_var_is_not_flagged()
    {
        // The value can't be validated until the variable resolves; defer to send time rather than
        // showing a spurious error on a literal that contains an unresolved placeholder.
        var row = new HeaderRowViewModel { Name = "trace-bin", Value = "${B5_NO_SUCH_BIN}" };

        row.HasBinError.ShouldBeFalse();
        row.HasBinReadout.ShouldBeFalse();
    }

    [Fact]
    public void A_bin_value_resolving_to_invalid_base64_surfaces_an_error()
    {
        var row = new HeaderRowViewModel
        {
            Name = "trace-bin",
            Value = "${B5_BAD_BIN}",
            ActiveEnvironmentResolver = name => name == "B5_BAD_BIN" ? "not base64!!" : null
        };

        row.HasBinError.ShouldBeTrue();
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

    // ── FR-066/FR-133: active-environment-aware preview ──────────────────────

    [Fact]
    public void Resolved_preview_prefers_the_active_environment_over_the_os()
    {
        Environment.SetEnvironmentVariable("FU7_HOST", "os-value");
        try
        {
            var row = new HeaderRowViewModel
            {
                Name = "x-region", Value = "${FU7_HOST}",
                ActiveEnvironmentResolver = name => name == "FU7_HOST" ? "env-value" : null
            };

            row.ResolvedPreview.ShouldBe("env-value"); // active env wins over OS
        }
        finally
        {
            Environment.SetEnvironmentVariable("FU7_HOST", null);
        }
    }

    [Fact]
    public void Resolved_preview_falls_back_to_the_os_when_the_active_environment_lacks_the_variable()
    {
        Environment.SetEnvironmentVariable("FU7_ONLY_OS", "os-value");
        try
        {
            var row = new HeaderRowViewModel
            {
                Name = "x-region", Value = "${FU7_ONLY_OS}",
                ActiveEnvironmentResolver = _ => null // not in the active env
            };

            row.ResolvedPreview.ShouldBe("os-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FU7_ONLY_OS", null);
        }
    }

    [Fact]
    public void Refresh_resolved_preview_raises_change_notification()
    {
        var row = new HeaderRowViewModel { Name = "x-region", Value = "${FU7_X}" };
        var raised = false;
        row.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(HeaderRowViewModel.ResolvedPreview);

        row.RefreshResolvedPreview();

        raised.ShouldBeTrue();
    }
}
