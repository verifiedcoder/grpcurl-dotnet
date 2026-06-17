namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     A named environment in the workspace (FR-130, SPEC-040 §3.2): an ordered list of variables that
///     resolve <c>${VAR}</c> placeholders ahead of the OS environment. Variable names are case-sensitive
///     identifiers; values are plain or secret-typed (the secret value lives only in <c>ISecretStore</c>).
/// </summary>
public sealed class WorkspaceEnvironment
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<EnvironmentVariable> Variables { get; set; } = [];

    public WorkspaceEnvironment Copy() => new()
    {
        Id = Id,
        Name = Name,
        Variables = Variables.Select(v => v.Copy()).ToList()
    };
}

/// <summary>One environment variable (FR-130): a case-sensitive <see cref="Name" /> and a plain/secret value.</summary>
public sealed class EnvironmentVariable
{
    public string Name { get; set; } = string.Empty;

    public StringOrSecret Value { get; set; } = StringOrSecret.Plain(string.Empty);

    public bool IsSecret => Value.IsSecret;

    public EnvironmentVariable Copy() => new() { Name = Name, Value = Value };
}
