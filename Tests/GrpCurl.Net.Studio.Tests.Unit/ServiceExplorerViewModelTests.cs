using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Explorer;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Panes;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class ServiceExplorerViewModelTests
{
    private static ServiceCatalog SampleCatalog() => new(
    [
        new ServiceEntry("pkg.Greeter",
        [
            new ServiceMethod("SayHello", "pkg.Greeter/SayHello", StreamingShape.Unary, "pkg.Req", "pkg.Resp"),
            new ServiceMethod("Chat", "pkg.Greeter/Chat", StreamingShape.BidiStreaming, "pkg.Msg", "pkg.Msg")
        ]),
        new ServiceEntry("pkg.Admin",
        [
            new ServiceMethod("Reload", "pkg.Admin/Reload", StreamingShape.Unary, "pkg.Empty", "pkg.Empty")
        ])
    ], [])
    {
        Types =
        [
            new TypeEntry("pkg.Req", TypeNodeKind.Message, "pkg"),
            new TypeEntry("pkg.Resp", TypeNodeKind.Message, "pkg"),
            new TypeEntry("other.Colour", TypeNodeKind.Enum, "other")
        ]
    };

    private static (ServiceExplorerViewModel vm, FakeDescriptorService descriptors, ConnectionSelection selection, FakeClipboardService clipboard, FakeDocumentHost host) Create()
    {
        var descriptors = new FakeDescriptorService();
        var selection = new ConnectionSelection();
        var clipboard = new FakeClipboardService();
        var host = new FakeDocumentHost();
        var vm = new ServiceExplorerViewModel(descriptors, selection, clipboard, new ImmediateUiDispatcher(), host);
        return (vm, descriptors, selection, clipboard, host);
    }

    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    [Fact]
    public void Starts_in_the_no_connection_state()
    {
        var (vm, _, _, _, _) = Create();

        vm.IsNoConnection.ShouldBeTrue();
        vm.Services.ShouldBeEmpty();
    }

    [Fact]
    public void Selecting_a_connection_loads_the_tree_with_shape_badges()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());

        selection.Set(Conn());

        vm.IsLoaded.ShouldBeTrue();
        vm.Services.Select(s => s.FullName).ShouldBe(["pkg.Greeter", "pkg.Admin"]);

        var greeter = vm.Services.First();
        greeter.MethodCount.ShouldBe(2);
        greeter.Methods.Single(m => m.Name == "Chat").Badge.ShouldBe("BD");
        greeter.Methods.Single(m => m.Name == "SayHello").Badge.ShouldBe("U");
    }

    [Fact]
    public void Empty_catalog_yields_the_empty_state()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(ServiceCatalog.Empty);

        selection.Set(Conn());

        vm.IsEmpty.ShouldBeTrue();
        vm.Services.ShouldBeEmpty();
    }

    [Fact]
    public void Reflection_failure_yields_the_error_state_with_hint()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Failure(
            new DescriptorLoadError("No reflection here.", "Use a protoset instead.", ReflectionUnavailable: true));

        selection.Set(Conn());

        vm.HasError.ShouldBeTrue();
        vm.ErrorMessage.ShouldBe("No reflection here.");
        vm.ErrorHint.ShouldBe("Use a protoset instead.");
        vm.ReflectionUnavailable.ShouldBeTrue();
    }

    [Fact]
    public void Clearing_the_connection_returns_to_no_connection_state()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());
        vm.IsLoaded.ShouldBeTrue();

        selection.Set(null);

        vm.IsNoConnection.ShouldBeTrue();
        vm.Services.ShouldBeEmpty();
    }

    [Fact]
    public void Filter_matches_method_names_and_prunes_other_services()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());

        vm.FilterText = "SayHello";

        var service = vm.Services.ShouldHaveSingleItem();
        service.FullName.ShouldBe("pkg.Greeter");
        service.Methods.ShouldHaveSingleItem().Name.ShouldBe("SayHello");
        service.IsExpanded.ShouldBeTrue(); // matched branches auto-expand
    }

    [Fact]
    public void Filter_matches_service_name_and_keeps_all_its_methods()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());

        vm.FilterText = "greeter";

        var service = vm.Services.ShouldHaveSingleItem();
        service.FullName.ShouldBe("pkg.Greeter");
        service.Methods.Count.ShouldBe(2);
    }

    [Fact]
    public void Clearing_the_filter_restores_all_services()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());
        vm.FilterText = "Admin";
        vm.Services.ShouldHaveSingleItem();

        vm.FilterText = string.Empty;

        vm.Services.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Copy_full_name_writes_to_the_clipboard()
    {
        var (vm, _, _, clipboard, _) = Create();

        await vm.CopyFullNameCommand.ExecuteAsync("pkg.Greeter/SayHello");

        clipboard.Text.ShouldBe("pkg.Greeter/SayHello");
    }

    [Fact]
    public void Refresh_is_disabled_without_a_connection_and_enabled_with_one()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());

        vm.RefreshCommand.CanExecute(null).ShouldBeFalse();

        selection.Set(Conn());

        vm.RefreshCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Selecting_a_connection_reloads_through_the_descriptor_service()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());

        var connection = Conn();
        selection.Set(connection);

        descriptors.LoadCount.ShouldBe(1);
        descriptors.LastLoaded.ShouldBe(connection);
    }

    [Fact]
    public void Types_branch_groups_types_by_package()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());

        selection.Set(Conn());

        vm.TypePackages.Select(p => p.Package).ShouldBe(["other", "pkg"]);
        var pkg = vm.TypePackages.Single(p => p.Package == "pkg");
        pkg.TypeCount.ShouldBe(2);
        pkg.Types.Select(t => t.FullName).ShouldBe(["pkg.Req", "pkg.Resp"]);
        vm.TypePackages.Single(p => p.Package == "other").Types.Single().Badge.ShouldBe("E");
    }

    [Fact]
    public void Filter_prunes_the_types_branch_too()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());

        vm.FilterText = "Colour";

        var package = vm.TypePackages.ShouldHaveSingleItem();
        package.Package.ShouldBe("other");
        package.Types.ShouldHaveSingleItem().FullName.ShouldBe("other.Colour");
    }

    [Fact]
    public void Describe_command_opens_a_describe_tab_for_the_active_connection()
    {
        var (vm, _, selection, _, host) = Create();
        var connection = Conn();
        selection.Set(connection);

        vm.DescribeCommand.Execute("pkg.Greeter");

        host.Last.ShouldNotBeNull();
        host.Last!.Value.Connection.ShouldBe(connection);
        host.Last.Value.Symbol.ShouldBe("pkg.Greeter");
        host.Last.Value.NewTab.ShouldBeFalse();
    }

    [Fact]
    public void Describe_command_is_a_no_op_without_a_connection()
    {
        var (vm, _, _, _, host) = Create();

        vm.DescribeCommand.Execute("pkg.Greeter");

        host.Opened.ShouldBeEmpty();
    }

    [Fact]
    public void New_request_command_opens_an_invocation_tab_for_the_active_connection()
    {
        var (vm, _, selection, _, host) = Create();
        var connection = Conn();
        selection.Set(connection);

        vm.NewRequestCommand.Execute("pkg.Greeter/SayHello");

        host.LastInvocation.ShouldNotBeNull();
        host.LastInvocation!.Value.Symbol.ShouldBe("pkg.Greeter/SayHello");
        host.LastInvocation.Value.Connection.ShouldBe(connection);
    }
}
