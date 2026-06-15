namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>Which kind of symbol a <see cref="SymbolDescription" /> describes (FR-050).</summary>
public enum SymbolKind
{
    Service,
    Method,
    Message,
    Enum
}

/// <summary>A message field's cardinality, shown in the field table (FR-050).</summary>
public enum FieldLabel
{
    Optional,
    Repeated,
    Map
}

/// <summary>
///     A UI-free structured description of a gRPC symbol, rendered as a describe tab (FR-050).
///     Discriminated by <see cref="Kind" />; the concrete subtypes carry the per-kind facts.
/// </summary>
public abstract record SymbolDescription(SymbolKind Kind, string FullName, string Name, string? SourceFile);

/// <summary>A method as it appears in a service's method table (FR-050).</summary>
public sealed record MethodSummary(string Name, string FullName, StreamingShape Shape, TypeRef InputType, TypeRef OutputType)
{
    /// <summary>Streaming-shape badge (U/SS/CS/BD) for the method table.</summary>
    public string Badge => Shape.Badge();
}

/// <summary>A service: its method table (FR-050).</summary>
public sealed record ServiceDescription(
    string FullName,
    string Name,
    string? SourceFile,
    IReadOnlyList<MethodSummary> Methods)
    : SymbolDescription(SymbolKind.Service, FullName, Name, SourceFile);

/// <summary>A method: full signature, input/output type links, parent service, and request template (FR-050/052).</summary>
public sealed record MethodDescription(
    string FullName,
    string Name,
    string? SourceFile,
    StreamingShape Shape,
    TypeRef InputType,
    TypeRef OutputType,
    TypeRef ParentService,
    string TemplateJson)
    : SymbolDescription(SymbolKind.Method, FullName, Name, SourceFile);

/// <summary>A single field in a message's field table (FR-050).</summary>
/// <param name="TypeDisplay">Human-readable proto type (e.g. <c>string</c>, <c>.pkg.Foo</c>, <c>map&lt;string, .pkg.Bar&gt;</c>).</param>
/// <param name="Link">The navigable message/enum this field references, or <see langword="null" /> for scalars.</param>
public sealed record FieldDescription(
    string Name,
    int Number,
    string TypeDisplay,
    TypeRef? Link,
    FieldLabel Label,
    string? OneofName)
{
    /// <summary>Cardinality prefix for the field table (<c>repeated </c>/<c>map </c>/empty).</summary>
    public string LabelText => Label switch
    {
        FieldLabel.Repeated => "repeated ",
        FieldLabel.Map => "map ",
        _ => string.Empty
    };
}

/// <summary>A message: field table, nested type links, and request template (FR-050/052).</summary>
public sealed record MessageDescription(
    string FullName,
    string Name,
    string? SourceFile,
    IReadOnlyList<FieldDescription> Fields,
    IReadOnlyList<TypeRef> NestedTypes,
    string TemplateJson)
    : SymbolDescription(SymbolKind.Message, FullName, Name, SourceFile);

/// <summary>A single enum value (FR-050).</summary>
public sealed record EnumValue(string Name, int Number);

/// <summary>An enum: its name/number value table (FR-050).</summary>
public sealed record EnumDescription(
    string FullName,
    string Name,
    string? SourceFile,
    IReadOnlyList<EnumValue> Values)
    : SymbolDescription(SymbolKind.Enum, FullName, Name, SourceFile);
