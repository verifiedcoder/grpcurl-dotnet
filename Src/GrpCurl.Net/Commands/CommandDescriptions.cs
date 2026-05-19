namespace GrpCurl.Net.Commands;

internal static class CommandDescriptions
{
    public const string List =
        "List services or methods exposed by a gRPC server (via reflection) or a protoset file.\n\n" +
        "Examples:\n" +
        "  grpcurl.net list --plaintext localhost:9090\n" +
        "  grpcurl.net list --plaintext --output json localhost:9090\n" +
        "  grpcurl.net list --plaintext localhost:9090 my.package.Service\n" +
        "  grpcurl.net list --protoset api.protoset my.package.Service\n" +
        "  grpcurl.net list --plaintext -H 'authorization: Bearer ${TOKEN}' localhost:9090";

    public const string Describe =
        "Describe a service, message, enum, or method.\n\n" +
        "Examples:\n" +
        "  grpcurl.net describe --plaintext localhost:9090 my.package.Service\n" +
        "  grpcurl.net describe --plaintext --output json localhost:9090 my.package.Service\n" +
        "  grpcurl.net describe --plaintext --msg-template localhost:9090 my.package.Request\n" +
        "  grpcurl.net describe --protoset api.protoset my.package.Service";

    public const string Invoke =
        "Invoke a gRPC method (unary, server-streaming, client-streaming, or bidirectional).\n\n" +
        "Recommendations for AI agents and scripts:\n" +
        "  - Always set --max-time so a hung server cannot block forever (e.g., --max-time 30s).\n" +
        "  - Use --output json for stable, machine-readable response and error envelopes.\n" +
        "  - --data accepts inline JSON, '@' for stdin, a JSON array, or concatenated objects '{...}{...}'.\n" +
        "  - Header values may reference environment variables as ${VAR_NAME}.\n\n" +
        "Examples:\n" +
        "  grpcurl.net invoke --plaintext localhost:9090 my.pkg.Svc/Get -d '{\"id\":1}'\n" +
        "  grpcurl.net invoke --plaintext --output json --max-time 30s localhost:9090 my.pkg.Svc/Get -d '{}'\n" +
        "  echo '{\"id\":1}' | grpcurl.net invoke --plaintext localhost:9090 my.pkg.Svc/Get -d @\n" +
        "  grpcurl.net invoke --plaintext localhost:9090 my.pkg.Svc/Stream -d '[{\"a\":1},{\"a\":2}]'\n" +
        "  grpcurl.net invoke --cacert ca.pem --cert client.pem --key client-key.pem host:443 svc/Method -d '{}'\n" +
        "  grpcurl.net invoke --plaintext -H 'authorization: Bearer ${TOKEN}' host:9090 svc/Method -d '{}'";
}