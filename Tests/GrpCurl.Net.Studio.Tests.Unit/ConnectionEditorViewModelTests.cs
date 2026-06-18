using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Panes;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class ConnectionEditorViewModelTests
{
    [Fact]
    public void New_editor_starts_invalid_until_name_and_address_are_set()
    {
        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry());

        vm.IsEdit.ShouldBeFalse();
        vm.Title.ShouldBe("New connection");
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();

        vm.Name = "prod";
        vm.Address = "localhost:9090";

        vm.SaveCommand.CanExecute(null).ShouldBeTrue();
        vm.TestConnectionCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void New_connection_seeds_network_fields_from_the_app_defaults()
    {
        var defaults = new NetworkSettings { ConnectTimeout = "7s", KeepaliveTime = "45s", KeepaliveTimeout = "20s" };

        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry(), existing: null, defaults);

        vm.ConnectTimeout.ShouldBe("7s");
        vm.KeepaliveTime.ShouldBe("45s");
        vm.KeepaliveTimeout.ShouldBe("20s");
    }

    [Fact]
    public void Editing_an_existing_connection_ignores_the_app_defaults()
    {
        var existing = new SavedConnection { Name = "s", Address = "a:443", ConnectTimeout = "5s" };
        var defaults = new NetworkSettings { ConnectTimeout = "99s" };

        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry(), existing, defaults);

        vm.ConnectTimeout.ShouldBe("5s"); // the connection's own value wins
    }

    [Fact]
    public void A_unix_socket_address_disables_the_tls_profile_picker()
    {
        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry()) { Name = "uds", Address = "h:1" };
        vm.IsTlsProfileEnabled.ShouldBeTrue(); // TLS over TCP

        vm.Address = "unix:///tmp/grpc.sock";

        vm.IsUnixSocket.ShouldBeTrue();
        vm.IsTlsProfileEnabled.ShouldBeFalse(); // TLS doesn't apply to Unix sockets
    }

    [Fact]
    public void Invalid_address_disables_save_and_test_and_surfaces_error()
    {
        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry()) { Name = "x", Address = "bad" };

        vm.AddressError.ShouldNotBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();
        vm.TestConnectionCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Editing_existing_connection_seeds_fields_and_preserves_id()
    {
        var existing = new SavedConnection
        {
            Name = "staging",
            Address = "api:443",
            Transport = TransportMode.Plaintext,
            ConnectTimeout = "5s",
            ReflectionHeaders = [new HeaderEntry { Name = "authorization", Value = "Bearer t" }]
        };

        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry(), existing);

        vm.IsEdit.ShouldBeTrue();
        vm.Title.ShouldBe("Edit connection");
        vm.Name.ShouldBe("staging");
        vm.IsPlaintext.ShouldBeTrue();
        vm.ConnectTimeout.ShouldBe("5s");
        vm.ReflectionHeaders.Single().Name.ShouldBe("authorization");

        var built = vm.BuildConnection();
        built.Id.ShouldBe(existing.Id); // edit preserves identity
        built.Transport.ShouldBe(TransportMode.Plaintext);
    }

    [Fact]
    public async Task Test_connection_records_result_from_registry()
    {
        var registry = new FakeConnectionRegistry { Result = TestConnectionResult.Success(3) };
        var vm = new ConnectionEditorViewModel(registry) { Name = "x", Address = "localhost:9090" };

        await vm.TestConnectionCommand.ExecuteAsync(null);

        vm.LastTestResult.ShouldNotBeNull();
        vm.LastTestResult!.Ok.ShouldBeTrue();
        vm.LastTestResult.ServiceCount.ShouldBe(3);
        registry.LastTested!.Address.ShouldBe("localhost:9090");
    }

    [Fact]
    public async Task Test_connection_mirrors_a_connect_activity_to_the_console()
    {
        var console = new ConsoleViewModel();
        var registry = new FakeConnectionRegistry { Result = TestConnectionResult.Success(2) };
        var vm = new ConnectionEditorViewModel(registry, console: console) { Name = "prod", Address = "localhost:9090" };

        await vm.TestConnectionCommand.ExecuteAsync(null);

        var row = console.Calls.ShouldHaveSingleItem();
        row.KindLabel.ShouldBe("connect");
        row.Method.ShouldBe("Test connection: prod");
        row.StatusName.ShouldBe("connected");
        row.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_test_connection_is_recorded_as_an_error_row()
    {
        var console = new ConsoleViewModel();
        var registry = new FakeConnectionRegistry { Result = TestConnectionResult.Failure("unreachable") };
        var vm = new ConnectionEditorViewModel(registry, console: console) { Name = "prod", Address = "localhost:9090" };

        await vm.TestConnectionCommand.ExecuteAsync(null);

        var row = console.Calls.ShouldHaveSingleItem();
        row.StatusName.ShouldBe("failed");
        row.IsError.ShouldBeTrue();
    }

    [Fact]
    public void Save_closes_with_built_connection()
    {
        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry()) { Name = " prod ", Address = " localhost:9090 " };
        SavedConnection? result = null;
        vm.CloseRequested += r => result = r;

        vm.SaveCommand.Execute(null);

        result.ShouldNotBeNull();
        result!.Name.ShouldBe("prod"); // trimmed
        result.Address.ShouldBe("localhost:9090");
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry());
        var raised = false;
        SavedConnection? result = new();
        vm.CloseRequested += r => { raised = true; result = r; };

        vm.CancelCommand.Execute(null);

        raised.ShouldBeTrue();
        result.ShouldBeNull();
    }

    [Fact]
    public void Add_and_remove_header_rows()
    {
        var vm = new ConnectionEditorViewModel(new FakeConnectionRegistry());

        vm.AddHeaderCommand.Execute(null);
        vm.ReflectionHeaders.Count.ShouldBe(1);

        var row = vm.ReflectionHeaders[0];
        row.Name = "x-trace-bin";
        row.IsBin.ShouldBeTrue();

        vm.RemoveHeaderCommand.Execute(row);
        vm.ReflectionHeaders.ShouldBeEmpty();
    }
}
