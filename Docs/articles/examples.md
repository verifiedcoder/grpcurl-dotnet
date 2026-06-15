# Examples

This page provides practical examples for common GrpCurl.Net use cases.

## Service Discovery

### List All Services

```bash
grpcn list --plaintext localhost:9090
```

For unattended scripts, bound reflection-backed discovery:

```bash
grpcn list --plaintext --max-time 10s localhost:9090
```

### List Services with Verbose Output

```bash
grpcn list --plaintext -v localhost:9090
```

Output includes connection details and service listing:
```
grpc.reflection.v1alpha.ServerReflection
testing.TestService
testing.UnimplementedService
```

### List Methods for a Service

```bash
grpcn list --plaintext localhost:9090 testing.TestService
```

---

## Describing Services and Messages

### Describe a Service

```bash
grpcn describe --plaintext localhost:9090 testing.TestService
```

### Describe a Message Type

```bash
grpcn describe --plaintext localhost:9090 testing.SimpleRequest
```

### Get JSON Template for a Message

Very useful for understanding the expected request format:

```bash
grpcn describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
```

Example Output:
```
testing.SimpleRequest is a message:
message SimpleRequest {
  .testing.PayloadType response_type = 1;
  int32 response_size = 2;
  .testing.Payload payload = 3;
  bool fill_username = 4;
  bool fill_oauth_scope = 5;
  .testing.EchoStatus response_status = 7;
}

Message template:
{
  "response_type": "COMPRESSABLE",
  "response_size": 0,
  "payload": {
    "type": "COMPRESSABLE",
    "body": ""
  },
  "fill_username": false,
  "fill_oauth_scope": false,
  "response_status": {
    "code": 0,
    "message": ""
  }
}
```

---

## Invoking Methods

### Simple Unary Call

```bash
grpcn invoke --plaintext localhost:9090 testing.TestService/EmptyCall
```

### Unary Call with Request Data

```bash
grpcn invoke --plaintext \
  -d '{"response_size": 20, "fill_username": true}' \
  localhost:9090 testing.TestService/UnaryCall
```

### Server Streaming

Request one message, receive multiple responses:

```bash
grpcn invoke --plaintext \
  -d '{"response_parameters": [{"size": 10}, {"size": 20}, {"size": 30}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

Output (multiple JSON objects):
```json
{
  "payload": {
    "body": "AAECAwQFBgcICQ=="
  }
}
{
  "payload": {
    "body": "AAECAwQFBgcICQoLDA0ODxAREhM="
  }
}
{
  "payload": {
    "body": "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwd"
  }
}
```

### Client Streaming

Send multiple messages, receive one response:

```bash
echo '{"payload": {"body": "AAAA"}}
{"payload": {"body": "BBBB"}}
{"payload": {"body": "CCCC"}}' | \
grpcn invoke --plaintext --max-stdin-bytes 1048576 -d @ localhost:9090 testing.TestService/StreamingInputCall
```

### Client Streaming with Concatenated JSON

For streaming methods, you can also pass multiple JSON objects as concatenated inline data (without stdin):

```bash
grpcn invoke --plaintext \
  -d '{"payload":{"body":"YQ=="}} {"payload":{"body":"YmI="}} {"payload":{"body":"Y2Nj"}}' \
  localhost:9090 testing.TestService/StreamingInputCall
```

Each top-level JSON object is parsed as a separate request message. Objects can be separated by whitespace or newlines.

### Bidirectional Streaming

Send and receive multiple messages:

```bash
echo '{"response_parameters": [{"size": 10}]}
{"response_parameters": [{"size": 20}]}' | \
grpcn invoke --plaintext --max-stdin-bytes 1048576 -d @ localhost:9090 testing.TestService/FullDuplexCall
```

---

## Working with Headers

### Add Custom Headers

```bash
grpcn invoke --plaintext \
  -H "Authorization: Bearer my-token" \
  -H "X-Request-Id: req-12345" \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

### Different Headers for Reflection vs RPC

Use `--reflect-header` for reflection-only headers and `--rpc-header` for RPC-only headers:

```bash
grpcn invoke --plaintext \
  --reflect-header "X-Reflect-Auth: reflect-token" \
  --rpc-header "X-RPC-Auth: rpc-token" \
  -H "X-Common: shared-value" \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

### Headers with Environment Variables

```bash
export AUTH_TOKEN="my-secret-token"
grpcn invoke --plaintext \
  -H "Authorization: Bearer ${AUTH_TOKEN}" \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

---

## Using Protoset Files

### Generate a Protoset File

Using `protoc` (shown here against this repository's own TestServer schema; substitute your `.proto` files):
```bash
protoc --descriptor_set_out=service.protoset \
  --include_imports \
  --proto_path=Tests/GrpCurl.Net.TestServer/Protos \
  test.proto
```

Or export from a running server:
```bash
grpcn list --plaintext --max-time 10s --protoset-out service.protoset localhost:9090
```

Local protoset files are capped at 64 MiB each before they are read. Reflection descriptor responses are capped at 16 MiB by default, and `--protoset-out` refuses to overwrite an existing file unless you pass `--force`.

### List Services from Protoset (Offline)

```bash
grpcn list --protoset service.protoset
```

### Invoke Method Using Protoset

```bash
grpcn invoke --plaintext \
  --protoset service.protoset \
  -d '{"name": "World"}' \
  localhost:9090 my.package.Service/SayHello
```

---

## Verbose and Timing Output

### Verbose Mode

```bash
grpcn invoke --plaintext -v \
  -H "Authorization: Bearer demo-token" \
  -d '{"payload": {"body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

Verbose output includes method descriptor, request metadata, response headers/trailers. Sensitive metadata values such as `authorization`, cookies, token-like headers, and `*-bin` headers are redacted by default; use `--unsafe-show-secrets` only when the terminal or log destination is trusted:
```
Resolved method descriptor:
rpc UnaryCall ( .testing.SimpleRequest ) returns ( .testing.SimpleResponse );

Request metadata to send:
authorization: [REDACTED]
user-agent: grpcn/1.0.0

Response headers received:
(server-specific headers)

Response contents:
{
  "payload": {
    "body": "SGVsbG8="
  }
}

Response trailers received:
(empty)

Sent 1 request and received 1 response
```

### Emit Default Values

Protobuf JSON drops unset scalars by default (proto3 semantics). Add `--emit-defaults` when you need every field present — useful for downstream tools that expect a stable shape:

```bash
# Without --emit-defaults: unset fields are omitted
grpcn invoke --plaintext \
  -d '{"payload": {"body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall

# With --emit-defaults: username, oauth_scope, response_status etc. appear as their defaults
grpcn invoke --plaintext --emit-defaults \
  -d '{"payload": {"body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

Demonstrated by `Scripts/15-emit-defaults.sh`.

### Custom User-Agent

The default User-Agent is derived from the assembly version (e.g. `grpcn/1.0.0`). Override with `--user-agent` when you need to identify traffic to your service or bypass a WAF rule:

```bash
grpcn invoke --plaintext \
  --user-agent "my-tool/2.4 (ci-runner-42)" \
  -d '{}' localhost:9090 testing.TestService/EmptyCall
```

Demonstrated by `Scripts/25-user-agent.sh`.

### Combined Demo

`Scripts/26-all-features-demo.sh` chains many of the above flags together (verbose output, custom headers, timeouts, message-size tuning, error formatting) against the test server — useful as an acceptance check after a local build.

### Very Verbose Mode with Timing

```bash
grpcn invoke --plaintext --vv \
  -d '{"payload": {"body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

Output includes detailed timing (values are illustrative):
```
═══════════════════════════════════════════════════════════
                    Timing Summary
═══════════════════════════════════════════════════════════
  Connection Establishment                12 ms (    12000 μs)  21.4%
  Schema Discovery                        23 ms (    23000 μs)  41.1%
  Request Preparation                      1 ms (     1000 μs)   1.8%
  RPC Channel Setup                        2 ms (     2000 μs)   3.6%
  RPC Invocation                           0 ms (        0 μs)   0.0%
  Request Serialisation                    0 ms (        0 μs)   0.0%
  Network Round-Trip                      16 ms (    16000 μs)  28.6%
  Response Deserialization                 1 ms (     1000 μs)   1.8%
───────────────────────────────────────────────────────────
  Total Time                              56 ms
───────────────────────────────────────────────────────────
  Request Size:  24 bytes
  Response Size: 42 bytes
  Message Count: 1
═══════════════════════════════════════════════════════════
```

---

## Timeouts and Limits

### Connection Timeout

```bash
grpcn invoke --plaintext \
  --connect-timeout 5s \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

### Operation Timeout (gRPC Deadline)

```bash
# Illustrative service/method — substitute your own long-running RPC
grpcn invoke --plaintext \
  --max-time 30s \
  -d '{}' \
  localhost:9090 my.package.Service/LongRunningOperation
```

`--max-time` also bounds reflection-backed `list` and `describe` operations:

```bash
grpcn list --plaintext --max-time 10s localhost:9090
grpcn describe --plaintext --max-time 10s localhost:9090 testing.TestService
```

### Stdin Size Limit

```bash
echo '{"payload": {"body": "AAAA"}}' | \
grpcn invoke --plaintext \
  --max-stdin-bytes 1048576 \
  -d @ \
  localhost:9090 testing.TestService/StreamingInputCall
```

`-d @` reads at most 16 MiB from stdin by default. Use `--max-stdin-bytes <bytes>` to set a smaller or explicitly documented numeric byte budget for scripts.

### Message Size Limits

```bash
# Illustrative service/method — substitute your own large-payload RPC
grpcn invoke --plaintext \
  --max-msg-sz 10MB \
  -d '{"large_payload": "..."}' \
  localhost:9090 my.package.Service/ProcessLargeData
```

---

## Error Handling

### View Error as JSON

```bash
grpcn invoke --plaintext \
  --output json \
  -d '{"response_status": {"code": 3, "message": "Custom error"}}' \
  localhost:9090 testing.TestService/UnaryCall 2>error.json ; echo $?
```

The error envelope is written to **stderr** as a single JSON line; stdout stays clean. Process exit code is `64 + gRPC status code` (here `64 + 3 = 67`).

Example `error.json`:
```json
{"kind":"error","category":"rpc","exitCode":67,"message":"Custom error","address":"localhost:9090","method":"testing.TestService/UnaryCall","grpc":{"code":3,"status":"InvalidArgument","detail":"Custom error"}}
```

### Cancellation (Ctrl+C)

Pressing Ctrl+C during a long-running call sends SIGINT. The CLI cancels the in-flight RPC gracefully and exits with code **130**:

```bash
grpcn invoke --plaintext \
  -d '{"response_parameters": [{"interval_us": 1000000}, {"interval_us": 1000000}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
# press Ctrl+C after the first response

echo $?
# 130
```

This matches the POSIX convention (`128 + SIGINT=2`). In streaming mode, any message already emitted to stdout is preserved — no partial JSON object is written after cancellation. See [CI/CD integration](ci-cd.md) for pipeline patterns.

### Insecure TLS (Testing Only)

**⚠️ `--insecure` disables certificate validation.** Never use this against production endpoints — a man-in-the-middle can intercept every request.

```bash
# Test-only: accept self-signed or mismatched certs during development.
# Targets the TestServer in TLS mode:
#   dotnet run --project Tests/GrpCurl.Net.TestServer -- --port 9443 --tls
grpcn invoke --insecure \
  -d '{}' localhost:9443 testing.TestService/EmptyCall
```

Prefer `--cacert <ca.crt>` against internal PKIs. See [Troubleshooting](troubleshooting.md) for TLS diagnostics.

### Allow Unknown Fields

If your JSON contains fields not in the proto definition:

```bash
grpcn invoke --plaintext \
  --allow-unknown-fields \
  -d '{"known_field": "value", "unknown_field": "ignored"}' \
  localhost:9090 testing.TestService/UnaryCall
```

---

## TLS Connections

### TLS with Default CA

```bash
grpcn invoke \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### TLS with Custom CA Certificate

```bash
grpcn invoke \
  --cacert /path/to/ca.crt \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### Mutual TLS (mTLS)

```bash
grpcn invoke \
  --cacert /path/to/ca.crt \
  --cert /path/to/client.crt \
  --key /path/to/client.key \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### Mutual TLS with PKCS12 Certificate

```bash
grpcn invoke \
  --cert /path/to/client.p12 \
  --cert-password "my-password" \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### Skip Certificate Verification (Testing Only)

```bash
grpcn invoke --insecure \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### Custom Authority Header

Useful for virtual hosting or when the server expects a specific host:

```bash
grpcn invoke \
  --authority api.example.com \
  -d '{}' \
  10.0.0.1:443 my.package.Service/Method
```

---

## Well-Known Types

GrpCurl.Net automatically handles Google protobuf well-known types with their canonical JSON representations.

### Timestamp (RFC 3339)

```bash
grpcn invoke --plaintext \
  -d '{"created_at": "2024-01-15T10:30:00Z"}' \
  localhost:9090 my.package.Service/CreateEvent
```

### Duration

```bash
grpcn invoke --plaintext \
  -d '{"timeout": "30.5s"}' \
  localhost:9090 my.package.Service/SetTimeout
```

### Wrapper Types

Wrapper types (e.g., `google.protobuf.StringValue`) are represented as their raw JSON values:

```bash
grpcn invoke --plaintext \
  -d '{"optional_name": "Alice"}' \
  localhost:9090 my.package.Service/UpdateUser
```

Supported well-known types: `Timestamp`, `Duration`, `Empty`, `Any`, `FieldMask`, `Struct`, `Value`, `ListValue`, and all wrapper types (`DoubleValue`, `FloatValue`, `Int64Value`, `UInt64Value`, `Int32Value`, `UInt32Value`, `BoolValue`, `StringValue`, `BytesValue`).

## GraphQL bridge (gql2grpc)

`gql2grpc` executes GraphQL operations against gRPC services. It mirrors `invoke`'s transport surface (TLS, mTLS, headers, deadlines, message-size limits, reflection or protoset descriptors) and adds queries, mutations, subscriptions (server-streaming → NDJSON), fragments, aliases, variables, `@include`/`@skip`, selection-set pruning, FieldMask projection, and schema introspection.

All worked examples now live in the dedicated cookbook:

- [Gql2Grpc cookbook](gql2grpc-cookbook.md) — queries, mutations, subscriptions, variables/fragments/aliases, cookie auth, FieldMask projection, error envelopes, parallel execution, introspection, `--raw` debugging, named operations.
- [Mapping file reference](gql2grpc-mapping.md) — YAML/JSON shape, argument rules, precedence, validation, introspection tuning.
- [CLI Reference § gql2grpc](cli-reference.md#gql2grpc-command) — every CLI option.
- [Gql2Grpc future work](gql2grpc-future-work.md) — deferred backlog.

Quick teaser:

```bash
gql2grpc --plaintext \
  --default-service testing.TestService \
  localhost:9090 \
  'query { EmptyCall }'
```

```json
{ "data": { "EmptyCall": {} } }
```
