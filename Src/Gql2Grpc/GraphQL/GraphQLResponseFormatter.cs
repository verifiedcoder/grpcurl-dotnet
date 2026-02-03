using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gql2Grpc.GraphQL;

/// <summary>
///     Formats gRPC responses as GraphQL JSON responses.
/// </summary>
public static class GraphQLResponseFormatter
{
    /// <summary>
    ///     Wraps a gRPC response in GraphQL response format.
    /// </summary>
    /// <param name="queryFieldName">The GraphQL query field name.</param>
    /// <param name="grpcResponseJson">The raw gRPC response JSON.</param>
    /// <returns>A GraphQL-formatted response JSON string.</returns>
    public static string FormatResponse(string queryFieldName, string grpcResponseJson)
    {
        try
        {
            // Parse the gRPC response
            var grpcResponse = JsonNode.Parse(grpcResponseJson);

            // Build GraphQL response wrapper
            var graphqlResponse = new JsonObject
            {
                ["data"] = new JsonObject
                {
                    [queryFieldName] = grpcResponse
                }
            };

            return graphqlResponse.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException ex)
        {
            return FormatError($"Failed to parse gRPC response: {ex.Message}");
        }
    }

    /// <summary>
    ///     Formats multiple gRPC responses for a multi-query GraphQL request.
    /// </summary>
    public static string FormatResponses(IReadOnlyList<(string QueryFieldName, string GrpcResponseJson)> responses)
    {
        var dataObject = new JsonObject();

        foreach (var (queryFieldName, grpcResponseJson) in responses)
        {
            try
            {
                var grpcResponse = JsonNode.Parse(grpcResponseJson);

                dataObject[queryFieldName] = grpcResponse;
            }
            catch (JsonException)
            {
                // Include error for this specific field
                dataObject[queryFieldName] = null;
            }
        }

        var graphqlResponse = new JsonObject
        {
            ["data"] = dataObject
        };

        return graphqlResponse.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    ///     Formats an error response in GraphQL format.
    /// </summary>
    public static string FormatError(string message, string? path = null)
    {
        var error = new JsonObject
        {
            ["message"] = message
        };

        if (path is not null)
        {
            error["path"] = new JsonArray(path);
        }

        var graphqlResponse = new JsonObject
        {
            ["data"] = null,
            ["errors"] = new JsonArray(error)
        };

        return graphqlResponse.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    ///     Formats a partial response with data and errors.
    /// </summary>
    public static string FormatPartialResponse(
        IReadOnlyList<(string QueryFieldName, string GrpcResponseJson)> responses,
        IReadOnlyList<(string Path, string Message)> errors)
    {
        var dataObject = new JsonObject();

        foreach (var (queryFieldName, grpcResponseJson) in responses)
        {
            try
            {
                var grpcResponse = JsonNode.Parse(grpcResponseJson);
                dataObject[queryFieldName] = grpcResponse;
            }
            catch
            {
                dataObject[queryFieldName] = null;
            }
        }

        var graphqlResponse = new JsonObject
        {
            ["data"] = dataObject
        };

        if (errors.Count <= 0)
        {
            return graphqlResponse.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        var errorsArray = new JsonArray();

        foreach (var (path, message) in errors)
        {
            errorsArray.Add(new JsonObject
            {
                ["message"] = message,
                ["path"] = new JsonArray(path)
            });
        }

        graphqlResponse["errors"] = errorsArray;

        return graphqlResponse.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}