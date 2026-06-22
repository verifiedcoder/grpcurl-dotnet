using GrpCurl.Net.Studio.TestSupport;
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
    public void Loading_descriptors_logs_a_console_activity()
    {
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(SampleCatalog()) };
        var selection = new ConnectionSelection();
        var console = new ConsoleViewModel();
        _ = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost(), console: console);

        selection.Set(new SavedConnection { Name = "alpha", Address = "h:1" });

        var row = console.Calls.ShouldHaveSingleItem();
        row.KindLabel.ShouldBe("describe");          // FR-004 descriptor operation
        row.Method.ShouldBe("Describe: alpha");
        row.StatusName.ShouldContain("service");     // "2 service(s)"
        row.IsError.ShouldBeFalse();
    }

    [Fact]
    public void A_failed_descriptor_load_logs_an_error_activity()
    {
        var descriptors = new FakeDescriptorService
        {
            Result = DescriptorLoadResult.Failure(new DescriptorLoadError("nope", null, ReflectionUnavailable: false))
        };
        var selection = new ConnectionSelection();
        var console = new ConsoleViewModel();
        _ = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost(), console: console);

        selection.Set(new SavedConnection { Name = "beta", Address = "h:1" });

        var row = console.Calls.ShouldHaveSingleItem();
        row.IsError.ShouldBeTrue();
        row.StatusName.ShouldBe("failed");
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
        _ = vm.Services.ShouldHaveSingleItem();

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
        var (_, descriptors, selection, _, _) = Create();
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

        _ = host.Last.ShouldNotBeNull();
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

        _ = host.LastInvocation.ShouldNotBeNull();
        host.LastInvocation!.Value.Symbol.ShouldBe("pkg.Greeter/SayHello");
        host.LastInvocation.Value.Connection.ShouldBe(connection);
    }

    // ── FR-020: selecting a method publishes its signature to the inspector ───

    [Fact]
    public void Selecting_a_method_node_shows_its_signature_in_the_inspector()
    {
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(SampleCatalog()) };
        var selection = new ConnectionSelection();
        var inspector = new FakeInspector();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost(),
            inspector: inspector);
        selection.Set(Conn());

        var method = vm.Services.First(s => s.FullName == "pkg.Greeter").Methods.First(m => m.Name == "SayHello");
        vm.SelectedNode = method;

        vm.SelectedMethod.ShouldBe(method);
        var shown = inspector.Last.ShouldBeOfType<MethodSignatureContent>();
        shown.FullName.ShouldBe("pkg.Greeter/SayHello");
        shown.InputType.ShouldBe("pkg.Req");
        shown.OutputType.ShouldBe("pkg.Resp");
    }

    [Fact]
    public void Selecting_a_non_method_node_leaves_the_inspector_unchanged()
    {
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(SampleCatalog()) };
        var selection = new ConnectionSelection();
        var inspector = new FakeInspector();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), new FakeDocumentHost(),
            inspector: inspector);
        selection.Set(Conn());

        vm.SelectedNode = vm.Services.First(); // a service branch, not a method leaf

        inspector.Shown.ShouldBeEmpty();
        vm.SelectedMethod.ShouldBeNull();
    }

    // ── SPEC-020 §5 (Ctrl+T): new request tab from the current tree selection ──

    [Fact]
    public void NewRequestForSelected_opens_an_invocation_on_the_selected_method()
    {
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(SampleCatalog()) };
        var selection = new ConnectionSelection();
        var host = new FakeDocumentHost();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), host);
        selection.Set(Conn());
        vm.SelectedNode = vm.Services.First(s => s.FullName == "pkg.Greeter").Methods.First(m => m.Name == "SayHello");

        vm.NewRequestForSelectedCommand.Execute(null);

        var opened = host.LastInvocation.ShouldNotBeNull();
        opened.Symbol.ShouldBe("pkg.Greeter/SayHello");
    }

    [Fact]
    public void NewRequestForSelected_is_a_no_op_when_no_method_is_selected()
    {
        var descriptors = new FakeDescriptorService { Result = DescriptorLoadResult.Success(SampleCatalog()) };
        var selection = new ConnectionSelection();
        var host = new FakeDocumentHost();
        var vm = new ServiceExplorerViewModel(
            descriptors, selection, new FakeClipboardService(), new ImmediateUiDispatcher(), host);
        selection.Set(Conn());
        vm.SelectedNode = vm.Services.First(); // a service node, not a method

        vm.NewRequestForSelectedCommand.Execute(null);

        host.Invocations.ShouldBeEmpty();
    }

    // ── FR-029: sort toggle ──────────────────────────────────────────────────

    [Fact]
    public void Services_and_methods_default_to_file_order()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());

        vm.Services.Select(s => s.FullName).ShouldBe(["pkg.Greeter", "pkg.Admin"]);
        vm.Services[0].Methods.Select(m => m.Name).ShouldBe(["SayHello", "Chat"]);
    }

    [Fact]
    public void Sort_alphabetically_reorders_services_and_methods()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());

        vm.SortAlphabetically = true;

        vm.Services.Select(s => s.FullName).ShouldBe(["pkg.Admin", "pkg.Greeter"]);
        vm.Services.First(s => s.FullName == "pkg.Greeter").Methods.Select(m => m.Name).ShouldBe(["Chat", "SayHello"]);
    }

    // ── FR-028: expansion + selection survive a refresh ──────────────────────

    [Fact]
    public async Task Refresh_preserves_expansion_and_selection_for_the_same_connection()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());

        vm.Services[0].IsExpanded = true;
        vm.SelectedNode = vm.Services[0].Methods[0];
        var selectedFullName = vm.SelectedMethod!.FullName;

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Services[0].IsExpanded.ShouldBeTrue();              // expansion restored on the rebuilt node
        vm.SelectedMethod!.FullName.ShouldBe(selectedFullName); // selection restored by identity
    }

    [Fact]
    public async Task Switching_connection_does_not_carry_expansion_across()
    {
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        selection.Set(Conn());
        vm.Services[0].IsExpanded = true;

        selection.Set(Conn()); // a different connection instance (new Id)
        await Task.Yield();

        vm.Services[0].IsExpanded.ShouldBeFalse(); // fresh tree, no carry-over
    }

    // ── FR-054: copy as .proto ───────────────────────────────────────────────

    [Fact]
    public async Task Copy_proto_copies_the_reconstructed_snippet()
    {
        var (vm, descriptors, selection, clipboard, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(SampleCatalog());
        descriptors.ProtoSnippet = "syntax = \"proto3\";\nmessage Req {}";
        selection.Set(Conn());

        await vm.CopyProtoCommand.ExecuteAsync("pkg.Greeter");

        descriptors.LastProtoSnippetSymbol.ShouldBe("pkg.Greeter");
        clipboard.Text.ShouldBe("syntax = \"proto3\";\nmessage Req {}");
    }

    // ── FR-059: deprecated flags reach the nodes ─────────────────────────────

    [Fact]
    public void Deprecated_services_and_methods_surface_on_their_nodes()
    {
        var catalog = new ServiceCatalog(
        [
            new ServiceEntry("pkg.Old",
                [new ServiceMethod("Gone", "pkg.Old/Gone", StreamingShape.Unary, "pkg.In", "pkg.Out", Deprecated: true)],
                Deprecated: true)
        ], []);
        var (vm, descriptors, selection, _, _) = Create();
        descriptors.Result = DescriptorLoadResult.Success(catalog);
        selection.Set(Conn());

        var service = vm.Services.ShouldHaveSingleItem();
        service.Deprecated.ShouldBeTrue();
        service.Methods.ShouldHaveSingleItem().Deprecated.ShouldBeTrue();
    }
}
