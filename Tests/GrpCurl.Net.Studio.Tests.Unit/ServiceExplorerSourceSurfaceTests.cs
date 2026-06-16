using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for the E2.3 PR-C Service Explorer source surface: the header source badge (kind +
///     counts + duration + last-refreshed, FR-040/048), the warnings strip mirrored to the console
///     (FR-046), and the protoc-in-use detail for proto sources (FR-043).
/// </summary>
public sealed class ServiceExplorerSourceSurfaceTests
{
    private static ServiceCatalog Catalog(IReadOnlyList<string> warnings) => new(
        [new ServiceEntry("pkg.Greeter", [new ServiceMethod("Hi", "pkg.Greeter/Hi", StreamingShape.Unary, "pkg.A", "pkg.B")])],
        warnings)
    {
        Types = [new TypeEntry("pkg.A", TypeNodeKind.Message, "pkg")],
        FileCount = 3,
        SymbolCount = 12,
        LoadDuration = TimeSpan.FromMilliseconds(42)
    };

    private static (ServiceExplorerViewModel Vm, ConnectionSelection Selection, ConsoleViewModel Console, FakeProtocService Protoc)
        Create(IReadOnlyList<string>? warnings = null)
    {
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(Catalog(warnings ?? [])) };
        var selection = new ConnectionSelection();
        var console = new ConsoleViewModel();
        var protoc = new FakeProtocService();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost(), protoc, console);
        return (vm, selection, console, protoc);
    }

    private static SavedConnection Conn(DescriptorMode mode) => new()
    {
        Name = "c",
        Address = "h:1",
        DescriptorSource = new DescriptorSourceConfig { Mode = mode }
    };

    [Fact]
    public void Loaded_reflection_source_shows_a_badge_with_counts_and_timestamp()
    {
        var (vm, selection, _, _) = Create();

        selection.Set(Conn(DescriptorMode.Reflection));

        vm.SourceKind.ShouldBe("Server reflection");
        vm.SourceSummary!.ShouldContain("3 file");
        vm.SourceSummary!.ShouldContain("12 symbol");
        vm.SourceSummary!.ShouldContain("42 ms");
        vm.LastRefreshed.ShouldNotBeNull();
        vm.LastRefreshedText.ShouldNotBeNull();
    }

    [Fact]
    public void Protoset_source_kind_is_labelled()
    {
        var (vm, selection, _, _) = Create();

        selection.Set(Conn(DescriptorMode.Protoset));

        vm.SourceKind.ShouldBe("Protoset");
        vm.HasProtocDetail.ShouldBeFalse();
    }

    [Fact]
    public void Proto_source_shows_the_protoc_in_use_detail()
    {
        var (vm, selection, _, protoc) = Create();
        protoc.DetectResult = ProtocInfo.Ok("/usr/bin/protoc", "libprotoc 28.3");

        selection.Set(Conn(DescriptorMode.Proto));

        vm.SourceKind.ShouldBe("Proto (protoc)");
        vm.HasProtocDetail.ShouldBeTrue();
        vm.ProtocDetail!.ShouldContain("28.3");
    }

    [Fact]
    public void Warnings_populate_the_strip_and_mirror_to_the_console()
    {
        var (vm, selection, console, _) = Create(["duplicate file a.proto", "overwrote cached descriptor"]);

        selection.Set(Conn(DescriptorMode.Protoset));

        vm.HasWarnings.ShouldBeTrue();
        vm.WarningCount.ShouldBe(2);
        vm.Warnings.ShouldContain("duplicate file a.proto");
        console.Messages.Count.ShouldBe(2);
        console.Messages.ShouldContain(m => m.Contains("duplicate file a.proto"));
    }

    [Fact]
    public void No_warnings_hides_the_strip()
    {
        var (vm, selection, console, _) = Create();

        selection.Set(Conn(DescriptorMode.Reflection));

        vm.HasWarnings.ShouldBeFalse();
        vm.WarningCount.ShouldBe(0);
        console.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void Deselecting_a_connection_clears_the_badge_and_warnings()
    {
        var (vm, selection, _, _) = Create(["w"]);
        selection.Set(Conn(DescriptorMode.Protoset));
        vm.HasWarnings.ShouldBeTrue();

        selection.Set(null);

        vm.SourceKind.ShouldBeNull();
        vm.HasWarnings.ShouldBeFalse();
        vm.LastRefreshed.ShouldBeNull();
    }
}
