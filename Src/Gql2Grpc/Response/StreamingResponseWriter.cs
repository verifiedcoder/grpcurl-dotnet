using System.Text.Json.Nodes;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Response;

/// <summary>
/// Emits subscription (server-streaming) output as newline-delimited GraphQL envelopes.
/// Each message becomes one line containing a self-contained <c>{"data":{"&lt;field&gt;":&lt;value&gt;}}</c>
/// payload. A terminating error envelope is written if the stream fails.
/// </summary>
public sealed class StreamingResponseWriter
{
    private readonly TextWriter _output;

    /// <summary>Wraps the supplied <paramref name="output"/> writer (typically <see cref="Console.Out"/>).</summary>
    public StreamingResponseWriter(TextWriter output)
    {
        _output = output;
    }

    /// <summary>Emits a single data envelope line for the given response key and payload.</summary>
    public void WriteData(string responseKey, JsonNode? payload)
    {
        var envelope = new JsonObject
        {
            ["data"] = new JsonObject
            {
                [responseKey] = payload?.DeepClone()
            }
        };

        _output.WriteLine(envelope.ToJsonString());
    }

    /// <summary>Emits a single GraphQL error envelope line and continues (the stream is not closed by this call).</summary>
    public void WriteError(GraphQLError error)
    {
        var envelope = GraphQLResponseBuilder.BuildSingleError(error);
        _output.WriteLine(envelope.ToJsonString());
    }
}
