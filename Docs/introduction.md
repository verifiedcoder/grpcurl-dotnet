# Introduction

GrpCurl.Net is a .NET implementation of [grpcurl](https://github.com/fullstorydev/grpcurl), a command-line tool for interacting with gRPC servers. It enables you to invoke gRPC methods using JSON requests without needing pre-compiled client stubs.

## What is GrpCurl.Net?

GrpCurl.Net is a cross-platform CLI tool that makes it easy to:

- **Explore gRPC APIs**: List services and methods, describe message types
- **Test gRPC endpoints**: Invoke methods with JSON payloads
- **Debug gRPC applications**: Verbose output with timing information
- **Work offline**: Export and use protoset files without server reflection

## Comparison with grpcurl

GrpCurl.Net is a .NET port of the original Go-based grpcurl tool. Both tools share the same core functionality:

| Feature | Go grpcurl | GrpCurl.Net |
|---------|-----------|-------------|
| Server reflection | Yes | Yes |
| Protoset files | Yes | Yes |
| Proto file parsing | Yes | No* |
| All streaming types | Yes | Yes |
| TLS/mTLS | Yes | Yes |
| Custom headers | Yes | Yes |
| Verbose output | Yes | Yes |
| Timing information | Basic | Detailed |
| Coloured output | No | Errors/verbose only |

*GrpCurl.Net does not support parsing `.proto` files at runtime. Use server reflection or pre-compiled protoset files instead.

## Supported Features

### Commands

- **`list`**: List available services or methods for a specific service
- **`describe`**: Describe services, methods, or message types with optional JSON templates
- **`invoke`**: Invoke gRPC methods with JSON request data

### gRPC Method Types

All gRPC method types are fully supported:

- **Unary**: Single request, single response
- **Server Streaming**: Single request, stream of responses
- **Client Streaming**: Stream of requests, single response
- **Bidirectional Streaming**: Stream of requests and responses

### Schema Discovery

Two methods for discovering protobuf schemas:

1. **Server Reflection**: Query running servers via the gRPC reflection protocol
2. **Protoset Files**: Use pre-compiled FileDescriptorSet files

### Connection Options

- Plaintext HTTP/2 (`--plaintext`)
- TLS with custom CA certificates (`--cacert`)
- Mutual TLS with client certificates (`--cert`, `--key`)
- PKCS12 certificate password (`--cert-password`)
- Skip certificate verification (`--insecure`)
- Custom authority header (`--authority`)
- Custom server name for TLS (`--servername`)

### Request/Response Options

- Custom headers (`-H`, `--rpc-header`, `--reflect-header`) — supports `${ENV_VAR}` expansion
- Request data from stdin (`-d @`) — streaming modes also accept concatenated JSON objects
- Emit default values (`--emit-defaults`)
- Allow unknown JSON fields (`--allow-unknown-fields`)
- Connection timeout (`--connect-timeout`)
- Operation timeout (`--max-time`)
- Message size limits (`--max-msg-sz`)

## GraphQL bridge (gql2grpc)

Alongside `grpcurl.net`, this project ships `gql2grpc` — a GraphQL-to-gRPC CLI bridge built directly on top of the same descriptor-source and dynamic-invocation code. It lets existing GraphQL clients (GraphiQL, Altair, or any SDK) and enterprise systems that authenticate via cookies / bearer tokens talk to a gRPC backend without a bespoke gateway.

Feature highlights:

- **Operations**: queries, mutations, subscriptions (→ server-streaming, emitted as NDJSON).
- **GraphQL surface**: fragments (spreads and inline), aliases, operation-level variables, `--var`/`--variables-file`, `@include`/`@skip` directives, selection-set response pruning, FieldMask projection.
- **Schema introspection**: answers `__schema`, `__type`, and `__typename` from a schema synthesised out of the descriptor set — no RPC required.
- **Transport parity with `grpcurl.net invoke`**: every TLS / mTLS / header / timeout / message-size option behaves identically, because both CLIs share a single in-process `GrpcChannel`.
- **Config-driven mapping**: a YAML (or JSON) file associates each GraphQL field with a gRPC service/method. A convention-based fallback covers the common case (`activeResponses` → `ActiveResponses` on a default service).

Start with the [mapping reference](articles/gql2grpc-mapping.md) and then the [cookbook](articles/gql2grpc-cookbook.md) for worked patterns.

## Requirements

- The current LTS .NET SDK. The authoritative version is whatever each `.csproj` targets (`TargetFramework`) and whatever [`global.json`](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/global.json) pins as the minimum SDK.
- A gRPC server with one of:
  - Server reflection enabled, or
  - Pre-compiled protoset files

## Architecture

GrpCurl.Net is built with modern .NET practices:

- **Async/await throughout**: Non-blocking I/O operations
- **IAsyncEnumerable**: Natural streaming support
- **System.CommandLine**: CLI argument parsing
- **Spectre.Console**: Error messages and verbose output formatting
- **Google.Protobuf**: Official protobuf runtime for .NET

For more details on the internal architecture, see [Architecture](articles/architecture.md).

## Learn Protobuf

New to Protocol Buffers? The [Learn Protobuf](articles/learn-protobuf/index.md) tutorial series teaches protobuf from first principles using GrpCurl.Net and a hands-on test server. No prior knowledge required.

## Getting help

- Hit an error you don't understand? Check [Troubleshooting](articles/troubleshooting.md) — organised by error class.
- Need to authenticate? [Authentication recipes](articles/authentication.md) covers bearer tokens, API keys, cookies, and mTLS (PEM + PKCS12).
- Wiring into a pipeline? [CI/CD integration](articles/ci-cd.md) documents exit codes, bash patterns, and GitHub Actions / GitLab CI snippets.
