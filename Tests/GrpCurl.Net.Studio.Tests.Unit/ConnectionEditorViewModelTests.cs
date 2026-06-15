using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

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
