using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     FR-145 PR-A: saved-request persistence (SPEC-040 §3.2 shape) and the workspace-level store CRUD.
/// </summary>
public sealed class SavedRequestTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SavedRequest Sample() => new()
    {
        Id = "r1", Name = "say hello", ConnectionId = "c1", Method = "demo.Greeter/SayHello",
        BodyFormat = RequestBodyFormat.Text,
        Body = "{ \"name\": \"world\" }",
        Headers = [new HeaderEntry { Name = "authorization", Value = "${TOKEN}" }],
        Deadline = "10s",
        EmitDefaults = true,
        AllowUnknownFields = true,
        MaxSendBytes = 1024,
        MaxReceiveBytes = 2048
    };

    // ── persistence (SPEC-040 §3.2) ──────────────────────────────────────────

    [Fact]
    public void A_saved_request_serializes_with_the_schema_shape()
    {
        var json = WorkspaceSerializer.Serialize(new WorkspaceModel { Id = "w", Name = "n", SavedRequests = [Sample()] });

        json.ShouldContain("\"savedRequests\":");
        json.ShouldContain("\"bodyFormat\": \"text\"");  // enum → lowercase string
        json.ShouldContain("\"method\": \"demo.Greeter/SayHello\"");
        json.ShouldContain("\"maxSendBytes\": 1024");
        json.ShouldContain("\"maxReceiveBytes\": 2048");
    }

    [Fact]
    public void Saved_requests_round_trip_byte_stable()
    {
        var json = WorkspaceSerializer.Serialize(new WorkspaceModel { Id = "w", Name = "n", SavedRequests = [Sample()] });

        var model = WorkspaceSerializer.Deserialize(json);

        var request = model.SavedRequests.ShouldHaveSingleItem();
        request.Name.ShouldBe("say hello");
        request.BodyFormat.ShouldBe(RequestBodyFormat.Text);
        request.Headers.ShouldHaveSingleItem().Value.ShouldBe("${TOKEN}");
        request.MaxSendBytes.ShouldBe(1024);
        WorkspaceSerializer.Serialize(model).ShouldBe(json);
    }

    [Fact]
    public void Copy_is_independent_of_the_original()
    {
        var original = Sample();
        var copy = original.Copy();

        copy.Headers.Add(new HeaderEntry { Name = "x", Value = "y" });
        copy.Name = "changed";

        original.Headers.ShouldHaveSingleItem(); // original untouched
        original.Name.ShouldBe("say hello");
    }

    // ── store CRUD (FR-145) ──────────────────────────────────────────────────

    private static SavedRequestStore Store(out FakeWorkspaceStore workspace, WorkspaceModel? initial = null)
    {
        workspace = new FakeWorkspaceStore(initial ?? new WorkspaceModel());
        return new SavedRequestStore(workspace);
    }

    [Fact]
    public async Task Saving_a_request_preserves_the_rest_of_the_workspace()
    {
        var store = Store(out var workspace, new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "api" }],
            Environments = [new WorkspaceEnvironment { Id = "e", Name = "staging" }]
        });

        await store.SaveAsync(Sample(), Ct);

        workspace.Current.Connections.ShouldHaveSingleItem();
        workspace.Current.Environments.ShouldHaveSingleItem();
        store.Requests.ShouldContain(r => r.Name == "say hello");
    }

    [Fact]
    public async Task Saving_an_existing_id_replaces_rather_than_appends()
    {
        var store = Store(out _, new WorkspaceModel { SavedRequests = [Sample()] });

        await store.SaveAsync(new SavedRequest { Id = "r1", Name = "renamed", ConnectionId = "c1", Method = "m" }, Ct);

        var request = store.Requests.ShouldHaveSingleItem();
        request.Name.ShouldBe("renamed");
    }

    [Fact]
    public void For_connection_filters_by_connection_id()
    {
        var store = Store(out _, new WorkspaceModel
        {
            SavedRequests =
            [
                new SavedRequest { Id = "a", Name = "1", ConnectionId = "c1", Method = "m" },
                new SavedRequest { Id = "b", Name = "2", ConnectionId = "c2", Method = "m" },
                new SavedRequest { Id = "c", Name = "3", ConnectionId = "c1", Method = "m" }
            ]
        });

        store.ForConnection("c1").Select(r => r.Name).ShouldBe(["1", "3"]);
        store.ForConnection("none").ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_removes_the_request()
    {
        var store = Store(out _, new WorkspaceModel { SavedRequests = [Sample()] });

        await store.DeleteAsync("r1", Ct);

        store.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_an_unknown_id_is_a_no_op()
    {
        var store = Store(out var workspace, new WorkspaceModel { SavedRequests = [Sample()] });
        var before = workspace.SaveCount;

        await store.DeleteAsync("missing", Ct);

        store.Requests.ShouldHaveSingleItem();
        workspace.SaveCount.ShouldBe(before); // nothing written
    }
}
