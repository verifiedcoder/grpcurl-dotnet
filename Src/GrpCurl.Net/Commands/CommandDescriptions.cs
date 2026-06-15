namespace GrpCurl.Net.Commands;

internal static class CommandDescriptions
{
    public const string List =
        "List services or methods exposed by a gRPC server (via reflection) or a protoset file.\n\n" +
        "Recommendations for AI agents and scripts:\n" +
        "  - Always set --max-time for reflection-backed discovery so a hung server cannot block forever.\n" +
        "  - Use --output json for stable, machine-readable service and method lists.\n\n" +
        "Examples:\n" +
        "  grpcn list --plaintext --max-time 10s localhost:9090\n" +
        "  grpcn list --plaintext --max-time 10s --output json localhost:9090\n" +
        "  grpcn list --plaintext --max-time 10s localhost:9090 my.package.Service\n" +
        "  grpcn list --max-time 10s --protoset api.protoset my.package.Service\n" +
        "  grpcn list --plaintext --max-time 10s -H 'authorization: Bearer ${TOKEN}' localhost:9090";

    public const string Describe =
        "Describe a service, message, enum, or method.\n\n" +
        "Recommendations for AI agents and scripts:\n" +
        "  - Always set --max-time for reflection-backed discovery so a hung server cannot block forever.\n" +
        "  - Use --output json when parsing service, method, or message descriptors programmatically.\n\n" +
        "Examples:\n" +
        "  grpcn describe --plaintext --max-time 10s localhost:9090 my.package.Service\n" +
        "  grpcn describe --plaintext --max-time 10s --output json localhost:9090 my.package.Service\n" +
        "  grpcn describe --plaintext --max-time 10s --msg-template localhost:9090 my.package.Request\n" +
        "  grpcn describe --max-time 10s --protoset api.protoset my.package.Service";

    public const string Invoke =
        "Invoke a gRPC method (unary, server-streaming, client-streaming, or bidirectional).\n\n" +
        "Recommendations for AI agents and scripts:\n" +
        "  - Always set --max-time so a hung server cannot block forever (e.g., --max-time 30s).\n" +
        "  - Use --output json for stable, machine-readable response and error envelopes.\n" +
        "  - --data accepts inline JSON, '@' for stdin, a JSON array, or concatenated objects '{...}{...}'.\n" +
        "  - Header values may reference environment variables as ${VAR_NAME}.\n\n" +
        "Examples:\n" +
        "  grpcn invoke --plaintext --max-time 30s localhost:9090 my.pkg.Svc/Get -d '{\"id\":1}'\n" +
        "  grpcn invoke --plaintext --output json --max-time 30s localhost:9090 my.pkg.Svc/Get -d '{}'\n" +
        "  echo '{\"id\":1}' | grpcn invoke --plaintext --max-time 30s --max-stdin-bytes 1048576 localhost:9090 my.pkg.Svc/Get -d @\n" +
        "  grpcn invoke --plaintext --max-time 30s localhost:9090 my.pkg.Svc/Stream -d '[{\"a\":1},{\"a\":2}]'\n" +
        "  grpcn invoke --max-time 30s --cacert ca.pem --cert client.pem --key client-key.pem host:443 svc/Method -d '{}'\n" +
        "  grpcn invoke --plaintext --max-time 30s -H 'authorization: Bearer ${TOKEN}' host:9090 svc/Method -d '{}'";
}
