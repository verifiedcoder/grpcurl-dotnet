# GrpCurl.Net Scripts

Two things live here:

1. **`ValidationRunner/`** - the **canonical** cross-platform validation runner. A .NET
   console project that publishes the GrpCurl.Net CLI + TestServer to a temp directory
   and exercises the production smoke-test scenarios against those *published* binaries. Runs
   identically on Windows, Linux, and macOS. This is what CI runs.

2. **`01-*.sh` ... `32-*.sh`** - feature **demonstration** scripts. Bash-only. Useful for
   manual exploration on Linux, macOS, WSL, or Git Bash; **not** the supported validation
   flow. Scripts `02` through `32` source `common.sh`, invoke the CLIs with `dotnet run`,
   and assume a TestServer is listening on `localhost:9090`.

3. **`dev-bootstrap/install-go.sh`** - optional developer bootstrap that installs Go,
   upstream `grpcurl`, and the `grpc-go` interop server into the current user's HOME
   (no `sudo`, no `/usr/local` edits). Versions and checksums are pinned. Use it only
   if you want to compare GrpCurl.Net against upstream behaviour locally.

## Canonical: cross-platform validation runner

```bash
cd repo
dotnet run --project Scripts/ValidationRunner/ValidationRunner.csproj --configuration Release -- --ci
```

The runner publishes the CLI + test server, picks a free localhost port, runs every
scenario, asserts on exit code + stdout/stderr, and tears down the test server. It
exits non-zero if any scenario fails. CI invokes it on every OS in the matrix.

Scenarios cover: `list` services and methods, `describe`, unary/server-streaming/JSON
envelope `invoke`, binary `-bin` metadata, and the drop-in upstream-grpcurl flag shape.
The numbered Bash demos below are broader teaching examples for manual exploration.

## Bash feature demos

These scripts are convenient for manual exploration but require **Bash**. They run on
Linux, macOS, WSL, and Git Bash. They invoke the source projects through `dotnet run`,
so they do not depend on OS-specific apphost filenames in `bin/Debug`. Start the
TestServer on `localhost:9090` first, then run the individual demo scripts from a
second terminal. `common.sh` detects either `dotnet` or `dotnet.exe`; when WSL is
using the Windows SDK, it converts absolute WSL paths before handing them to .NET.

```bash
# Terminal 1
bash Scripts/01-start-server.sh

# Terminal 2
bash Scripts/02-list-services.sh
bash Scripts/04-describe-service.sh
bash Scripts/08-invoke-unary-call.sh
```

| Script | Purpose |
|--------|---------|
| **Discovery** | |
| 01-start-server.sh | Start the TestServer (run in separate terminal) |
| 02-list-services.sh | List all services via server reflection |
| 03-list-methods.sh | List methods for a specific service |
| 04-describe-service.sh | Describe a service and its methods |
| 05-describe-message.sh | Describe message types |
| 06-describe-with-template.sh | Generate JSON templates for messages |
| **Unary RPC** | |
| 07-invoke-empty-call.sh | Basic unary RPC with empty request |
| 08-invoke-unary-call.sh | Unary RPC with request data |
| 09-invoke-unary-with-payload.sh | Unary RPC with complex payload |
| **Streaming RPC** | |
| 10-invoke-server-streaming.sh | Server streaming - multiple responses |
| 11-invoke-client-streaming.sh | Client streaming - multiple requests |
| 12-invoke-bidirectional-full.sh | Full duplex bidirectional streaming |
| 13-invoke-bidirectional-half.sh | Half duplex bidirectional streaming |
| **Options & Features** | |
| 14-verbose-output.sh | Verbose and very verbose output modes |
| 15-emit-defaults.sh | Emit default values in JSON output |
| 16-custom-headers.sh | Add custom headers to requests |
| 17-reflect-vs-rpc-headers.sh | Differentiate reflection vs RPC headers |
| 18-using-protoset.sh | Use protoset file instead of reflection |
| 19-export-protoset.sh | Export FileDescriptorSet to file |
| 20-timeout-options.sh | Connection and operation timeouts |
| 21-message-size-limits.sh | Control max message sizes |
| 22-error-handling.sh | Handle gRPC errors gracefully |
| 23-allow-unknown-fields.sh | Handle unknown fields in JSON |
| 24-authority-header.sh | Override `:authority` header |
| 25-user-agent.sh | Set custom User-Agent header |
| **Comprehensive** | |
| 26-all-features-demo.sh | Combined demo of multiple features |
| 27-concatenated-json.sh | Concatenated JSON input for streaming methods |
| **Gql2Grpc (GraphQL bridge)** | |
| 28-gql-simple-query.sh | Simple reflection-based GraphQL query |
| 29-gql-mapping-file.sh | Query with a mapping file (nested request shape) |
| 30-gql-subscription.sh | GraphQL subscription to server-streaming (NDJSON) |
| 31-gql-error-envelope.sh | Force a gRPC error and show the GraphQL error envelope |
| 32-gql-introspection.sh | `__schema` introspection over the synthesised schema |
| **Validation entry points** | |
| run-production-validation.sh | Thin wrapper that delegates to `ValidationRunner` (Unix only). |
| dev-bootstrap/install-go.sh | Developer-only bootstrap for upstream `grpcurl`/`grpc-go`. Pinned versions + SHA-256 checks. |

## TestServer Services

The TestServer provides `testing.TestService` with:

- `EmptyCall` - Unary RPC (empty request/response)
- `UnaryCall` - Unary RPC with payload (honours `response_size`, `fill_username`, `fill_oauth_scope`, `response_status`)
- `StreamingOutputCall` - Server streaming
- `StreamingInputCall` - Client streaming
- `FullDuplexCall` - Bidirectional streaming (immediate)
- `HalfDuplexCall` - Bidirectional streaming (buffered)

It can also start with TLS or mTLS:

```bash
# Plaintext
dotnet run --project Tests/GrpCurl.Net.TestServer -- --port 9090

# TLS (uses Tests/TestCertificates/server.crt + server.key)
dotnet run --project Tests/GrpCurl.Net.TestServer -- --port 9443 --tls

# mTLS (requires --cert/--key in the client)
dotnet run --project Tests/GrpCurl.Net.TestServer -- --port 9443 --require-client-cert
```

## Troubleshooting

**"Connection refused"** - Ensure TestServer is running on port 9090.

**"Project file not found"** - Run scripts from inside the repository checkout, or keep
the repository layout intact so `Scripts/common.sh` can find `Src/`.

**Permission denied** - Run `chmod +x *.sh`. On Windows, run via Git Bash / WSL, or
prefer the cross-platform ValidationRunner.

**protoc not found** - Some demos use `--proto` which needs `protoc` on PATH. Install
via `apt install protobuf-compiler`, `brew install protobuf`, or `choco install protoc`.
