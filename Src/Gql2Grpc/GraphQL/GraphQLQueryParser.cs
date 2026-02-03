using GraphQLParser;
using GraphQLParser.AST;

namespace Gql2Grpc.GraphQL;

/// <summary>
///     Parses GraphQL queries and extracts the information needed for gRPC translation.
/// </summary>
public static class GraphQLQueryParser
{
    /// <summary>
    ///     Parses a GraphQL query string and extracts query information.
    /// </summary>
    public static ParsedQuery Parse(string queryString, Dictionary<string, string>? variables = null)
    {
        var document = Parser.Parse(queryString);

        // Find the query operation
        var operation = document.Definitions
                                .OfType<GraphQLOperationDefinition>()
                                .FirstOrDefault(d => d.Operation == OperationType.Query);

        if (operation is null)
        {
            throw new ArgumentException("No query operation found in GraphQL document");
        }

        // Extract the root selection fields
        var queries = new List<QueryField>();

        foreach (var selection in operation.SelectionSet.Selections)
        {
            if (selection is GraphQLField field)
            {
                queries.Add(ExtractQueryField(field, variables));
            }
        }

        return new ParsedQuery(queries);
    }

    private static QueryField ExtractQueryField(GraphQLField field, Dictionary<string, string>? variables)
    {
        var name = field.Name.StringValue;
        var arguments = new Dictionary<string, object?>();
        var selections = new List<string>();

        // Extract arguments
        if (field.Arguments is not null)
        {
            foreach (var arg in field.Arguments)
            {
                var argName = arg.Name.StringValue;
                var argValue = ExtractArgumentValue(arg.Value, variables);
                arguments[argName] = argValue;
            }
        }

        // Extract field selections (flattened for simplicity)
        if (field.SelectionSet is not null)
        {
            ExtractSelections(field.SelectionSet, "", selections);
        }

        return new QueryField(name, arguments, selections);
    }

    private static object? ExtractArgumentValue(GraphQLValue value, Dictionary<string, string>? variables)
        => value switch
        {
            GraphQLStringValue sv  => sv.Value.ToString(),
            GraphQLIntValue iv     => int.Parse(iv.Value.ToString()),
            GraphQLFloatValue fv   => double.Parse(fv.Value.ToString()),
            GraphQLBooleanValue bv => bv.Value,
            GraphQLNullValue       => null,
            GraphQLVariable v      => variables?.GetValueOrDefault(v.Name.StringValue),
            GraphQLEnumValue ev    => ev.Name.StringValue,
            GraphQLListValue lv    => lv.Values?.Select(v => ExtractArgumentValue(v, variables)).ToList(),
            GraphQLObjectValue ov  => ExtractObjectValue(ov, variables),
            _                      => value.ToString()
        };

    private static Dictionary<string, object?> ExtractObjectValue(GraphQLObjectValue obj, Dictionary<string, string>? variables)
    {
        var result = new Dictionary<string, object?>();

        if (obj.Fields is null)
        {
            return result;
        }

        foreach (var field in obj.Fields)
        {
            result[field.Name.StringValue] = ExtractArgumentValue(field.Value, variables);
        }

        return result;
    }

    private static void ExtractSelections(GraphQLSelectionSet selectionSet, string prefix, List<string> selections)
    {
        foreach (var selection in selectionSet.Selections)
        {
            if (selection is not GraphQLField field)
            {
                continue;
            }

            var fieldPath = string.IsNullOrEmpty(prefix)
                ? field.Name.StringValue
                : $"{prefix}.{field.Name.StringValue}";

            if (field.SelectionSet is not null && field.SelectionSet.Selections.Count > 0)
            {
                // Has nested selections - recurse
                ExtractSelections(field.SelectionSet, fieldPath, selections);
            }
            else
            {
                // Leaf field
                selections.Add(fieldPath);
            }
        }
    }
}

/// <summary>
///     Represents a parsed GraphQL query.
/// </summary>
public record ParsedQuery(IReadOnlyList<QueryField> Fields);

/// <summary>
///     Represents a single query field with its arguments and selections.
/// </summary>
public record QueryField(
    string Name,
    IReadOnlyDictionary<string, object?> Arguments,
    IReadOnlyList<string> Selections);