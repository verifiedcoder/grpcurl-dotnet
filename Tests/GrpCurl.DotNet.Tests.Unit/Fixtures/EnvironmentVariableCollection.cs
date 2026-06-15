namespace GrpCurl.Net.Tests.Unit.Fixtures;

/// <summary>
///     Collection that serializes tests which mutate process-wide environment variables
///     (e.g. proxy variables). Without this, xUnit runs test classes in parallel and the
///     shared <see cref="Environment" /> state would race between them.
/// </summary>
[CollectionDefinition("EnvironmentVariables", DisableParallelization = true)]
public sealed class EnvironmentVariableCollection;
