# Drop-in upstream `grpcurl` shape

GrpCurl.Net is normally invoked through three subcommands — `list`, `describe`, `invoke`. To make migration from upstream `grpcurl` painless, the CLI also accepts upstream-style positional invocations: `grpcurl.net <flags> host:port [symbol]`. The first argument that isn't a flag determines which subcommand the input is routed to.

```bash
# Upstream-style — works as-is on GrpCurl.Net:
grpcurl.net -plaintext localhost:9090                       # → list
grpcurl.net -plaintext localhost:9090 my.pkg.Service        # → describe
grpcurl.net -plaintext -d '{}' localhost:9090 my.pkg.Service/MyMethod   # → invoke
```

## Subcommand inference

| Positional shape | Routes to |
|---|---|
| no positionals, or only `-foo` flags | `list` |
| `host:port` alone | `list <host:port>` |
| `host:port <Service>` | `describe <host:port> <Service>` |
| `host:port <Service/Method>` | `invoke <host:port> <Service/Method>` |

If the first positional **is** one of the native subcommands (`list`, `describe`, `invoke`), the rest of the argv is passed through untouched.

## Flag mapping

Each single-dash upstream flag maps to the native `--double-dash` form. Both `-foo value` and `-foo=value` are accepted.

| Upstream grpcurl | GrpCurl.Net native |
|---|---|
| `-plaintext` | `--plaintext` |
| `-insecure` | `--insecure` |
| `-cacert <path>` | `--cacert <path>` |
| `-cert <path>` | `--cert <path>` |
| `-key <path>` | `--key <path>` |
| `-servername <host>` | `--servername <host>` |
| `-authority <host>` | `--authority <host>` |
| `-user-agent <ua>` | `--user-agent <ua>` |
| `-d <json>` / `-data <json>` | `--data <json>` |
| `-format json\|text` | `--format json\|text` |
| `-max-time <duration>` | `--max-time <duration>` |
| `-connect-timeout <duration>` | `--connect-timeout <duration>` |
| `-max-msg-sz <bytes>` | `--max-msg-sz <bytes>` |
| `-keepalive-time <duration>` | `--keepalive-time <duration>` |
| `-keepalive-timeout <duration>` | `--keepalive-timeout <duration>` |
| `-protoset <path>` | `--protoset <path>` |
| `-protoset-out <path>` | `--protoset-out <path>` |
| `-proto <path>` | `--proto <path>` |
| `-import-path <dir>` / `-I <dir>` | `--import-path <dir>` |
| `-proto-out-dir <dir>` | `--proto-out-dir <dir>` |
| `-H 'name: value'` | `-H 'name: value'` |
| `-rpc-header 'name: value'` | `--rpc-header 'name: value'` |
| `-reflect-header 'name: value'` | `--reflect-header 'name: value'` |
| `-v` | `--verbose` |
| `-vv` | `--very-verbose` |
| `-emit-defaults` | `--emit-defaults` |
| `-allow-unknown-fields` | `--allow-unknown-fields` |
| `-unsafe-show-secrets` | `--unsafe-show-secrets` |

## Intentional divergences

These differ from upstream by design — they are not bugs:

- **Header `${VAR}` expansion is always on.** Upstream grpcurl gates it behind `-expand-headers`; GrpCurl.Net treats it as the natural shape because env-var-driven secrets are the dominant CI pattern. Set values literally if you need a `$` in the value.
- **`--max-time` bounds the whole operation, not just the RPC.** It covers protoset load, reflection, stdin reads, and the call itself.
- **`-v` redacts sensitive headers by default.** Use `--unsafe-show-secrets` to see raw values.
- **`unix:///path` addresses are supported on Linux/macOS** (not on Windows).

## Falls-through behaviour

An unknown single-dash flag is passed through verbatim so the parser raises the standard usage error (exit code 2) rather than silently dropping it:

```bash
grpcurl.net -unknown-thing localhost:9090
# Required command was not provided.
# Unrecognized command or argument '-unknown-thing'.
# Unrecognized command or argument 'localhost:9090'.
# Run 'grpcurl.net [command] --help' for usage.
```

## Worked examples

```bash
# 1. List services
grpcurl.net -plaintext localhost:9090

# 2. Describe with import-path so the schema comes from local .proto files
grpcurl.net -plaintext -I ./protos -proto svc.proto localhost:9090 pkg.Service

# 3. Invoke with mTLS, JSON envelope output, and authority override
grpcurl.net \
  -cacert ca.pem -cert client.crt -key client.key \
  -authority internal.svc.cluster \
  -max-time 30s \
  -format json \
  -d '{"name":"world"}' \
  api.internal:443 pkg.Service/SayHello

# 4. Drop-in invocation against a Unix socket
grpcurl.net -plaintext -d '{}' unix:///var/run/grpc.sock pkg.Service/Status
```
