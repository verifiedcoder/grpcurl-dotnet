namespace Gql2Grpc.Commands;

internal static class CommandDescriptions
{
    public const string Root =
        "GraphQL-to-gRPC bridge — execute GraphQL operations against gRPC services.\n\n" +
        "Recommendations for AI agents and scripts:\n" +
        "  - Always set --max-time so a hung upstream cannot block forever (e.g., --max-time 30s).\n" +
        "  - Output is always a GraphQL response envelope on stdout. Errors live in the envelope's\n" +
        "    'errors[]' array with 'extensions.code' (and 'extensions.grpcStatus'/'grpcStatusCode'\n" +
        "    for upstream gRPC failures). Subscriptions emit one envelope per line (NDJSON).\n" +
        "  - Verbose diagnostics go to stderr; stdout carries only the envelope(s).\n" +
        "  - Header values support ${VAR_NAME} environment-variable expansion.\n" +
        "  - Exit code: 0 success; 2 usage; 3 schema/file; 4 network; 5 timeout; 64+gRPC code\n" +
        "    for upstream errors; 130 for Ctrl+C; 1 for anything else.\n\n" +
        "Examples:\n" +
        "  gql2grpc --plaintext --max-time 30s localhost:9090 'query { ping }'\n" +
        "  gql2grpc --plaintext --mapping schema.yaml --max-time 30s localhost:9090 \\\n" +
        "    'query Hello($n: Int!) { unaryCall(input: { responseSize: $n }) { payload { body } } }' \\\n" +
        "    --var n=10\n" +
        "  gql2grpc --plaintext --max-time 30s --file query.graphql --variables-file vars.json localhost:9090\n" +
        "  gql2grpc --plaintext --max-time 30s -H 'authorization: Bearer ${TOKEN}' localhost:9090 'query { me { id } }'\n" +
        "  gql2grpc --protoset api.protoset --max-time 30s --default-service my.pkg.Service \\\n" +
        "    localhost:9090 'subscription { stream { value } }'";
}
