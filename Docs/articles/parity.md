# grpcurl Parity Matrix

GrpCurl.Net is a .NET re-implementation of [fullstorydev/grpcurl](https://github.com/fullstorydev/grpcurl). This page lists every upstream capability and the status in this project. Use it to plan migrations or to decide whether GrpCurl.Net can replace a specific grpcurl invocation.

## Supported capabilities

| Reference capability | Status | Notes |
|---|---|---|
| `list` services/methods | Supported | Reflection or protoset. |
| `describe` services/messages/enums/methods | Supported | Proto-like output, JSON envelope option. |
| Invoke unary / server-stream / client-stream / bidi | Supported | All four streaming combinations are exercised by integration tests. |
| Reflection descriptor source (`v1alpha`) | Supported | Channel is reused for both reflection and RPC (closes the P0 mTLS-drop bug). |
| Protoset descriptor source (`--protoset` repeatable) | Supported | Multi-file `FileDescriptorSet`s; co-existing with a network channel for the RPC. |
| TLS / plaintext | Supported | `--plaintext`, `--insecure`, `--cacert`, `--cert`, `--key`, `--cert-password`. |
| mTLS | Supported | Single channel applies client cert to reflection *and* RPC. PKCS12 and PEM. Content-based format detection. |
| `--authority` | Supported | Rewrites HTTP/2 `:authority` via a `DelegatingHandler`. Independent of `--servername` / SNI. |
| `--servername` | Supported | Maps to `SslOptions.TargetHost` for cert validation and SNI. |
| `-H`, `--rpc-header`, `--reflect-header` | Supported | Repeatable. `${VAR}` env expansion. **Always-on** env expansion is an intentional divergence from upstream's `-expand-headers` gate. |
| `*-bin` binary metadata | Supported | Header values for `*-bin` names are base64-decoded into `byte[]`. |
| `--max-time` | Supported | Bounds the **entire** operation including protoset load, reflection, stdin reads, mapping file reads, and the RPC. Differs from upstream which only bounds the RPC. |
| `--connect-timeout` | Supported | Honored on both plaintext and TLS channels (closes the P1 plaintext-fast-path bug). |
| `--max-msg-sz` | Supported | Applied to both send and receive. |
| `--keepalive-time`, `--keepalive-timeout` | Supported | Map to `SocketsHttpHandler.KeepAlivePingDelay`/`Timeout`. |
| `--revocation-mode online\|offline\|nocheck` | Supported | Default `Online` when a custom CA is supplied. |
| `--exportable-key` | Supported | Off by default. Default key storage is `EphemeralKeySet`. |
| `--unsafe-show-secrets` | Supported | Off by default. Verbose metadata is redacted (authorization, cookie, `*-token`, `*-secret`, `*-bin`, etc.). |
| Unix domain sockets (`unix:///path`) | Supported on Linux/macOS | Windows fast-fails with a clear error message. |
| JSON request/response format | Supported | Dynamic message serialisation via `SimpleDynamicMessage`. |
| `--protoset-out` | Supported | Refuses to overwrite without `--force`. |

## Intentional divergences from upstream

These are documented choices, not bugs.

| Behaviour | Upstream grpcurl | GrpCurl.Net |
|---|---|---|
| Header `${VAR}` expansion | Off by default; gated behind `-expand-headers` | Always on |
| Total deadline scope | `-max-time` bounds the RPC only | `--max-time` bounds the entire operation |
| Library reuse | C-based, no managed library surface | `GrpCurl.Net.Core` packs as a NuGet library |
| Verbose output | Prints raw metadata values | Sensitive values redacted by default (opt out with `--unsafe-show-secrets`) |
| CLI shape | Positional invocation: `grpcurl <flags> host:port symbol` | Native shape: `list`/`describe`/`invoke` subcommands. A drop-in compatibility shape is on the roadmap (`grpcurl-compat.md`). |

## Newly implemented (closed)

| Capability | Status | Notes |
|---|---|---|
| `--proto` / `--import-path` proto-source descriptor | ✅ | `ProtoSource` shells out to local `protoc` and feeds the resulting `FileDescriptorSet` into the same loader as `--protoset`. |
| Protobuf text format (`--format text`) | ✅ | `DynamicTextFormat` does Print + Parse over `SimpleDynamicMessage` for scalars, nested messages, repeated, and enums by name or number. Maps / groups not supported in the text-format path (documented). |
| Rich status details (`grpc-status-details-bin`) | ✅ | `RichStatusDecoder` parses the trailer and unpacks 10+ well-known `google.rpc.*` Any payloads (`ErrorInfo`, `RetryInfo`, `BadRequest`, etc). Surfaced in the JSON error envelope. |
| Response headers / trailers parity across call types | ✅ | New `StreamingInvocationResult` / `ClientStreamingInvocationResult` wrappers expose headers and trailers for server-streaming, client-streaming, and bidi RPCs. |
| `--proto-out-dir` | ✅ | `ProtoFileEmitter` reconstructs `.proto` files from the active `FileDescriptorSet`. Refuses to overwrite without `--force`. |
| Drop-in CLI shape | ✅ | `GrpcurlCompatHandler` rewrites upstream-style argv into the native subcommand shape. See [`grpcurl-compat.md`](grpcurl-compat.md). |
| Streaming stdin parity | ✅ | `-d @` reads stdin to EOF and parses it through the same multi-value JSON decoder as inline `-d`. Bounded by `--max-stdin-bytes` (default 16 MiB). |
| Cross-platform `ValidationRunner` | ✅ | `Scripts/ValidationRunner` publishes the CLI + test server and runs scenarios against published binaries. CI runs it on Win/Linux/macOS. |
| proto2 group wire format | ✅ | `ProtobufReader.ReadGroup` and `ProtobufWriter` SGROUP/EGROUP round trip. Full proto2 fidelity (extensions, defaults, required-field validation) is still out of scope. |

## Still out of scope

| Capability | Notes |
|---|---|
| ALTS, xDS, `SSLKEYLOGFILE` | No demand yet. File an issue if you need one. |
| proto2 extensions + required-field validation | Group wire format works; full proto2 semantic fidelity is a larger undertaking. |
| Maps in protobuf text format | JSON path supports them; the text-format path rejects map literals. |
