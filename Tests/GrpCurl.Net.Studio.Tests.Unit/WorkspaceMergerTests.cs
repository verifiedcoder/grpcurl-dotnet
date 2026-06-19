using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="WorkspaceMerger" /> (FR-164): imported items are added (never overwriting),
///     name collisions get an " (imported)" suffix, every imported item gets a fresh id, connection→profile
///     references are remapped, and secrets are reported as needing re-entry rather than copied.
/// </summary>
public sealed class WorkspaceMergerTests
{
    [Fact]
    public void Merging_adds_items_without_touching_the_current_ones()
    {
        var current = new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "local" }]
        };
        var incoming = new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "staging" }],
            Environments = [new WorkspaceEnvironment { Id = "e", Name = "prod" }]
        };

        var (merged, summary) = WorkspaceMerger.Merge(current, incoming);

        merged.Connections.Select(c => c.Name).ShouldBe(["local", "staging"]);
        merged.Environments.Single().Name.ShouldBe("prod");
        summary.Connections.ShouldBe(["staging"]);
        summary.Environments.ShouldBe(["prod"]);
        summary.TotalAdded.ShouldBe(2);
        _ = current.Connections.ShouldHaveSingleItem(); // the source workspace is untouched
    }

    [Fact]
    public void A_name_collision_gets_an_imported_suffix()
    {
        var current = new WorkspaceModel { Connections = [new SavedConnection { Name = "api" }] };
        var incoming = new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "api" }, new SavedConnection { Name = "api" }]
        };

        var (merged, _) = WorkspaceMerger.Merge(current, incoming);

        merged.Connections.Select(c => c.Name).ShouldBe(["api", "api (imported)", "api (imported 2)"]);
    }

    [Fact]
    public void Imported_items_get_fresh_ids()
    {
        var shared = new SavedConnection { Id = "dup", Name = "x" };
        var current = new WorkspaceModel { Connections = [new SavedConnection { Id = "dup", Name = "y" }] };
        var incoming = new WorkspaceModel { Connections = [shared] };

        var (merged, _) = WorkspaceMerger.Merge(current, incoming);

        merged.Connections.Select(c => c.Id).Distinct().Count().ShouldBe(2); // no id collision
        shared.Id.ShouldNotBe("dup"); // the imported item was re-identified
    }

    [Fact]
    public void Connection_profile_references_are_remapped_to_the_imported_profiles()
    {
        var profile = new TlsProfile { Id = "p-old", Name = "mtls" };
        var incoming = new WorkspaceModel
        {
            TlsProfiles = [profile],
            Connections = [new SavedConnection { Name = "svc", TlsProfileId = "p-old" }]
        };

        var (merged, _) = WorkspaceMerger.Merge(new WorkspaceModel(), incoming);

        var importedProfile = merged.TlsProfiles.Single();
        importedProfile.Id.ShouldNotBe("p-old");
        merged.Connections.Single().TlsProfileId.ShouldBe(importedProfile.Id); // re-pointed at the new id
    }

    [Fact]
    public void A_reference_to_an_unimported_profile_is_dropped()
    {
        var incoming = new WorkspaceModel
        {
            Connections = [new SavedConnection { Name = "svc", TlsProfileId = "not-in-this-file" }]
        };

        var (merged, _) = WorkspaceMerger.Merge(new WorkspaceModel(), incoming);

        merged.Connections.Single().TlsProfileId.ShouldBeNull(); // not silently bound to a local profile
    }

    [Fact]
    public void Secrets_are_counted_for_re_entry_not_copied()
    {
        var incoming = new WorkspaceModel
        {
            TlsProfiles = [new TlsProfile { Name = "mtls", ClientCertPasswordSecretRef = "ref-1" }],
            Environments =
            [
                new WorkspaceEnvironment
                {
                    Id = "e", Name = "prod",
                    Variables =
                    [
                        new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("h") },
                        new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("ref-2") }
                    ]
                }
            ]
        };

        var (_, summary) = WorkspaceMerger.Merge(new WorkspaceModel(), incoming);

        summary.SecretsToReenter.ShouldBe(2); // the PKCS12 password + the secret variable
        summary.Describe().ShouldContain("re-entered");

        // SEC-041: each dangling secret is listed by name + keyref so it can be supplied inline on import.
        summary.MissingSecrets.Select(m => m.KeyRef).ShouldBe(["ref-1", "ref-2"], ignoreOrder: true);
        summary.MissingSecrets.ShouldContain(m => m.DisplayName.Contains("mtls") && m.DisplayName.Contains("password"));
        summary.MissingSecrets.ShouldContain(m => m.DisplayName.Contains("TOKEN"));
    }

    [Fact]
    public void An_empty_import_summarises_as_empty()
    {
        var (_, summary) = WorkspaceMerger.Merge(new WorkspaceModel(), new WorkspaceModel());

        summary.IsEmpty.ShouldBeTrue();
        summary.TotalAdded.ShouldBe(0);
        summary.Describe().ShouldContain("nothing to import");
    }
}
