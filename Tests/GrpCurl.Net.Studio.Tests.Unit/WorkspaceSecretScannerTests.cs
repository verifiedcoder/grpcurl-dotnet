using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>SEC-034: the save-time scanner flags secret literals (sensitive-header + secret-value-in-plain-field).</summary>
public sealed class WorkspaceSecretScannerTests
{
    private static WorkspaceModel With(params SavedConnection[] connections)
        => new() { Name = "W", Connections = [.. connections] };

    [Fact]
    public void A_sensitive_header_with_a_literal_value_is_flagged()
    {
        var ws = With(new SavedConnection
        {
            Name = "prod", Address = "h:1",
            ReflectionHeaders = [new HeaderEntry { Name = "authorization", Value = "Bearer abc123" }]
        });

        var leaks = WorkspaceSecretScanner.Scan(ws, []);

        var leak = leaks.ShouldHaveSingleItem();
        leak.Kind.ShouldBe(SecretLeakKind.SensitiveHeaderLiteral);
        leak.Location.ShouldContain("authorization");
    }

    [Fact]
    public void A_sensitive_header_that_references_a_variable_is_clean()
    {
        var ws = With(new SavedConnection
        {
            Name = "prod", Address = "h:1",
            ReflectionHeaders = [new HeaderEntry { Name = "authorization", Value = "Bearer ${TOKEN}" }]
        });

        WorkspaceSecretScanner.Scan(ws, []).ShouldBeEmpty();
    }

    [Fact]
    public void A_plain_field_matching_a_resolved_secret_value_is_flagged()
    {
        var ws = With(new SavedConnection { Name = "prod", Address = "h:1", Notes = "hunter2" });

        var leak = WorkspaceSecretScanner.Scan(ws, ["hunter2"]).ShouldHaveSingleItem();

        leak.Kind.ShouldBe(SecretLeakKind.SecretValueInPlainField);
        leak.Location.ShouldContain("notes");
    }

    [Fact]
    public void A_non_sensitive_header_holding_a_secret_value_is_flagged()
    {
        var ws = With(new SavedConnection
        {
            Name = "prod", Address = "h:1",
            ReflectionHeaders = [new HeaderEntry { Name = "x-trace", Value = "hunter2" }]
        });

        WorkspaceSecretScanner.Scan(ws, ["hunter2"]).ShouldHaveSingleItem()
            .Kind.ShouldBe(SecretLeakKind.SecretValueInPlainField);
    }

    [Fact]
    public void A_saved_request_body_matching_a_secret_value_is_flagged()
    {
        var ws = new WorkspaceModel
        {
            Name = "W",
            SavedRequests = [new SavedRequest { Name = "login", Body = "{ \"pw\": \"hunter2\" }" }]
        };

        // Exact-match scan: only flags when the whole field equals the secret value.
        WorkspaceSecretScanner.Scan(ws, ["{ \"pw\": \"hunter2\" }"]).ShouldHaveSingleItem()
            .Location.ShouldContain("login");
    }

    [Fact]
    public void A_secret_typed_variable_is_not_itself_a_leak()
    {
        var ws = new WorkspaceModel
        {
            Name = "W",
            Environments =
            [
                new WorkspaceEnvironment
                {
                    Name = "prod",
                    Variables = [new EnvironmentVariable { Name = "TOKEN", Value = StringOrSecret.Secret("studio/v1/x") }]
                }
            ]
        };

        WorkspaceSecretScanner.Scan(ws, ["any"]).ShouldBeEmpty();
    }

    [Fact]
    public void A_clean_workspace_yields_no_leaks()
    {
        var ws = With(new SavedConnection
        {
            Name = "prod", Address = "h:1", Notes = "harmless",
            ReflectionHeaders = [new HeaderEntry { Name = "authorization", Value = "${TOKEN}" }]
        });

        WorkspaceSecretScanner.Scan(ws, ["a-secret-not-present-anywhere"]).ShouldBeEmpty();
    }
}
