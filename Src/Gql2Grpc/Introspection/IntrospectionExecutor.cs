using Gql2Grpc.GraphQL;
using Gql2Grpc.Response;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Introspection;

/// <summary>
///     Handles <c>__schema</c>, <c>__type(name:)</c>, and <c>__typename</c> GraphQL introspection
///     selections entirely from the synthesised schema (no RPC).
/// </summary>
/// <remarks>
///     Creates an executor that uses <paramref name="schemaBuilder" /> for schema synthesis and
///     <paramref name="projector" /> for response shaping.
/// </remarks>
public sealed class IntrospectionExecutor(GraphQLSchemaBuilder schemaBuilder, SelectionProjector projector)
{
    /// <summary>The <c>__schema</c> introspection meta-field name.</summary>
    public const string SchemaField = "__schema";

    /// <summary>The <c>__type</c> introspection meta-field name.</summary>
    public const string TypeField = "__type";

    /// <summary>The <c>__typename</c> introspection meta-field name.</summary>
    public const string TypenameField = "__typename";

    private JsonObject? _schemaCache;

    /// <summary>Returns <c>true</c> if <paramref name="name" /> is one of the introspection meta-fields.</summary>
    public static bool IsIntrospectionField(string name) =>
        name is SchemaField or TypeField or TypenameField;

    /// <summary>Executes a single introspection root selection and returns the projected result.</summary>
    public RootFieldResult Execute(ResolvedSelection selection, GraphQLOperationType operationType)
    {
        var errors = new List<GraphQLError>();

        try
        {
            switch (selection.Name)
            {
                case SchemaField:

                {
                    var schema = GetSchema();
                    var projected = projector.Project(schema, selection.Children, null, [selection.ResponseKey], errors);

                    return new RootFieldResult(selection.ResponseKey, projected, errors, false);
                }

                case TypeField:

                {
                    if (!selection.Arguments.TryGetValue("name", out var nameNode) || nameNode is not JsonValue nv || !nv.TryGetValue(out string? typeName) || typeName is null)
                    {
                        errors.Add(new GraphQLError(
                                       "__type requires a 'name' argument of type String.",
                                       [selection.ResponseKey]));

                        return new RootFieldResult(selection.ResponseKey, null, errors, true);
                    }

                    var typeObj = schemaBuilder.FindType(typeName);

                    if (typeObj is null)
                    {
                        return new RootFieldResult(selection.ResponseKey, null, errors, false);
                    }

                    var projected = projector.Project(typeObj, selection.Children, null, [selection.ResponseKey], errors);

                    return new RootFieldResult(selection.ResponseKey, projected, errors, false);
                }

                case TypenameField:

                {
                    var rootName = operationType switch
                    {
                        GraphQLOperationType.Mutation     => "Mutation",
                        GraphQLOperationType.Subscription => "Subscription",
                        _                                 => "Query"
                    };

                    return new RootFieldResult(selection.ResponseKey, JsonValue.Create(rootName), errors, false);
                }

                default:

                    errors.Add(new GraphQLError(
                                   $"Introspection field '{selection.Name}' is not supported.",
                                   [selection.ResponseKey]));

                    return new RootFieldResult(selection.ResponseKey, null, errors, true);
            }
        }
        catch (Exception ex)
        {
            errors.Add(new GraphQLError(ex.Message, [selection.ResponseKey]));

            return new RootFieldResult(selection.ResponseKey, null, errors, true);
        }
    }

    private JsonObject GetSchema() => _schemaCache ??= schemaBuilder.BuildSchema();
}