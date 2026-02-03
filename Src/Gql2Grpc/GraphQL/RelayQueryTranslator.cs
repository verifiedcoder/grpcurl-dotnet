using System.Text.Json;

namespace Gql2Grpc.GraphQL;

/// <summary>
///     Translates GraphQL Relay-style queries to gRPC request format.
/// </summary>
public class RelayQueryTranslator
{
    private readonly Dictionary<string, MethodMapping> _methodMappings = new(StringComparer.OrdinalIgnoreCase);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <summary>
    ///     Registers a mapping from a GraphQL query field to a gRPC method.
    /// </summary>
    public void RegisterMethod(string graphqlFieldName, string grpcServiceName, string grpcMethodName, MethodType methodType) 
        => _methodMappings[graphqlFieldName] = new MethodMapping(grpcServiceName, grpcMethodName, methodType);

    /// <summary>
    ///     Translates a parsed GraphQL query field to a gRPC request.
    /// </summary>
    public GrpcRequest? Translate(QueryField queryField)
    {
        if (!_methodMappings.TryGetValue(queryField.Name, out var mapping))
        {
            return null;
        }

        var requestJson = mapping.Type switch
        {
            MethodType.List   => BuildListRequest(queryField),
            MethodType.Lookup => BuildLookupRequest(queryField),
            _                 => "{}"
        };

        return new GrpcRequest(
            $"{mapping.ServiceName}/{mapping.MethodName}",
            requestJson,
            queryField.Name,
            queryField.Selections);
    }

    private string BuildListRequest(QueryField queryField)
    {
        var request = new Dictionary<string, object?>();

        // Map GraphQL Relay arguments to gRPC GraphQLRelayRequest fields
        foreach (var (argName, argValue) in queryField.Arguments)
        {
            var grpcFieldName = MapArgumentName(argName);
            var grpcValue = MapArgumentValue(argName, argValue);

            if (grpcFieldName is not null && grpcValue is not null)
            {
                request[grpcFieldName] = grpcValue;
            }
        }

        return JsonSerializer.Serialize(request, _jsonOptions);
    }

    private string BuildLookupRequest(QueryField queryField)
    {
        var request = new Dictionary<string, object?>();

        // For lookup methods, map the 'ids' argument
        if (queryField.Arguments.TryGetValue("ids", out var idsValue) && idsValue is List<object?> ids)
        {
            request["ids"] = ids;
        }
        else if (queryField.Arguments.TryGetValue("id", out var idValue))
        {
            // Single ID lookup
            request["ids"] = new[] { idValue };
        }

        return JsonSerializer.Serialize(request, _jsonOptions);
    }

    private static string? MapArgumentName(string graphqlArgName)
    {
        // Map GraphQL Relay argument names to gRPC field names
        return graphqlArgName.ToLowerInvariant() switch
        {
            "first"     => "page_size",
            "last"      => "page_size", // Backwards pagination uses same field
            "after"     => "after_cursor",
            "before"    => "before_cursor",
            "filter"    => "filter",
            "orderby"   => "order_by",
            "order_by"  => "order_by",
            "pagesize"  => "page_size",
            "page_size" => "page_size",
            _           => null // Unknown arguments are skipped
        };
    }

    private static object? MapArgumentValue(string argName, object? value)
    {
        if (value is null)
        {
            return null;
        }

        // Handle cursor encoding (GraphQL uses base64 strings, gRPC uses bytes)
        if (argName.Equals("after", StringComparison.OrdinalIgnoreCase) ||
            argName.Equals("before", StringComparison.OrdinalIgnoreCase))
        {
            // Pass through as string - gRPC side expects base64
            return value;
        }

        return value;
    }
}

/// <summary>
///     Represents a gRPC method mapping.
/// </summary>
public record MethodMapping(string ServiceName, string MethodName, MethodType Type);

/// <summary>
///     The type of gRPC method for GraphQL translation purposes.
/// </summary>
public enum MethodType
{
    /// <summary>List operation with Relay pagination.</summary>
    List,

    /// <summary>Lookup operation by IDs.</summary>
    Lookup
}

/// <summary>
///     Represents a translated gRPC request.
/// </summary>
public record GrpcRequest(
    string FullMethodName,
    string RequestJson,
    string QueryFieldName,
    IReadOnlyList<string> SelectedFields);