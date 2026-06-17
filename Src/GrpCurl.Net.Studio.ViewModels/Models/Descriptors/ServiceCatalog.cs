namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>A single method on a service (FR-020/021).</summary>
/// <param name="Name">The method's simple name (e.g. <c>UnaryCall</c>).</param>
/// <param name="FullName">The invocation grammar name <c>pkg.Service/Method</c> (FR-024 Copy full name).</param>
/// <param name="Shape">The streaming shape badge.</param>
/// <param name="InputType">Fully-qualified request message type.</param>
/// <param name="OutputType">Fully-qualified response message type.</param>
public sealed record ServiceMethod(
    string Name,
    string FullName,
    StreamingShape Shape,
    string InputType,
    string OutputType,
    bool Deprecated = false);

/// <summary>A service node and its methods (FR-020).</summary>
/// <param name="FullName">Fully-qualified service name (e.g. <c>testing.TestService</c>).</param>
/// <param name="Methods">Methods in descriptor (file) order.</param>
/// <param name="Deprecated">FR-059: the service carries <c>option deprecated = true</c>.</param>
public sealed record ServiceEntry(string FullName, IReadOnlyList<ServiceMethod> Methods, bool Deprecated = false);

/// <summary>
///     The descriptor set browsable in the explorer, plus any non-fatal warnings raised while
///     loading (collected as data rather than written to stdio).
/// </summary>
public sealed record ServiceCatalog(IReadOnlyList<ServiceEntry> Services, IReadOnlyList<string> Warnings)
{
    /// <summary>All message and enum types in the active set, grouped-by-package material for the Types branch (FR-022).</summary>
    public IReadOnlyList<TypeEntry> Types { get; init; } = [];

    /// <summary>Descriptor files in the loaded set (FR-048 load metadata).</summary>
    public int FileCount { get; init; }

    /// <summary>Resolved symbols — services + methods + message/enum types (FR-048).</summary>
    public int SymbolCount { get; init; }

    /// <summary>Wall-clock time the descriptor load/compile took (FR-048).</summary>
    public TimeSpan LoadDuration { get; init; }

    public static ServiceCatalog Empty { get; } = new([], []);
}
