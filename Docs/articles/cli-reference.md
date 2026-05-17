# CLI Reference

Complete reference for all GrpCurl.Net commands and options.

## Commands

GrpCurl.Net provides three main commands:

- `list`: List services or methods
- `describe`: Describe protobuf symbols
- `invoke`: Invoke gRPC methods

## Global Options

These options are available for all commands:

| Option | Description |
|--------|-------------|
| `--plaintext` | Use plaintext HTTP/2 (no TLS). |
| `--insecure` | Skip TLS certificate verification. **Test/diagnostic only.** |
| `--cacert <path>` | PEM CA certificate for custom server-cert validation. |
| `--cert <path>` | Client certificate file for mutual TLS. PEM or PKCS12; format detected from file content. |
| `--key <path>` | Client private key file (PEM) for mutual TLS. Omit for PKCS12 (key inside the bundle). |
| `--cert-password <password>` | Password for an encrypted PKCS12 (.p12/.pfx) client certificate. |
| `--revocation-mode <online\|offline\|nocheck>` | Certificate revocation policy when `--cacert` is set. Default: `online`. Use `nocheck` only against self-signed test fixtures with no CRL distribution point. |
| `--exportable-key` | Load PKCS12 client keys with `X509KeyStorageFlags.Exportable`. Default is `EphemeralKeySet` so the key never persists. |
| `--connect-timeout <duration>` | Per-attempt TCP/TLS connection timeout (e.g. `10s`, `1m`, `500ms`). Honoured on both plaintext and TLS. |
| `--keepalive-time <duration>` | HTTP/2 keepalive ping interval (default `60s`). |
| `--keepalive-timeout <duration>` | HTTP/2 keepalive ping ack timeout (default `30s`). |
| `--authority <value>` | Rewrites the HTTP/2 `:authority` pseudo-header on every reflection and RPC call. Independent of `--servername`. |
| `--servername <value>` | Overrides the TLS SNI / `SslOptions.TargetHost` for certificate validation. Does **not** change `:authority`. |
| `--user-agent <value>` | Custom User-Agent header value. |
| `-v`, `--verbose` | Verbose output. Metadata values are **redacted by default**; pass `--unsafe-show-secrets` to opt out. |
| `--vv`, `--very-verbose` | Very verbose output with phase-level timing summary. |
| `--unsafe-show-secrets` | Disable redaction in verbose output. Use only when you control the destination of the captured output. |
| `-H`, `--header <header>` | Add header (`name: value`). Repeatable. Text values support `${ENV_VAR}` expansion. Header names ending in `-bin` are **base64-decoded** and sent as binary metadata. |
| `--reflect-header <header>` | Header for reflection requests only. Repeatable. |
| `--rpc-header <header>` | Header for the business RPC only. Repeatable. |
| `--protoset <path>` | Use protoset file(s) instead of server reflection. Repeatable. |
| `--protoset-out <path>` | Export `FileDescriptorSet` to file after operation. Refuses to overwrite without `--force`. |
| `--force` | Allow `--protoset-out` to overwrite an existing file. |
| `--output <text\|json>` | Output format. `text` (default) is human-readable. `json` emits stable line-based envelopes (NDJSON for `invoke` streaming). Errors always go to stderr. |

### Address forms

- `host:port` — TCP/TLS (default).
- `unix:///absolute/path` (Linux / macOS) — Unix domain socket. Windows fast-fails with `PlatformNotSupportedException`.
- The scheme may be omitted; `--plaintext` selects `http://`, otherwise `https://` is inferred.

### Binary metadata example

```bash
# Send a base64-encoded byte payload as the trace-bin header
grpcurl.net invoke --plaintext --max-time 30s \
  -H 'trace-bin: AQIDBA==' \
  localhost:9090 my.pkg.Service/Echo -d '{}'
```

---

## list Command

List available services or methods for a specific service.

### Syntax

```bash
grpcurl.net list [options] [address] [service]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `address` | Server address (host:port). Required unless using `--protoset`. |
| `service` | Service name to list methods for. If omitted, lists all services. |

### Examples

```bash
# List all services
grpcurl.net list --plaintext localhost:9090

# List methods for a service
grpcurl.net list --plaintext localhost:9090 my.package.Service

# Machine-readable JSON envelope (single line on stdout)
grpcurl.net list --plaintext --output json localhost:9090 \
  | jq '.services[]'

# List services using protoset (offline)
grpcurl.net list --protoset service.protoset

# List and export protoset (initial write)
grpcurl.net list --plaintext --protoset-out export.protoset localhost:9090
# ... and force-overwrite on subsequent runs
grpcurl.net list --plaintext --protoset-out export.protoset --force localhost:9090
```

---

## describe Command

Describe a service, method, or message type.

### Syntax

```bash
grpcurl.net describe [options] [address] [symbol]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `address` | Server address (host:port). Required unless using `--protoset`. |
| `symbol` | Symbol to describe (service, method, or message type). If omitted, describes all services. |

### Options

| Option | Description |
|--------|-------------|
| `--msg-template` | Output a JSON template for message types |

### Examples

```bash
# Describe all services
grpcurl.net describe --plaintext localhost:9090

# Describe a specific service
grpcurl.net describe --plaintext localhost:9090 my.package.Service

# Describe a message type
grpcurl.net describe --plaintext localhost:9090 my.package.MyMessage

# Get JSON template for a message
grpcurl.net describe --plaintext --msg-template localhost:9090 my.package.MyRequest

# Machine-readable JSON envelope for a service
grpcurl.net describe --plaintext --output json localhost:9090 my.package.Service \
  | jq '{kind, methods: [.methods[].fullName]}'
```

---

## invoke Command

Invoke a gRPC method.

### Syntax

```bash
grpcurl.net invoke [options] <address> <method>
```

### Arguments

| Argument | Description |
|----------|-------------|
| `address` | Server address (host:port). Required. |
| `method` | Method to invoke in format `Service/Method` or `package.Service/Method`. Required. |

### Options

| Option | Description |
|--------|-------------|
| `-d`, `--data <json>` | Request data as JSON. Use `@` to read from stdin (refused if stdin is a TTY). For streaming methods, supply a JSON array `[{...},{...}]` or concatenated objects `{...}{...}`. |
| `--emit-defaults` | Include default values in JSON output |
| `--allow-unknown-fields` | Allow unknown fields in JSON input |
| `--max-msg-sz <size>` | Maximum message size (e.g., `4MB`, `10MB`) |
| `--max-time <duration>` | Maximum operation time / gRPC deadline. **Always set this for unattended use** — there is no built-in default. |
| `--rpc-header <header>` | Add header to RPC requests only |

`--output` and `--force` are inherited from [Global Options](#global-options) and behave identically here. In `--output json` mode, each response message becomes a single NDJSON line (`{"kind":"message","index":N,"message":{...}}`) on stdout; errors render as a one-line `{"kind":"error",...}` envelope on stderr.

### Examples

```bash
# Invoke unary method with inline JSON
grpcurl.net invoke --plaintext \
  -d '{"name": "World"}' \
  localhost:9090 my.package.Service/SayHello

# Invoke with data from stdin
echo '{"name": "World"}' | grpcurl.net invoke --plaintext \
  -d @ \
  localhost:9090 my.package.Service/SayHello

# Invoke with custom headers
grpcurl.net invoke --plaintext \
  -H "Authorization: Bearer token123" \
  -H "X-Request-Id: abc123" \
  -d '{}' \
  localhost:9090 my.package.Service/GetData

# Invoke with timeout
grpcurl.net invoke --plaintext \
  --max-time 30s \
  -d '{}' \
  localhost:9090 my.package.Service/LongRunningOperation

# Invoke server streaming
grpcurl.net invoke --plaintext \
  -d '{"count": 5}' \
  localhost:9090 my.package.Service/StreamData

# Invoke client streaming (multiple messages from stdin)
echo '{"value": 1}
{"value": 2}
{"value": 3}' | grpcurl.net invoke --plaintext \
  -d @ \
  localhost:9090 my.package.Service/AccumulateValues

# Invoke bidirectional streaming
echo '{"message": "hello"}
{"message": "world"}' | grpcurl.net invoke --plaintext \
  -d @ \
  localhost:9090 my.package.Service/Chat

# Machine-readable NDJSON streaming responses
grpcurl.net invoke --plaintext --output json --max-time 30s \
  -d '{"count": 5}' \
  localhost:9090 my.package.Service/StreamData \
  | jq -c '.message'
```

---

## gql2grpc Command

Executes a GraphQL document against a gRPC server. The document can contain queries, mutations, or subscriptions. Transport options mirror [`invoke`](#invoke-command) exactly; GraphQL-specific options add operation selection, variables, mapping configuration, and output shaping.

### Usage

```
gql2grpc [OPTIONS] <address> [query]
```

- `<address>` — `host:port` of the target gRPC server.
- `[query]` — GraphQL document on the command line. Optional when `-f`/`--file` is supplied.

### Descriptor source

| Option | Description |
|---|---|
| `--protoset <path>` | Protoset file(s). Repeatable. When absent, server reflection is used. |
| `--protoset-out <path>` | Write the discovered `FileDescriptorSet` to a file after the operation runs. Refuses to overwrite without `--force`. |
| `--force` | Allow `--protoset-out` to overwrite an existing file. |

### Transport (identical to `invoke`)

`--plaintext`, `--insecure`, `--cacert`, `--cert`, `--key`, `--cert-password`, `--authority`, `--servername`, `--user-agent`, `--connect-timeout`, `--max-time`, `--max-msg-sz`, `-H` / `--header`, `--reflect-header`, `--rpc-header`. See [Global Options](#global-options) for semantics. `${ENV_VAR}` expansion in `-H` values works the same way.

### GraphQL input

| Option | Description |
|---|---|
| `<query>` (positional) | GraphQL document as a single string argument. |
| `-f`, `--file <path>` | Load the GraphQL document from a file. |
| `--operation <name>` | Select a named operation when the document declares more than one. |
| `--var <name=value>` | Supply an operation variable. Repeatable. Values are coerced to the declared variable type (Int, Float, Boolean, String). |
| `--variables-file <path>` | JSON object of variables. CLI `--var` overrides matching keys. |

### Mapping

| Option | Description |
|---|---|
| `--mapping <path>` | Path to a mapping file (YAML or JSON; extension-detected). See the [Gql2Grpc mapping reference](gql2grpc-mapping.md). |
| `--default-service <fqn>` | Fully-qualified gRPC service used by the convention-based fallback when no mapping entry matches. |

### Output and shaping

| Option | Description |
|---|---|
| `--emit-defaults` | Include default proto3 values in the projected response. |
| `--allow-unknown-fields` | Skip unknown fields in request JSON instead of erroring (default `true`). |
| `--strict-selection` | When set, a GraphQL field absent from the gRPC response produces an error instead of `null`. |
| `--raw` | Emit the unshaped gRPC JSON response without selection projection (debugging aid). |

`errors[].extensions` is always populated (`code`, plus `grpcStatus`/`grpcStatusCode` for upstream gRPC failures). The previous `--format-error` toggle has been removed — agents can rely on the structured shape unconditionally.

### Introspection

| Option | Description |
|---|---|
| `--introspection` | Enable `__schema`/`__type`/`__typename` interception (default `true`). When off, introspection selections are routed to the normal translator and will fail. |

### Diagnostics

`-v`/`--verbose` and `--vv`/`--very-verbose` behave as on `invoke`: verbose logs each root field's mapping and method name to stderr; very-verbose additionally logs the translated request JSON.

### Exit codes

`0` on success, `2` for usage/JSON-parse errors, `3` for missing-file/schema errors, `4` for network errors, `5` for timeouts, `64 + grpcStatusCode` when the upstream RPC fails (e.g. `InvalidArgument=3` → `67`), `130` on Ctrl+C, `1` for anything else. Top-level failures (e.g. missing GraphQL document, unreachable address) still produce a parseable `{"data":null,"errors":[...]}` envelope on stdout — there is no out-of-band error format.

### Examples

```bash
# Convention-based: no mapping file
gql2grpc --plaintext --default-service testing.TestService \
  localhost:9090 'query { EmptyCall }'

# Mutation with a mapping file
gql2grpc --plaintext --mapping ./gql2grpc.yaml \
  localhost:9090 'mutation { createPayload(input: { responseSize: 1 }) { payload { body } } }'

# Subscription (server-streaming) → NDJSON
gql2grpc --plaintext --mapping ./gql2grpc.yaml \
  localhost:9090 \
  'subscription { streamingOutput(input: { responseParameters: [{ size: 1 }] }) { payload { body } } }'

# Cookie-authenticated with env var
export SESSION_COOKIE='...'
gql2grpc --mapping ./gql2grpc.yaml \
  -H "cookie: .session=${SESSION_COOKIE}" \
  api.example.com:443 \
  'query($first: Int) { activeResponses(first: $first) { id } }' --var first=10

# Schema introspection — answered entirely client-side
gql2grpc --plaintext --default-service testing.TestService \
  localhost:9090 'query { __schema { queryType { name } } }'
```

See also: the [Gql2Grpc cookbook](gql2grpc-cookbook.md) for worked patterns, the [mapping file reference](gql2grpc-mapping.md) for rule syntax, and [Authentication recipes](authentication.md) for header-based auth.

---

## Duration Format

Duration values accept the following formats:

| Format | Example | Description |
|--------|---------|-------------|
| Seconds | `30s` | 30 seconds |
| Milliseconds | `500ms` | 500 milliseconds |
| Minutes | `5m` | 5 minutes |
| Hours | `1h` | 1 hour |
| Decimal | `1.5m` | 1.5 minutes (90 seconds) |

## Size Format

Size values accept the following formats:

| Format | Example | Description |
|--------|---------|-------------|
| Bytes | `1024` | 1024 bytes |
| Kilobytes | `64KB` | 64 kilobytes |
| Megabytes | `4MB` | 4 megabytes |
| Gigabytes | `1GB` | 1 gigabyte |

---

## Exit Codes

| Code | Description |
|------|-------------|
| 0 | Success |
| 1 | Internal error (unhandled exception, generic failure) |
| 2 | Usage error (bad CLI args, JSON parse failure in request data) |
| 3 | Schema/file error (protoset missing or invalid, symbol not found, output file overwrite refused) |
| 4 | Network error (connection failure outside the RPC call) |
| 5 | Timeout (connect or operation deadline exceeded outside the RPC call) |
| 64 + StatusCode | RPC error from the server (e.g. `Unavailable=14` → `78`, `NotFound=5` → `69`) |
| 130 | Cancelled by user (Ctrl+C) |

Both `grpcurl.net` and `gql2grpc` use the same exit-code mapping. In `--output json` mode every code is accompanied by a structured envelope on stderr (or, for `gql2grpc`, inside the GraphQL response envelope on stdout) that carries the category and any RPC details.

## Environment Variables

Headers can reference environment variables using `${VAR_NAME}` syntax:

```bash
grpcurl.net invoke --plaintext \
  -H "Authorization: Bearer ${AUTH_TOKEN}" \
  -d '{}' \
  localhost:9090 my.package.Service/SecureMethod
```

Expansion happens at metadata-creation time; missing variables raise an error rather than silently expanding to an empty string.

## Output Discipline (for AI agents and scripts)

Both CLIs follow strict stdout/stderr discipline:

- **stdout** carries only data: list entries, describe output, invoke response messages, GraphQL response envelopes.
- **stderr** carries everything else: error envelopes (text or JSON), suggestion blocks, verbose chatter.

In `--output json` mode for `grpcurl.net`:

- `list` (no service): `{"kind":"services","services":[...]}` — single line on stdout.
- `list <service>`: `{"kind":"methods","service":"...","methods":[...]}` — single line on stdout.
- `describe <symbol>`: `{"kind":"service|message|enum|method|messageTemplate", ...}` — single line on stdout. When called without a symbol, one envelope per service (NDJSON).
- `invoke`: one `{"kind":"message","index":N,"message":{...}}` envelope per response on stdout (NDJSON).
- Errors (any command): one `{"kind":"error","category":"usage|schema|network|timeout|rpc|cancelled|internal","exitCode":N,"message":"...", ... }` line on stderr.

For `gql2grpc`, output is *always* a GraphQL response envelope on stdout (single envelope for unary, NDJSON for subscriptions). Errors live inside the envelope as `errors[]` with `extensions.code` (and `extensions.grpcStatus`/`grpcStatusCode` for upstream gRPC failures).
