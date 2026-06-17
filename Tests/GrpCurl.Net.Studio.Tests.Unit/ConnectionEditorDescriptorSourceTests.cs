using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for the connection editor's descriptor-source section (E2.3 PR-B): mode gating,
///     add/remove/reorder of path rows, over-limit + missing-file flags, the effective-source text,
///     the protoc-missing remediation, and the BuildConnection round-trip.
/// </summary>
public sealed class ConnectionEditorDescriptorSourceTests : IDisposable
{
    private readonly List<string> _temp = [];

    public void Dispose()
    {
        foreach (var f in _temp)
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }

    private string TempFile(long bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grpcn-ds-{Guid.NewGuid():N}.protoset");
        using (var fs = File.Create(path))
        {
            fs.SetLength(bytes);
        }

        _temp.Add(path);
        return path;
    }

    private static ConnectionEditorViewModel Create(
        out FakeFilePickerService picker, out FakeProtocService protoc, SavedConnection? existing = null)
    {
        picker = new FakeFilePickerService();
        protoc = new FakeProtocService();
        return new ConnectionEditorViewModel(
            new FakeConnectionRegistry(), existing, networkDefaults: null,
            profileStore: null, picker, dialogService: null, secretStore: null, protoc);
    }

    [Fact]
    public void Defaults_to_reflection_mode()
    {
        var vm = Create(out _, out _);

        vm.SelectedDescriptorMode.ShouldBe(DescriptorMode.Reflection);
        vm.IsReflectionMode.ShouldBeTrue();
        vm.IsProtosetMode.ShouldBeFalse();
        vm.EffectiveSourceText.ShouldContain("reflection");
    }

    [Fact]
    public void Effective_source_text_tracks_the_mode()
    {
        var vm = Create(out _, out _);

        vm.SelectedDescriptorMode = DescriptorMode.Protoset;
        vm.EffectiveSourceText.ShouldContain("protoset");

        vm.SelectedDescriptorMode = DescriptorMode.Proto;
        vm.EffectiveSourceText.ShouldContain(".proto");
    }

    [Fact]
    public async Task Add_protosets_appends_rows_with_sizes()
    {
        var small = TempFile(2048);
        var vm = Create(out var picker, out _);
        vm.SelectedDescriptorMode = DescriptorMode.Protoset;
        picker.OpenFilesResult = [small];

        await vm.AddProtosetsCommand.ExecuteAsync(null);

        var row = vm.ProtosetRows.ShouldHaveSingleItem();
        row.Path.ShouldBe(small);
        row.HasSize.ShouldBeTrue();
        row.IsOverLimit.ShouldBeFalse();
    }

    [Fact]
    public async Task An_oversized_protoset_is_flagged_before_load()
    {
        var big = TempFile(DescriptorPathRow.ProtosetByteCap + 1);
        var vm = Create(out var picker, out _);
        vm.SelectedDescriptorMode = DescriptorMode.Protoset;
        picker.OpenFilesResult = [big];

        await vm.AddProtosetsCommand.ExecuteAsync(null);

        var row = vm.ProtosetRows.Single();
        row.IsOverLimit.ShouldBeTrue();
        row.Warning!.ShouldContain("64 MiB");
    }

    [Fact]
    public void A_missing_proto_file_is_flagged()
    {
        var connection = new SavedConnection
        {
            DescriptorSource = new DescriptorSourceConfig
            {
                Mode = DescriptorMode.Proto,
                ProtoFiles = ["/no/such/file.proto"]
            }
        };

        var vm = Create(out _, out _, connection);

        vm.ProtoFileRows.Single().Missing.ShouldBeTrue();
    }

    [Fact]
    public void Remove_and_reorder_act_on_the_right_list()
    {
        var connection = new SavedConnection
        {
            DescriptorSource = new DescriptorSourceConfig
            {
                Mode = DescriptorMode.Proto,
                ProtoFiles = ["a.proto", "b.proto", "c.proto"]
            }
        };
        var vm = Create(out _, out _, connection);

        var first = vm.ProtoFileRows[0];
        vm.MoveDescriptorRowDownCommand.Execute(first);
        vm.ProtoFileRows[1].ShouldBe(first); // a moved down

        var b = vm.ProtoFileRows[0];
        vm.RemoveDescriptorRowCommand.Execute(b);
        vm.ProtoFileRows.Select(r => r.Path).ShouldBe(["a.proto", "c.proto"]);
    }

    [Fact]
    public async Task Proto_mode_with_no_protoc_shows_remediation()
    {
        var vm = Create(out _, out var protoc);
        protoc.DetectResult = ProtocInfo.NotFound("protoc not found on PATH.");

        vm.SelectedDescriptorMode = DescriptorMode.Proto;
        await Task.Yield(); // let the detached detect complete (fake returns synchronously)

        vm.ProtocMissing.ShouldBeTrue();
        vm.ShowProtocRemediation.ShouldBeTrue();

        // Remediation action switches to a protoset config.
        vm.SwitchToProtosetCommand.Execute(null);
        vm.SelectedDescriptorMode.ShouldBe(DescriptorMode.Protoset);
        vm.ShowProtocRemediation.ShouldBeFalse();
    }

    [Fact]
    public async Task Proto_mode_with_protoc_present_hides_remediation()
    {
        var vm = Create(out _, out var protoc);
        protoc.DetectResult = ProtocInfo.Ok("/usr/bin/protoc", "libprotoc 28.3");

        vm.SelectedDescriptorMode = DescriptorMode.Proto;
        await Task.Yield();

        vm.ProtocMissing.ShouldBeFalse();
        vm.ShowProtocRemediation.ShouldBeFalse();
    }

    [Fact]
    public async Task Build_connection_round_trips_the_descriptor_source()
    {
        var proto = TempFile(10);
        var vm = Create(out var picker, out _);
        vm.Name = "c";
        vm.Address = "localhost:443";
        vm.SelectedDescriptorMode = DescriptorMode.Proto;
        picker.OpenFilesResult = [proto];
        await vm.AddProtoFilesCommand.ExecuteAsync(null);
        picker.OpenFolderResult = "/inc";
        await vm.AddImportPathCommand.ExecuteAsync(null);

        var built = vm.BuildConnection();

        built.DescriptorSource.Mode.ShouldBe(DescriptorMode.Proto);
        built.DescriptorSource.ProtoFiles.ShouldBe([proto]);
        built.DescriptorSource.ImportPaths.ShouldBe(["/inc"]);
    }

    [Fact]
    public void Editing_an_existing_protoset_connection_preloads_rows()
    {
        var existing = new SavedConnection
        {
            Name = "c",
            DescriptorSource = new DescriptorSourceConfig { Mode = DescriptorMode.Protoset, ProtosetPaths = ["x.protoset"] }
        };

        var vm = Create(out _, out _, existing);

        vm.SelectedDescriptorMode.ShouldBe(DescriptorMode.Protoset);
        vm.ProtosetRows.Single().Path.ShouldBe("x.protoset");
    }

    // ── FR-039: path re-validation at probe ──────────────────────────────────

    [Fact]
    public async Task Testing_with_a_missing_protoset_path_fails_before_probing()
    {
        var existing = new SavedConnection
        {
            Name = "c", Address = "localhost:9090",
            DescriptorSource = new DescriptorSourceConfig
            {
                Mode = DescriptorMode.Protoset, ProtosetPaths = ["/no/such/schema.protoset"]
            }
        };
        var vm = Create(out _, out _, existing);

        await vm.TestConnectionCommand.ExecuteAsync(null);

        vm.LastTestResult.ShouldNotBeNull();
        vm.LastTestResult!.Ok.ShouldBeFalse();
        vm.LastTestResult.Message.ShouldContain("Protoset file not found");
        vm.LastTestResult.Message.ShouldContain("/no/such/schema.protoset");
    }

    // ── FR-049: per-connection descriptor-limit overrides ────────────────────

    [Fact]
    public void Limit_overrides_round_trip_through_build_connection()
    {
        var vm = Create(out _, out _);
        vm.Name = "c";
        vm.Address = "localhost:9090";
        vm.MaxFileDescriptorsOverride = "100";
        vm.MaxSymbolsOverride = "5000";

        vm.LimitsError.ShouldBeNull();
        var built = vm.BuildConnection();

        built.DescriptorSource.MaxFileDescriptors.ShouldBe(100);
        built.DescriptorSource.MaxSymbols.ShouldBe(5000);
        built.DescriptorSource.MaxDependencyDepth.ShouldBeNull(); // blank → Core default
    }

    [Fact]
    public void A_non_positive_override_is_an_error_and_blocks_save()
    {
        var vm = Create(out _, out _);
        vm.Name = "c";
        vm.Address = "localhost:9090";
        vm.SaveCommand.CanExecute(null).ShouldBeTrue();

        vm.MaxFileDescriptorsOverride = "-5";

        vm.LimitsError.ShouldNotBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();

        vm.MaxFileDescriptorsOverride = "abc";
        vm.LimitsError.ShouldNotBeNull();

        vm.MaxFileDescriptorsOverride = ""; // blank clears the error (use default)
        vm.LimitsError.ShouldBeNull();
    }

    [Fact]
    public void Existing_limit_overrides_seed_the_editor_fields()
    {
        var existing = new SavedConnection
        {
            Name = "c",
            DescriptorSource = new DescriptorSourceConfig { MaxFileDescriptors = 256 }
        };
        var vm = Create(out _, out _, existing);

        vm.MaxFileDescriptorsOverride.ShouldBe("256");
    }
}
