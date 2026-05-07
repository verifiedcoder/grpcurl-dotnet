# GrpCurl.Net

A .NET implementation of grpcurl - a command-line tool for interacting with gRPC servers.

## Overview

GrpCurl.Net allows you to interact with gRPC servers using JSON requests instead of binary protocol buffers. It supports server reflection, protoset files, and dynamic method invocation for all four gRPC method types.

## Key Features

- **Server Reflection** - Discover services and methods at runtime
- **Protoset Support** - Use pre-compiled descriptor files for offline operation
- **All Streaming Types** - Unary, server-streaming, client-streaming, and bidirectional
- **Rich CLI** - Verbose output, timing information, and colored terminal display
- **TLS/mTLS** - Full support for secure connections and mutual authentication
- **Cross-Platform** - Runs on Windows, Linux, and macOS

## Quick Start

```bash
# List services on a gRPC server
grpcurl.net list --plaintext localhost:9090

# Describe a service
grpcurl.net describe --plaintext localhost:9090 my.package.Service

# Invoke a method
grpcurl.net invoke --plaintext -d '{"name": "World"}' localhost:9090 my.package.Service/SayHello
```

## Agent / Script Usage

GrpCurl.Net is designed to be driven by AI agents and shell scripts without human input.

- **JSON output**: Pass `--output json` to any subcommand. `list`/`describe` emit a single JSON envelope per call; `invoke` emits one NDJSON envelope per response message (`{"kind":"message","index":N,"message":{...}}`).
- **Errors on stderr**: All errors and progress chatter go to **stderr**. **stdout** carries only data. In `--output json` mode errors are a single JSON line on stderr (`{"kind":"error","category":"rpc|network|timeout|usage|schema|cancelled|internal", "exitCode":N, "message":"...", ...}`).
- **Exit codes**: `0` success, `1` internal, `2` usage, `3` schema/file, `4` network, `5` timeout, `64+gRPC status` for RPC errors, `130` for Ctrl+C.
- **Always set `--max-time`** on `invoke`. There is no built-in default deadline; without it a hung server can block forever.
- **stdin**: `--data @` reads JSON from stdin. The CLI **refuses** to read from a TTY (it would block) — pipe input or use inline `--data '{...}'`. For client/bidi streaming, supply a JSON array (`--data '[{...},{...}]'`) or concatenated objects.
- **Headers**: `-H 'name: value'` may be repeated. Values support `${VAR}` environment-variable expansion.
- **Idempotent file outputs**: `--protoset-out` refuses to overwrite an existing file unless `--force` is set.

```bash
# Machine-readable list
grpcurl.net list --plaintext --output json localhost:9090 | jq '.services[]'

# NDJSON streaming responses
grpcurl.net invoke --plaintext --output json --max-time 30s \
  localhost:9090 svc/Stream -d '{"n":3}' \
  | jq -c '.message'

# Structured error on stderr (exit code 78 = 64 + Unimplemented(12))
grpcurl.net invoke --plaintext --output json --max-time 5s \
  localhost:9090 svc/Missing -d '{}' 2>error.json ; echo $?
```

## Documentation

The documentation is a [DocFx](https://github.com/dotnet/docfx) project, so you can serve a self-contained local documentation site.

- [Introduction](Docs/introduction.md) - Learn about GrpCurl.Net and its capabilities
- [Getting Started](Docs/getting-started.md) - Installation and first steps
- [CLI Reference](Docs/articles/cli-reference.md) - Complete command reference
- [Examples](Docs/articles/examples.md) - Usage examples for common scenarios
- [Architecture](Docs/articles/architecture.md) - Internal design and extensibility
- [Learn Protobuf](Docs/articles/learn-protobuf/index.md) - Tutorial series: learn protobuf from scratch
- [API Reference](Docs/api/GrpCurl.Net.DescriptorSources.yml) - Public API documentation

## Requirements

- .NET 10.0 or later
- Target gRPC server with reflection enabled (or protoset files)
