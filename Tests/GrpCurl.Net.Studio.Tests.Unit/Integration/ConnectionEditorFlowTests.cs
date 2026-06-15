using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 E2E through the view model: the connection editor's Test-connection command drives the
///     real <see cref="ConnectionRegistry" /> against the in-process TestServer, proving the
///     GUI → service → Core → live-gRPC path end to end.
/// </summary>
[Collection(StudioPlaintextServerCollection.Name)]
public sealed class ConnectionEditorFlowTests(StudioPlaintextServerFixture server)
{
    [Fact]
    public async Task Test_connection_against_live_server_reports_success_with_service_count()
    {
        var editor = new ConnectionEditorViewModel(new ConnectionRegistry())
        {
            Name = "local",
            Address = server.Address,
            IsPlaintext = true
        };

        await editor.TestConnectionCommand.ExecuteAsync(null);

        editor.LastTestResult.ShouldNotBeNull();
        editor.LastTestResult!.Ok.ShouldBeTrue(editor.LastTestResult.Message);
        editor.LastTestResult.ServiceCount.ShouldNotBeNull();
        editor.LastTestResult.ServiceCount!.Value.ShouldBeGreaterThanOrEqualTo(1);

        // The built connection is valid and ready to save.
        editor.SaveCommand.CanExecute(null).ShouldBeTrue();
        editor.BuildConnection().Transport.ShouldBe(TransportMode.Plaintext);
    }

    [Fact]
    public async Task Test_connection_to_tls_against_plaintext_server_reports_failure()
    {
        var editor = new ConnectionEditorViewModel(new ConnectionRegistry())
        {
            Name = "local",
            Address = server.Address,
            IsPlaintext = false // TLS against a plaintext server → handshake failure
        };

        await editor.TestConnectionCommand.ExecuteAsync(null);

        editor.LastTestResult.ShouldNotBeNull();
        editor.LastTestResult!.Ok.ShouldBeFalse();
    }
}
