# GrpCurl.Net

A .NET implementation of grpcurl — a command-line tool for interacting with gRPC servers, plus the `Gql2Grpc` GraphQL-to-gRPC proxy. Both tools are backed by the shared internal `GrpCurl.Net.Core` project.

## Overview

GrpCurl.Net lets you call gRPC servers with JSON instead of binary protobuf. It supports server reflection, protoset files, dynamic invocation for all four gRPC method types, mTLS, Unix domain sockets, and HTTP/2 `:authority` override.

## Key Features

- **Server Reflection** — Discover services and methods at runtime over `grpc.reflection.v1alpha`.
- **Protoset Support** — Use pre-compiled `FileDescriptorSet` files for offline operation.
- **All Streaming Types** — Unary, server-streaming, client-streaming, and bidirectional.
- **TLS / mTLS** — Custom CA, PEM and PKCS12 client certs, content-based format detection, configurable revocation policy, ephemeral key storage by default.
- **HTTP/2 `:authority`** — True per-request authority override via a delegating handler; `--servername` still controls SNI / cert validation.
- **Unix domain sockets** — `unix:///path/to/sock` addresses on Linux and macOS.
- **Binary metadata** — `-H "trace-bin: <base64>"` is decoded and sent as `byte[]` metadata.
- **Whole-operation `--max-time`** — Bounds the entire flow (protoset load, reflection, stdin reads, RPC), not just the gRPC deadline.
- **Secret redaction** — `--verbose` redacts sensitive metadata (authorization, cookies, `*-token`, `*-secret`, `*-bin`, etc.). `--unsafe-show-secrets` opts out.
- **Cross-platform** — Build, test, and publish on Windows, Linux, and macOS. CI runs the full suite on all three.

## Quick Start

```bash
# List services on a gRPC server
grpcn list --plaintext localhost:9090

# Describe a service
grpcn describe --plaintext localhost:9090 my.package.Service

# Invoke a method
grpcn invoke --plaintext --max-time 30s \
  -d '{"name": "World"}' localhost:9090 my.package.Service/SayHello

# mTLS with custom CA + client cert
grpcn invoke --max-time 30s \
  --cacert ca.pem --cert client.crt --key client.key \
  -d '{}' my-service.internal:443 my.package.Service/Status

# Unix-domain socket (Linux / macOS)
grpcn invoke --plaintext --max-time 30s \
  unix:///var/run/grpc.sock my.package.Service/Status -d '{}'
```

## Install

### Download a release (recommended)

Prebuilt, self-contained binaries for **GrpCurl.Net Studio**, `grpcn`, and `gql2grpc` are published on the [**Releases page**](https://github.com/verifiedcoder/grpcurl-dotnet/releases) for Windows, macOS, and Linux (x64 & arm64) — no .NET runtime required. See the [**install guide**](Docs/articles/install.md) for picking the right archive, verifying `SHA256SUMS`, and first-launch steps (the binaries are unsigned — zero-budget OSS). There is no NuGet.org / `dotnet tool` feed.

New to the desktop app? The [**Studio user guide**](Docs/articles/studio/index.md) covers the UI tour, first run, the keyboard map, and common workflows.

### Build the CLIs from source

GrpCurl.Net can also be installed by building the [.NET tool](https://learn.microsoft.com/dotnet/core/tools/global-tools) packages from source. The packages are **not published to NuGet.org**; there is no public-feed install path.

```bash
dotnet pack Src/GrpCurl.Net -c Release
dotnet tool install -g GrpCurl.Net --add-source Src/GrpCurl.Net/bin/Release
grpcn --version
```

The GraphQL bridge is built and installed the same way as a separate tool:

```bash
dotnet pack Src/Gql2Grpc -c Release
dotnet tool install -g Gql2Grpc --add-source Src/Gql2Grpc/bin/Release
gql2grpc --help
```

For team distribution, push the packed `.nupkg` files to an internal NuGet feed and install with `--add-source <feed-url>`.

`GrpCurl.Net.Core` is bundled inside the tool packages and is not installed directly.

> **Naming note:** the command was formerly invoked as `grpcurl.net`; it was renamed to `grpcn` before first public release. The package ID remains `GrpCurl.Net`.

## Agent / Script Usage

GrpCurl.Net is designed to be driven by AI agents and shell scripts without human input.

- **JSON output**: Pass `--output json`. `list`/`describe` emit one JSON envelope per call. `invoke` emits NDJSON: one `{"kind":"message","index":N,"message":{...}}` line per response.
- **Errors on stderr**: All errors and progress chatter go to **stderr**. **stdout** carries only data. In `--output json` mode errors are a single JSON line on stderr (`{"kind":"error","category":"rpc|network|timeout|usage|schema|cancelled|internal", "exitCode":N, "message":"...", ...}`).
- **Exit codes**: `0` success, `1` internal, `2` usage, `3` schema/file, `4` network, `5` timeout, `64+gRPC status` for RPC errors, `130` for Ctrl+C. In practice most transport failures surface through the gRPC layer as `64+status` rather than `4`/`5` — e.g. connection refused exits `78` (64 + Unavailable(14)) and a `--max-time` hit during connect exits `65` (64 + Cancelled(1)), matching upstream grpcurl. Scripts should treat `>=64` as RPC/transport failures and `1`–`3` as local failures.
- **Always set `--max-time`** on reflection-backed `list`/`describe`, `invoke`, and `gql2grpc` in unattended scripts. There is no built-in default deadline; without it a hung server can block forever.
- **stdin**: `--data @` reads JSON from stdin. The CLI **refuses** to read from a TTY — pipe input or use inline `--data '{...}'`. Stdin reads are capped at 16 MiB by default; use `--max-stdin-bytes <bytes>` to set an explicit numeric byte limit. For client/bidi streaming, supply a JSON array (`--data '[{...},{...}]'`) or concatenated objects.
- **Headers**: `-H 'name: value'` may be repeated. Text values support `${VAR}` environment-variable expansion. Header names ending in `-bin` are base64-decoded and sent as binary metadata.
- **Idempotent file outputs**: `--protoset-out` refuses to overwrite an existing file unless `--force` is set.

```bash
# Machine-readable list
grpcn list --plaintext --output json localhost:9090 | jq '.services[]'

# NDJSON streaming responses
grpcn invoke --plaintext --output json --max-time 30s \
  localhost:9090 svc/Stream -d '{"n":3}' \
  | jq -c '.message'

# Structured error on stderr (exit code 76 = 64 + Unimplemented(12))
grpcn invoke --plaintext --output json --max-time 5s \
  localhost:9090 svc/Missing -d '{}' 2>error.json ; echo $?
```

## Documentation

The documentation is a [DocFx](https://github.com/dotnet/docfx) project, so you can serve a self-contained local documentation site.

- [Introduction](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/introduction.md) - Learn about GrpCurl.Net and its capabilities
- [Getting Started](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/getting-started.md) - Installation and first steps
- [CLI Reference](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/cli-reference.md) - Complete command reference
- [Examples](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/examples.md) - Usage examples for common scenarios
- [Architecture](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/architecture.md) - Internal design and extensibility (now split into `GrpCurl.Net.Core` + CLI)
- [Authentication](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/authentication.md) - TLS, mTLS, hardening defaults, secret redaction
- [Learn Protobuf](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/articles/learn-protobuf/index.md) - Tutorial series
- [API Reference](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/Docs/api-reference.md) - Public API documentation

## Requirements

- .NET 10.0 or later
- Target gRPC server with reflection enabled (or protoset files)

## Repository Layout

```
Src/
  GrpCurl.Net.Core/     # Shared dependency project — descriptor sources, channel factory, invocation
  GrpCurl.Net/          # CLI shell — references GrpCurl.Net.Core
  Gql2Grpc/             # GraphQL-to-gRPC proxy — references GrpCurl.Net.Core
Tests/
  GrpCurl.DotNet.Tests.Unit/
  GrpCurl.DotNet.Tests.Integration/
  GrpCurl.Net.TestServer/
  Gql2Grpc.Tests/
  TestCertificates/     # Test CA + server/client/expired/wrong-CA cert fixtures
  TestProtosets/
Docs/                   # DocFX site
Scripts/                # Feature demonstration scripts (Unix/WSL/Git-Bash)
```
