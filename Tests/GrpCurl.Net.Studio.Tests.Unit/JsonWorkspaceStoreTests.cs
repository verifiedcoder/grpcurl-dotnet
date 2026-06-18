using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class JsonWorkspaceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-ws-tests-" + Guid.NewGuid().ToString("N"));

    public JsonWorkspaceStoreTests() => Directory.CreateDirectory(_dir);

    private string Path_ => Path.Combine(_dir, "workspace.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Save_then_load_round_trips_connections()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);

        var workspace = new WorkspaceModel
        {
            Connections =
            [
                new SavedConnection
                {
                    Name = "staging",
                    Address = "api.example.com:443",
                    Transport = TransportMode.Tls,
                    ConnectTimeout = "10s",
                    Authority = "edge",
                    ReflectionHeaders = [new HeaderEntry { Name = "authorization", Value = "Bearer x" }]
                }
            ]
        };

        await store.SaveAsync(workspace, ct);

        File.Exists(Path_).ShouldBeTrue();

        var reloaded = await new JsonWorkspaceStore(Path_).LoadAsync(ct);

        reloaded.SchemaVersion.ShouldBe(1);
        reloaded.Connections.Count.ShouldBe(1);
        var c = reloaded.Connections[0];
        c.Name.ShouldBe("staging");
        c.Address.ShouldBe("api.example.com:443");
        c.Transport.ShouldBe(TransportMode.Tls);
        c.ConnectTimeout.ShouldBe("10s");
        c.Authority.ShouldBe("edge");
        c.ReflectionHeaders.Single().Name.ShouldBe("authorization");
    }

    [Fact]
    public async Task File_references_beneath_the_workspace_are_stored_relative_but_resolve_to_absolute(/* FR-147 */)
    {
        var ct = TestContext.Current.CancellationToken;
        var protosetAbs = Path.Combine(_dir, "protos", "svc.protoset");
        var caAbs = Path.Combine(_dir, "tls", "ca.pem");
        var outsideAbs = Path.GetFullPath(Path.Combine(_dir, "..", "shared", "import.protoset"));

        var workspace = new WorkspaceModel
        {
            Connections =
            [
                new SavedConnection
                {
                    Name = "svc", Address = "h:1", Transport = TransportMode.Plaintext,
                    DescriptorSource = new DescriptorSourceConfig { ProtosetPaths = [protosetAbs, outsideAbs] }
                }
            ],
            TlsProfiles = [new TlsProfile { Name = "p", CaCertPath = caAbs }]
        };

        await new JsonWorkspaceStore(Path_).SaveAsync(workspace, ct);

        // On disk: paths beneath the workspace dir are relative (forward-slash); the outside one stays absolute.
        var raw = await File.ReadAllTextAsync(Path_, ct);
        raw.ShouldContain("protos/svc.protoset");
        raw.ShouldContain("tls/ca.pem");
        raw.ShouldNotContain(protosetAbs.Replace('\\', '/'));
        raw.ShouldContain(JsonEscaped(outsideAbs));

        // In memory after load: every reference is absolute again.
        var reloaded = await new JsonWorkspaceStore(Path_).LoadAsync(ct);
        reloaded.Connections[0].DescriptorSource.ProtosetPaths.ShouldBe([protosetAbs, outsideAbs]);
        reloaded.TlsProfiles[0].CaCertPath.ShouldBe(caAbs);
    }

    // Paths embedded in JSON have backslashes escaped; normalise to compare against an absolute Windows path.
    private static string JsonEscaped(string path) => path.Replace("\\", "\\\\");

    [Fact]
    public void New_with_a_starter_connection_seeds_the_example_connection(/* FR-149 */)
    {
        var store = new JsonWorkspaceStore(Path_);

        var workspace = store.NewWorkspace(withStarterConnection: true);

        store.CurrentPath.ShouldBeNull(); // untitled until Save As
        var connection = workspace.Connections.ShouldHaveSingleItem();
        connection.Address.ShouldBe("localhost:9090");
        connection.Transport.ShouldBe(TransportMode.Plaintext);
    }

    [Fact]
    public void New_without_a_template_is_empty()
        => new JsonWorkspaceStore(Path_).NewWorkspace().Connections.ShouldBeEmpty();

    private static readonly DateTimeOffset LockNow = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private static WorkspaceLockManager LockManager(int pid)
        => new(pid, "host", "1.0", () => LockNow, _ => true);

    [Fact]
    public async Task A_foreign_lock_opens_the_workspace_locked_and_suppresses_autosave_until_taken_over(/* SPEC-040 §8 */)
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_dir, "shared.gcnws.json");
        await new JsonWorkspaceStore(Path_).SaveAsAsync(new WorkspaceModel { Name = "Shared" }, path, ct);

        LockManager(100).TakeOver(path); // a live foreign instance (pid 100) holds the lock

        var store = new JsonWorkspaceStore(Path_, lockManager: LockManager(200));
        await store.OpenAsync(path, ct);

        store.IsLockedByAnother.ShouldBeTrue();
        store.ForeignLock!.Pid.ShouldBe(100);

        // Autosave is suppressed while foreign-locked: the change stays in memory, the file is untouched.
        var before = await File.ReadAllTextAsync(path, ct);
        await store.SaveAsync(new WorkspaceModel { Name = "Edited" }, ct);
        store.IsDirty.ShouldBeTrue();
        (await File.ReadAllTextAsync(path, ct)).ShouldBe(before);

        // Take over → no longer locked, and saves land.
        await store.TakeOverLockAsync(ct);
        store.IsLockedByAnother.ShouldBeFalse();
        await store.SaveAsync(new WorkspaceModel { Name = "Edited2" }, ct);
        (await File.ReadAllTextAsync(path, ct)).ShouldContain("Edited2");
    }

    [Fact]
    public async Task Losing_the_lock_to_another_instance_degrades_on_the_next_save(/* SPEC-040 §8 */)
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_dir, "owned.gcnws.json");

        var writer = new JsonWorkspaceStore(Path_);
        await writer.SaveAsAsync(new WorkspaceModel { Name = "Owned" }, path, ct);
        writer.ReleaseLock(); // leave the file with no lingering lock

        var store = new JsonWorkspaceStore(Path_, lockManager: LockManager(200));
        await store.OpenAsync(path, ct);
        store.IsLockedByAnother.ShouldBeFalse();

        LockManager(300).TakeOver(path); // another instance steals the lock

        var before = await File.ReadAllTextAsync(path, ct);
        await store.SaveAsync(new WorkspaceModel { Name = "Edited" }, ct);

        store.IsLockedByAnother.ShouldBeTrue();
        store.ForeignLock!.Pid.ShouldBe(300);
        (await File.ReadAllTextAsync(path, ct)).ShouldBe(before);
    }

    [Fact]
    public async Task A_read_only_file_opens_read_only_and_suppresses_autosave_until_save_as(/* FR-148 */)
    {
        var ct = TestContext.Current.CancellationToken;
        var roPath = Path.Combine(_dir, "locked.gcnws.json");

        // Write a valid workspace, then mark the file read-only on disk (cross-platform).
        await new JsonWorkspaceStore(Path_).SaveAsAsync(new WorkspaceModel { Name = "Locked" }, roPath, ct);
        File.SetAttributes(roPath, FileAttributes.ReadOnly);

        try
        {
            var store = new JsonWorkspaceStore(Path_);
            await store.OpenAsync(roPath, ct);
            store.IsCurrentReadOnly.ShouldBeTrue();

            var before = await File.ReadAllTextAsync(roPath, ct);

            // Autosave is suppressed: the change stays in memory (dirty), the file is untouched.
            await store.SaveAsync(new WorkspaceModel { Name = "Edited" }, ct);
            store.Current.Name.ShouldBe("Edited");
            store.IsDirty.ShouldBeTrue();
            (await File.ReadAllTextAsync(roPath, ct)).ShouldBe(before);

            // Save As to a writable path clears read-only and writes.
            var writable = Path.Combine(_dir, "copy.gcnws.json");
            await store.SaveAsAsync(store.Current, writable, ct);
            store.IsCurrentReadOnly.ShouldBeFalse();
            File.Exists(writable).ShouldBeTrue();
        }
        finally
        {
            File.SetAttributes(roPath, FileAttributes.Normal); // let the temp dir clean up on Windows
        }
    }

    [Fact]
    public async Task Enum_serializes_as_string()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);

        await store.SaveAsync(new WorkspaceModel { Connections = [new SavedConnection { Name = "p", Address = "h:1", Transport = TransportMode.Plaintext }] }, ct);

        var text = await File.ReadAllTextAsync(Path_, ct);
        text.ShouldContain("plaintext");
        text.ShouldNotContain("\"transport\": 1");
    }

    [Fact]
    public async Task Load_missing_file_returns_empty()
    {
        var settings = await new JsonWorkspaceStore(Path_).LoadAsync(TestContext.Current.CancellationToken);

        settings.Connections.ShouldBeEmpty();
        settings.SchemaVersion.ShouldBe(1);
    }

    // ── E3.1 PR-B: open / save-as / new + recents ────────────────────────────

    private string DocPath(string name) => Path.Combine(_dir, name + ".gcnws.json");

    private static WorkspaceModel Named(string name) => new() { Id = Guid.NewGuid().ToString("D"), Name = name };

    [Fact]
    public async Task Save_as_writes_the_document_sets_current_path_and_heads_recents()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        var target = DocPath("project-a");

        await store.SaveAsAsync(Named("Project A"), target, ct);

        File.Exists(target).ShouldBeTrue();
        store.CurrentPath.ShouldBe(target);
        store.RecentWorkspaces[0].Path.ShouldBe(Path.GetFullPath(target));
    }

    [Fact]
    public async Task Open_loads_a_document_sets_current_path_and_heads_recents()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = DocPath("project-b");
        await new JsonWorkspaceStore(Path_).SaveAsAsync(Named("Project B"), target, ct);

        var store = new JsonWorkspaceStore(Path_);
        var opened = await store.OpenAsync(target, ct);

        opened.Name.ShouldBe("Project B");
        store.Current.Name.ShouldBe("Project B");
        store.CurrentPath.ShouldBe(target);
        store.RecentWorkspaces[0].Path.ShouldBe(Path.GetFullPath(target));
    }

    [Fact]
    public async Task Open_a_newer_version_throws_and_leaves_current_untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = DocPath("future");
        await File.WriteAllTextAsync(target, """{ "schemaVersion": 999, "id": "x", "name": "future" }""", ct);
        var store = new JsonWorkspaceStore(Path_);
        var before = store.Current;

        var ex = await Should.ThrowAsync<WorkspaceSchemaException>(() => store.OpenAsync(target, ct));

        ex.IsNewerVersion.ShouldBeTrue();
        store.Current.ShouldBeSameAs(before); // unchanged on failure
        store.CurrentPath.ShouldBe(Path_);     // still the default
    }

    [Fact]
    public async Task Open_a_corrupt_file_throws_a_schema_exception()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = DocPath("broken");
        await File.WriteAllTextAsync(target, "{ not json", ct);

        await Should.ThrowAsync<WorkspaceSchemaException>(() => new JsonWorkspaceStore(Path_).OpenAsync(target, ct));
    }

    [Fact]
    public async Task Recents_dedupe_move_to_front_and_cap_at_ten()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);

        for (var i = 0; i < 12; i++)
        {
            await store.SaveAsAsync(Named($"w{i}"), DocPath($"w{i}"), ct);
        }

        await store.SaveAsAsync(Named("w3-again"), DocPath("w3"), ct); // re-touch an existing path

        store.RecentWorkspaces.Count.ShouldBe(10);                       // capped
        store.RecentWorkspaces[0].Path.ShouldBe(Path.GetFullPath(DocPath("w3"))); // moved to front, not duplicated
        store.RecentWorkspaces.Count(r => r.Path == Path.GetFullPath(DocPath("w3"))).ShouldBe(1);
    }

    [Fact]
    public async Task Recents_persist_across_store_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        await new JsonWorkspaceStore(Path_).SaveAsAsync(Named("persisted"), DocPath("persisted"), ct);

        var reopened = new JsonWorkspaceStore(Path_);

        reopened.RecentWorkspaces.ShouldContain(r => r.Path == Path.GetFullPath(DocPath("persisted")));
    }

    [Fact]
    public async Task A_deleted_recent_file_is_flagged_dangling()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        var target = DocPath("gone");
        await store.SaveAsAsync(Named("gone"), target, ct);

        File.Delete(target);

        store.RecentWorkspaces.Single(r => r.Path == Path.GetFullPath(target)).Exists.ShouldBeFalse();
    }

    [Fact]
    public async Task Remove_recent_drops_the_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        var target = DocPath("temp");
        await store.SaveAsAsync(Named("temp"), target, ct);

        await store.RemoveRecentAsync(target, ct);

        store.RecentWorkspaces.ShouldNotContain(r => r.Path == Path.GetFullPath(target));
    }

    [Fact]
    public void New_workspace_resets_current_and_clears_the_path()
    {
        var store = new JsonWorkspaceStore(Path_);
        var first = store.NewWorkspace();

        first.Connections.ShouldBeEmpty();
        first.Id.ShouldNotBeNullOrWhiteSpace();
        store.CurrentPath.ShouldBeNull(); // untitled until Save As
    }

    // ── E3.1 PR-C: dirty tracking + debounced autosave + reload ──────────────

    [Fact]
    public async Task A_zero_debounce_autosave_flushes_synchronously_and_stays_clean()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_); // tests default to a zero debounce

        await store.SaveAsync(Named("clean"), ct);

        store.IsDirty.ShouldBeFalse();
        File.Exists(Path_).ShouldBeTrue();
    }

    [Fact]
    public async Task A_debounced_autosave_marks_dirty_until_it_flushes()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_, TimeSpan.FromMinutes(5)); // never auto-fires during the test
        var dirtyEvents = 0;
        store.DirtyChanged += (_, _) => dirtyEvents++;

        await store.SaveAsync(Named("pending"), ct);

        store.IsDirty.ShouldBeTrue();       // mutation registered, flush still pending
        File.Exists(Path_).ShouldBeFalse(); // not yet written
        dirtyEvents.ShouldBe(1);

        await store.SaveNowAsync(ct);        // explicit Save forces the flush

        store.IsDirty.ShouldBeFalse();
        File.Exists(Path_).ShouldBeTrue();
        dirtyEvents.ShouldBe(2);
    }

    [Fact]
    public async Task A_short_debounce_eventually_autosaves_on_its_own()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_, TimeSpan.FromMilliseconds(20));

        await store.SaveAsync(Named("auto"), ct);

        // The debounced flush runs shortly after; poll briefly for it.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (store.IsDirty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, ct);
        }

        store.IsDirty.ShouldBeFalse();
        File.Exists(Path_).ShouldBeTrue();
    }

    [Fact]
    public async Task An_untitled_workspace_stays_dirty_with_nowhere_to_autosave()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        store.NewWorkspace(); // CurrentPath becomes null

        await store.SaveAsync(Named("untitled-edit"), ct);

        store.IsDirty.ShouldBeTrue(); // no path → cannot autosave; awaits a Save As
    }

    [Fact]
    public async Task Reload_re_reads_the_file_and_discards_in_memory_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        await new JsonWorkspaceStore(Path_).SaveAsync(Named("on-disk"), ct); // seed the file (zero debounce)

        var store = new JsonWorkspaceStore(Path_, TimeSpan.FromMinutes(5));
        await store.SaveAsync(Named("in-memory-only"), ct); // pending edit, never flushed
        store.IsDirty.ShouldBeTrue();
        store.Current.Name.ShouldBe("in-memory-only");

        await store.ReloadAsync(ct);

        store.Current.Name.ShouldBe("on-disk"); // disk state wins
        store.IsDirty.ShouldBeFalse();
    }

    // ── E3.4: export / read-for-merge (FR-164) ───────────────────────────────

    [Fact]
    public async Task Export_writes_a_copy_without_changing_the_active_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);
        await store.SaveAsync(Named("active"), ct);

        var exportPath = System.IO.Path.Combine(_dir, "shared.gcnws.json");
        await store.ExportAsync(Named("shared-copy"), exportPath, ct);

        File.Exists(exportPath).ShouldBeTrue();
        store.CurrentPath.ShouldBe(Path_);                 // active file unchanged
        store.Current.Name.ShouldBe("active");             // in-memory workspace unchanged
        store.RecentWorkspaces.ShouldNotContain(r => r.Path.Contains("shared.gcnws.json")); // not a recent
    }

    [Fact]
    public async Task Read_deserializes_without_opening()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new JsonWorkspaceStore(Path_);

        var otherPath = System.IO.Path.Combine(_dir, "other.gcnws.json");
        await store.ExportAsync(Named("incoming"), otherPath, ct);

        var read = await store.ReadAsync(otherPath, ct);

        read.Name.ShouldBe("incoming");
        store.CurrentPath.ShouldBe(Path_); // reading a file does not open it
        store.Current.Name.ShouldNotBe("incoming");
    }
}
