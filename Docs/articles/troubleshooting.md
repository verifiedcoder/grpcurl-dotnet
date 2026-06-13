# Troubleshooting

Common errors surfaced by `grpcn` and `gql2grpc`, with diagnoses and fixes. Organised by error class so you can jump to the symptom.

## TLS / certificate errors

### "The remote certificate was rejected" / "Remote certificate chain not validated"

The server's certificate is signed by a CA your OS trust store doesn't recognise (common with private PKIs and internal load balancers).

- Trust the CA explicitly via `--cacert /path/to/ca.crt`. The CA certificate must be in PEM form; see `Scripts/generate-certs.sh` for generating a matching pair.
- For testing only, bypass verification with `--insecure`. Never use this against production endpoints — it accepts any certificate.

### "The remote certificate's name doesn't match" / SNI mismatch

The certificate was issued for a hostname that doesn't match the URL you're connecting to — common when reaching a service via an IP, a sidecar, or a tunnelled connection.

- `--authority <name>` — overrides both the HTTP/2 `:authority` header and TLS SNI. Use when you want the server to route as though you reached it via `<name>`.
- `--servername <name>` — overrides only TLS SNI (certificate validation), leaving `:authority` alone. Use when routing differs from cert naming.

### mTLS: "SSL connect error" / "alert handshake failure"

- For PEM client certs: pass both `--cert <cert.pem>` and `--key <key.pem>`. The key must not be passphrase-protected (use PKCS12 for that).
- For `.p12` / `.pfx` containers: pass `--cert <file.p12> --cert-password <password>` and omit `--key`.
- If you see `certificate not yet valid` / `certificate has expired`, check system clock skew and certificate validity windows (`openssl x509 -in cert.pem -noout -dates`).

## Reflection errors

### `StatusCode.Unimplemented` on `grpc.reflection.v1alpha.ServerReflection`

The target server does not expose the reflection service.

- Add `Grpc.AspNetCore.Server.Reflection` to the server project, call `services.AddGrpcReflection()` and `app.MapGrpcReflectionService()`, then redeploy.
- If you can't modify the server, supply a pre-compiled descriptor via `--protoset <path>`. Generate one from the `.proto` files with `protoc --descriptor_set_out=service.protoset --include_imports ./service.proto`.
- Export on-the-fly from a server you *can* reach with reflection, using `--protoset-out service.protoset`, then point at the production server using `--protoset service.protoset`.

Local protoset files are capped at 64 MiB each before they are read. Reflection descriptor responses are capped at 16 MiB by default, and the retained descriptor graph is bounded to avoid pathological schemas. If a legitimate schema hits one of these limits, prefer a smaller service-specific protoset or split the schema into focused protosets.

### `--proto-out-dir` rejects descriptor file names

When reconstructing `.proto` files, descriptor names are treated as untrusted input. Rooted paths, `..` path traversal, invalid path characters, and names that resolve outside the requested output directory are rejected instead of being written.

### `StatusCode.Unavailable` or connection refused

- Confirm the server is actually listening: `ss -tln | grep <port>` (Linux) or `netstat -an` (Windows).
- For plaintext, ensure the server speaks HTTP/2 cleartext (h2c). ASP.NET Core Kestrel requires `HttpProtocols.Http2` in `ListenOptions`.
- For TLS, ensure the port serves TLS and ALPN advertises `h2`. Behind nginx, `grpc_pass` is usually required.

## Deadlines and timeouts

### `StatusCode.DeadlineExceeded`

The call exceeded the per-RPC deadline (`--max-time`). Inspect very-verbose timing to see which phase is slow:

```bash
grpcn invoke --vv --max-time 30s ...
```

The `--vv` output breaks down connection, schema discovery, serialisation, and the RPC itself. If "RPC" dominates, the server is slow; if "Schema Discovery" dominates, consider a protoset to skip reflection.

### Reflection-backed discovery hangs

`list`, `describe`, and `--protoset-out` can all use server reflection. Add `--max-time` to bound the whole discovery operation:

```bash
grpcn list --plaintext --max-time 10s localhost:9090
grpcn describe --plaintext --max-time 10s localhost:9090 my.pkg.Service
```

### Connection hangs before status arrives

- `--connect-timeout 10s` limits only the TCP + TLS handshake. If the socket opens but nothing else happens, the server may be silently blocking on something before handling the request. Try `--vv` for phase timings.
- Check for proxy interception (corporate MITM). Run `curl -v https://<host>` to confirm basic reachability and certificate chain.

## Message size

### "Received message exceeds the maximum configured message size (4,194,304)"

Default is 4 MB per message. Raise with `--max-msg-sz`:

```bash
grpcn invoke --max-msg-sz 64MB ...
```

Accepts `B`, `KB`, `MB`, `GB`. Applies to both inbound and outbound messages — set high enough for whichever direction overflows. For streaming methods, this is per-message, not total.

## JSON request errors

### "Unknown field 'foo'" when invoking

The JSON you supplied with `-d` contains a field the request message doesn't declare. Two fixes:

- Check the field name against the schema: `grpcn describe --plaintext --msg-template localhost:9090 my.pkg.MyRequest`.
- Temporarily skip the unknown field: add `--allow-unknown-fields`. Unknown fields are silently dropped — useful when sending the same JSON to multiple schema versions.

### "Invalid JSON" or parsing errors

- Shell escaping trips up inline JSON. Prefer `-d @` (read from stdin) or `-d @file.json` (read from a file).
- Protobuf JSON accepts both camelCase (`responseSize`) and snake_case (`response_size`) field names — you don't need to match the proto declaration exactly.

### "Stdin exceeded the maximum allowed size"

`grpcn invoke -d @` accepts up to 16 MiB from stdin by default and exits with code **2** (usage) when the cap is hit. For scripts, either split the payload or set an explicit numeric byte limit:

```bash
grpcn invoke --plaintext --max-stdin-bytes 1048576 -d @ localhost:9090 my.pkg.Service/Call
```

## Cancellation

### Ctrl+C mid-stream

Pressing Ctrl+C cancels the in-flight RPC gracefully via a linked `CancellationTokenSource`. The tool exits with code **130**, matching POSIX convention (128 + SIGINT). In streaming mode, any message already emitted to stdout is preserved; no partial JSON is written after cancellation.

Scripts relying on streaming output should use `set -o pipefail` to avoid masking cancellation from a downstream consumer. See [CI/CD integration](ci-cd.md) for patterns.

## Gql2Grpc-specific

### "No mapping for GraphQL field '...' and no default service"

The mapping resolver couldn't route a root field. Either:

- Supply an entry in the `operations:` section of your mapping file, or
- Provide `--default-service <fqn>` on the CLI (or `defaults.service` in the mapping file) so convention-based resolution can synthesise one.

### `"__type"` returns `null`

The requested GraphQL type isn't in the synthesised schema. Introspection only exposes types that appear in the `FileDescriptorSet` (reflection or protoset) — if the type is from an unimported proto file, it won't be there. Add the file to your protoset or wait for the other server-side schema to be advertised via reflection.

### Subscription emits only one line then exits

Server-side streams can terminate for legitimate reasons (server-side `OnCompleted()`, resource exhausted, deadline exceeded). Add `-vv` to see why the stream closed; trailers with status details are logged on stderr.

### "Required variable '$x' was not supplied"

`VariableCoercer` requires every non-null operation variable to be supplied. Pass it via `--var x=value` or via `--variables-file vars.json`. Variables with `NonNullType` and no default value are always required.

GraphQL document files, variables files, and YAML/JSON mapping files are capped at 4 MiB each before parsing. If a file exceeds that limit, reduce the input, split the mapping, or move large payload data into the underlying gRPC request rather than the GraphQL document.

### Unexpected `payload: null` in response

The server's `UnaryCall` (or other method) doesn't synthesise payloads from `response_size` — the gRPC test server echoes whatever `payload` you send. For real servers, check whether the selected field is actually populated in the response; try `--raw` to see the full gRPC JSON before selection projection.

## Exit codes

| Code | Meaning | When to expect |
|---|---|---|
| `0` | Success | Normal completion. For Gql2Grpc, envelope has no `errors[]`. |
| `1` | Internal error | Unhandled exception that didn't fall into a known category. |
| `2` | Usage / JSON-parse error | Bad CLI args, invalid JSON in `-d`, missing required GraphQL document. |
| `3` | Schema / file error | Protoset missing or invalid, symbol not found, refusing to overwrite an existing `--protoset-out` target. |
| `4` | Network error | TCP/TLS failure outside the RPC itself. Rare in practice: a refused connection usually surfaces as `Unavailable` → `78`. |
| `5` | Timeout | Connect or operation deadline exceeded outside the RPC itself. Rare in practice: a `--max-time` hit during connect usually surfaces as `Cancelled` → `65`. |
| `64 + grpcStatusCode` | RPC failed with the given status | Examples: `InvalidArgument` (3) → `67`; `NotFound` (5) → `69`; `Unauthenticated` (16) → `80`; `Unavailable` (14) → `78`. Most transport failures land here — treat `>= 64` as RPC/transport failure in scripts. |
| `130` | User cancelled (Ctrl+C / SIGINT) | Streaming and long unary calls. |

In `--output json` mode (`grpcn`) the same code is paired with a single-line error envelope on stderr (`{"kind":"error","category":"...","exitCode":N,...}`); for `gql2grpc` the envelope on stdout always carries `errors[].extensions.code` (and `grpcStatus`/`grpcStatusCode` when the upstream RPC failed).

For GitHub Actions / GitLab CI patterns, see [CI/CD integration](ci-cd.md).
