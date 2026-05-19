# Gql2Grpc cookbook

Ready-to-run GraphQL patterns for the `gql2grpc` bridge. Every example assumes the test server is running on `localhost:9090` (via `Scripts/01-start-server.sh`). Most recipes work against any gRPC service — swap the service name and mapping as appropriate.

For the mapping-file schema, see [gql2grpc-mapping.md](gql2grpc-mapping.md). For every CLI option, see [CLI Reference § gql2grpc](cli-reference.md#gql2grpc-command). For error triage, see [Troubleshooting](troubleshooting.md).

## 1. Simple query (reflection, no mapping file)

When the server has reflection enabled and the GraphQL field name matches the method's PascalCase form, `gql2grpc` needs nothing beyond `--default-service`:

```bash
gql2grpc --plaintext \
  --default-service testing.TestService \
  localhost:9090 \
  'query { EmptyCall }'
```

```json
{ "data": { "EmptyCall": {} } }
```

Demonstrated by `Scripts/28-gql-simple-query.sh`.

## 2. Query with a mapping file

Declare the mapping once; keep your GraphQL documents idiomatic. The spread rule (`path: "."`) is the clean way to handle `input` types:

```yaml
# gql2grpc.yaml
version: 1
defaults:
  service: testing.TestService
operations:
  - graphqlField: unaryCall
    operationType: query
    method: UnaryCall
    arguments:
      input: { path: "." }
```

```bash
gql2grpc --plaintext --mapping gql2grpc.yaml localhost:9090 \
  'query { unaryCall(input: { payload: { body: "aGVsbG8=" } }) { payload { body } } }'
```

```json
{ "data": { "unaryCall": { "payload": { "body": "aGVsbG8=" } } } }
```

Demonstrated by `Scripts/29-gql-mapping-file.sh`.

## 3. Mutation

Mutations and queries differ only by the GraphQL `operationType`. Both translate to unary gRPC calls. Reuse the same mapping entry by registering the field under both types (or under `mutation` if it's semantically write-only):

```yaml
operations:
  - graphqlField: createPayload
    operationType: mutation
    method: UnaryCall               # the test server's UnaryCall echoes the payload back
    arguments:
      input: { path: "." }
```

```bash
gql2grpc --plaintext --mapping gql2grpc.yaml localhost:9090 \
  'mutation { createPayload(input: { payload: { body: "aGVsbG8=" } }) { payload { body } } }'
```

## 4. Subscription → NDJSON

A `subscription` operation maps to a server-streaming RPC. Each streamed message becomes one self-contained line of stdout:

```yaml
operations:
  - graphqlField: streamingOutput
    operationType: subscription
    method: StreamingOutputCall
    kind: serverStreaming
    arguments:
      input: { path: "." }
```

```bash
gql2grpc --plaintext --mapping gql2grpc.yaml localhost:9090 \
  'subscription { streamingOutput(input: { responseParameters: [{ size: 1 }, { size: 2 }, { size: 3 }] }) { payload { body } } }'
```

```
{"data":{"streamingOutput":{"payload":{"body":"AA=="}}}}
{"data":{"streamingOutput":{"payload":{"body":"AAE="}}}}
{"data":{"streamingOutput":{"payload":{"body":"AAEC"}}}}
```

Pipe to `jq` per-line, tail to a dashboard, or break early with `head -n N`. Ctrl+C cleanly cancels the stream with exit code 130. Demonstrated by `Scripts/30-gql-subscription.sh`.

## 5. Error envelope with gRPC status extensions

Force an upstream error and observe the spec-compliant GraphQL envelope. The test server's `fail-early` header makes every method fail with the supplied gRPC status code — `3` = `InvalidArgument`:

```bash
gql2grpc --plaintext --default-service testing.TestService \
  -H "fail-early: 3" \
  localhost:9090 'query { EmptyCall }'
```

```json
{
  "data": { "EmptyCall": null },
  "errors": [{
    "message": "fail",
    "path": ["EmptyCall"],
    "extensions": {
      "code": "UPSTREAM_ERROR",
      "grpcStatus": "InvalidArgument",
      "grpcStatusCode": 3
    }
  }]
}
```

Exit code is `64 + 3 = 67`. The error envelope is always emitted on stdout even when exit code is non-zero, so scripts can parse structured failures without choosing between stdout and stderr. Demonstrated by `Scripts/31-gql-error-envelope.sh`.

## 6. Schema introspection (answered locally)

`__schema`, `__type`, and `__typename` are intercepted by `IntrospectionExecutor` and answered from a schema synthesised out of the discovered `FileDescriptorSet` — no RPC is made. This means you can point [GraphiQL](https://github.com/graphql/graphiql) or [Altair](https://altair.sirmuel.design) at a thin HTTP wrapper around `gql2grpc` and get a full IDE experience:

```bash
gql2grpc --plaintext --default-service testing.TestService \
  localhost:9090 \
  'query { __schema { queryType { name } types { kind name } } }'
```

Subset of output:

```json
{
  "data": {
    "__schema": {
      "queryType": { "name": "Query" },
      "types": [
        { "kind": "SCALAR", "name": "String" },
        { "kind": "OBJECT", "name": "SimpleRequest" },
        { "kind": "OBJECT", "name": "SimpleResponse" },
        { "kind": "ENUM",   "name": "PayloadType" },
        ...
      ]
    }
  }
}
```

Use `--introspection=false` to disable interception (useful when testing what the upstream actually serves). Demonstrated by `Scripts/32-gql-introspection.sh`.

## 7. Variables, fragments, aliases

GraphQL's compositional tools all work. Variables are coerced to their declared types, fragments (spreads and inline) are inlined by `SelectionResolver`, and aliases flow through as the response keys.

```bash
gql2grpc --plaintext --mapping gql2grpc.yaml localhost:9090 \
  --var big=64 --var small=4 \
  'query Sizes($big: Int, $small: Int) {
     large: unaryCall(input: { payload: { body: "aGVsbG8=" }, responseSize: $big }) { ...Body }
     tiny:  unaryCall(input: { payload: { body: "aGk=" }, responseSize: $small })    { ...Body }
   }
   fragment Body on SimpleResponse { payload { body } }'
```

```json
{
  "data": {
    "large": { "payload": { "body": "aGVsbG8=" } },
    "tiny":  { "payload": { "body": "aGk=" } }
  }
}
```

The two root fields run **concurrently** via `ParallelFieldScheduler` (bounded at 4), but the envelope preserves document order. Switch to `--variables-file vars.json` for complex variables that don't fit into `name=value` form.

## 8. Authentication via cookie header

Cookie authentication is the motivating scenario for `gql2grpc`. Supply the cookie via `-H`; `${ENV_VAR}` substitution keeps secrets out of shell history:

```bash
export SESSION_COOKIE='mySessionCookie'
gql2grpc --mapping gql2grpc.yaml \
  -H "cookie: .tmc.ac.session=${SESSION_COOKIE}" \
  api.example.com:443 \
  'query($first: Int) { activeResponses(first: $first) { id } }' \
  --var first=10
```

`--rpc-header` and `--reflect-header` scope a header to one traffic class if reflection and RPC need different credentials. See [Authentication recipes](authentication.md) for bearer tokens, API keys, and mTLS.

## 9. FieldMask projection for bandwidth-sensitive APIs

Server-side filtering is more efficient than fetching everything and dropping fields client-side. `$selection: { fieldMask: <target> }` synthesises a `google.protobuf.FieldMask` from the GraphQL selection tree and injects it into the request:

```yaml
operations:
  - graphqlField: activeResponses
    operationType: query
    service: company.product.v1.ResponseService
    method: ListActiveResponses
    arguments:
      $selection: { fieldMask: read_mask }
    response:
      unwrap: items                           # response has { items: [...] }
```

A GraphQL selection of `{ id payload { body } }` sends `read_mask = "id,payload.body"` to the server and projects the response down to exactly those fields.

## 10. Multi-field documents (parallel execution)

When a single query selects multiple root fields, they execute concurrently (bounded at 4 in flight):

```bash
gql2grpc --plaintext --mapping gql2grpc.yaml localhost:9090 \
  'query Dashboard {
     a: unaryCall(input: { payload: { body: "YQ==" } })   { payload { body } }
     b: unaryCall(input: { payload: { body: "Yg==" } })   { payload { body } }
     c: unaryCall(input: { payload: { body: "Yw==" } })   { payload { body } }
     d: unaryCall(input: { payload: { body: "ZA==" } })   { payload { body } }
   }'
```

All four RPCs are in flight at once; partial failures are surfaced as per-field errors while successful siblings still populate `data`. Subscriptions and other server-streaming operations always run standalone — the scheduler never parallelises a stream.

## 11. `--raw` for debugging

When a projection returns unexpected shapes, drop to the raw gRPC JSON to confirm what the server actually returned:

```bash
gql2grpc --plaintext --mapping gql2grpc.yaml --raw \
  localhost:9090 \
  'query { unaryCall(input: { payload: { body: "aGVsbG8=" } }) { payload { body } } }'
```

The response will contain every protobuf field (respecting `--emit-defaults`), ignoring the GraphQL selection set. Pair with `-vv` to additionally log the outbound request JSON on stderr.

## 12. Named operations from a multi-operation file

A `.graphql` file can contain multiple named operations. Use `--operation <name>` to pick one:

```graphql
# queries.graphql
query ListUsers { users { id name } }
query GetUser($id: ID!) { user(id: $id) { id name email } }
mutation CreateUser($input: UserInput!) { createUser(input: $input) { id } }
```

```bash
gql2grpc --plaintext --mapping gql2grpc.yaml localhost:9090 \
  -f queries.graphql \
  --operation GetUser \
  --var id=abc123
```

The loader rejects the invocation with a clear error when the document has multiple operations and `--operation` is omitted.
