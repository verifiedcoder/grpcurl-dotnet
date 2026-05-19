using Google.Protobuf.Reflection;

namespace Gql2Grpc.Introspection;

/// <summary>
///     Proto → GraphQL type-name rules. These are deterministic so that both the schema builder and
///     the request/response pipelines can agree on what a proto type is called in GraphQL.
/// </summary>
internal static class TypeMappings
{
    public const string StringTypeName = "String";
    public const string BooleanTypeName = "Boolean";
    public const string IntTypeName = "Int";
    public const string FloatTypeName = "Float";
    public const string IdTypeName = "ID";

    /// <summary>Custom scalars that Gql2Grpc exposes for types that have no native GraphQL mapping.</summary>
    public static readonly string[] CustomScalars =
    [
        "AnyScalar",
        "JsonScalar"
    ];

    private static readonly Dictionary<string, string> WellKnownMessageTypes = new(StringComparer.Ordinal)
    {
        ["google.protobuf.Timestamp"] = StringTypeName,
        ["google.protobuf.Duration"] = StringTypeName,
        ["google.protobuf.FieldMask"] = StringTypeName,
        ["google.protobuf.StringValue"] = StringTypeName,
        ["google.protobuf.BytesValue"] = StringTypeName,
        ["google.protobuf.BoolValue"] = BooleanTypeName,
        ["google.protobuf.Int32Value"] = IntTypeName,
        ["google.protobuf.UInt32Value"] = StringTypeName,
        ["google.protobuf.Int64Value"] = StringTypeName,
        ["google.protobuf.UInt64Value"] = StringTypeName,
        ["google.protobuf.FloatValue"] = FloatTypeName,
        ["google.protobuf.DoubleValue"] = FloatTypeName,
        ["google.protobuf.Empty"] = "Boolean", // placeholder scalar
        ["google.protobuf.Any"] = "AnyScalar",
        ["google.protobuf.Struct"] = "JsonScalar",
        ["google.protobuf.Value"] = "JsonScalar",
        ["google.protobuf.ListValue"] = "JsonScalar"
    };

    public static bool TryGetWellKnownScalar(string fullyQualifiedMessageName, out string scalarName) => WellKnownMessageTypes.TryGetValue(fullyQualifiedMessageName, out scalarName!);

    /// <summary>Canonical GraphQL scalar for a primitive proto field type.</summary>
    public static string ScalarFor(FieldType fieldType) => fieldType switch
    {
        FieldType.String                                          => StringTypeName,
        FieldType.Bool                                            => BooleanTypeName,
        FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => IntTypeName,
        FieldType.UInt32 or FieldType.Fixed32                     => StringTypeName,
        FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => StringTypeName,
        FieldType.UInt64 or FieldType.Fixed64                     => StringTypeName,
        FieldType.Float or FieldType.Double                       => FloatTypeName,
        _                                                         => StringTypeName
    };

    /// <summary>Converts a proto message's simple name to the GraphQL ObjectType name, honouring overrides.</summary>
    // ReSharper disable once UnusedMember.Global
    // ReSharper disable once InconsistentNaming
    public static string GraphQLObjectName(
        MessageDescriptor descriptor,
        IReadOnlyDictionary<string, string> typeOverrides)
        => typeOverrides.TryGetValue(descriptor.FullName, out var overridden)
            ? overridden
            : descriptor.Name;

    // ReSharper disable once UnusedMember.Global
    // ReSharper disable once InconsistentNaming
    public static string GraphQLEnumName(
        EnumDescriptor descriptor,
        IReadOnlyDictionary<string, string> typeOverrides)
        => typeOverrides.TryGetValue(descriptor.FullName, out var overridden)
            ? overridden
            : descriptor.Name;
}