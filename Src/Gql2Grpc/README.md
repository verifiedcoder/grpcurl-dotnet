# Gql2Grpc

A GraphQL-to-gRPC bridge CLI. Parses a GraphQL document, translates each root operation into a gRPC method invocation, and emits a spec-compliant GraphQL response envelope, or NDJSON for subscriptions.

Full reference documentation lives in the DocFx site:

- **[Mapping file reference](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/gql2grpc-mapping.md)** - YAML/JSON shape, argument rules, precedence, introspection defaults, FieldMask projection.
- **[Gql2Grpc cookbook](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/gql2grpc-cookbook.md)** - worked patterns: queries, mutations, subscriptions, fragments, auth, error envelopes.
- **[CLI reference: gql2grpc](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/cli-reference.md#gql2grpc-command)** - every CLI option.
- **[Architecture: Gql2Grpc subsystems](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/architecture.md#gql2grpc-subsystems)** - module layout and dataflow.
- **[Gql2Grpc future work](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/gql2grpc-future-work.md)** - deferred backlog.

## Quickstart

Install the tool by packing from source (the package is not published to NuGet.org):

```bash
dotnet pack Src/Gql2Grpc -c Release
dotnet tool install -g Gql2Grpc --add-source Src/Gql2Grpc/bin/Release
```

Reflection-based (no mapping file, no protoset):

```bash
gql2grpc \
  --plaintext --default-service testing.TestService \
  localhost:9090 'query { EmptyCall }'
```

With a mapping file and cookie auth:

```bash
export SESSION_COOKIE='mySessionCookie'
gql2grpc \
  --mapping ./gql2grpc.yaml \
  -H "cookie: .tmc.ac.session=${SESSION_COOKIE}" \
  api.example.com:443 \
  'query($first: Int) { activeResponses(first: $first) { id payload { body } } }' \
  --var first=10
```

Success:

```json
{ "data": { "activeResponses": { "id": "...", "payload": { "body": "..." } } } }
```

Failure (envelope + `extensions.grpcStatus`):

```json
{
  "data": null,
  "errors": [{
    "message": "<gRPC status detail>",
    "path": ["activeResponses"],
    "extensions": {
      "code": "UPSTREAM_ERROR",
      "grpcStatus": "Unavailable",
      "grpcStatusCode": 14
    }
  }]
}
```

Exit codes follow `grpcurl-dotnet`'s convention: `0` success, `1` internal, `2` usage/JSON-parse (including bad CLI arguments), `3` schema/file, `64 + <grpc status code>` for RPC errors (`UNAVAILABLE=14` => `78`), `130` for Ctrl+C. Most transport failures surface as `64 + status` (connection refused => `78`).
