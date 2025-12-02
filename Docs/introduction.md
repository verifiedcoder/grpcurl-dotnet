# Introduction

GrpCurl.Net is a .NET implementation of [grpcurl](https://github.com/fullstorydev/grpcurl), a command-line tool for interacting with gRPC servers. It enables you to invoke gRPC methods using JSON requests without needing pre-compiled client stubs.

## What is GrpCurl.Net?

GrpCurl.Net is a cross-platform CLI tool that makes it easy to:

- **Explore gRPC APIs** - List services and methods, describe message types
- **Test gRPC endpoints** - Invoke methods with JSON payloads
- **Debug gRPC applications** - Verbose output with timing information
- **Work offline** - Export and use protoset files without server reflection

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
| Colored output | No | Yes |

*GrpCurl.Net does not support parsing `.proto` files at runtime. Use server reflection or pre-compiled protoset files instead.

## Supported Features

### Commands

- **`list`** - List available services or methods for a specific service
- **`describe`** - Describe services, methods, or message types with optional JSON templates
- **`invoke`** - Invoke gRPC methods with JSON request data

### gRPC Method Types

All four gRPC method types are fully supported:

- **Unary** - Single request, single response
- **Server Streaming** - Single request, stream of responses
- **Client Streaming** - Stream of requests, single response
- **Bidirectional Streaming** - Stream of requests and responses

### Schema Discovery

Two methods for discovering protobuf schemas:

1. **Server Reflection** - Query running servers via the gRPC reflection protocol
2. **Protoset Files** - Use pre-compiled FileDescriptorSet files

### Connection Options

- Plaintext HTTP/2 (`--plaintext`)
- TLS with custom CA certificates (`--cacert`)
- Mutual TLS with client certificates (`--cert`, `--key`)
- Skip certificate verification (`--insecure`)
- Custom authority header (`--authority`)
- Custom server name for TLS (`--servername`)

### Request/Response Options

- Custom headers (`-H`, `--rpc-header`, `--reflect-header`)
- Request data from stdin (`-d @`)
- Emit default values (`--emit-defaults`)
- Allow unknown JSON fields (`--allow-unknown-fields`)
- Connection timeout (`--connect-timeout`)
- Operation timeout (`--max-time`)
- Message size limits (`--max-msg-sz`)

## Requirements

- **.NET 10.0** or later
- A gRPC server with one of:
  - Server reflection enabled, or
  - Pre-compiled protoset files

## Architecture

GrpCurl.Net is built with modern .NET practices:

- **Async/await throughout** - Non-blocking I/O operations
- **IAsyncEnumerable** - Natural streaming support
- **System.CommandLine** - Modern CLI argument parsing
- **Spectre.Console** - Rich terminal output with colors and tables
- **Google.Protobuf** - Official protobuf runtime for .NET

For more details on the internal architecture, see [Architecture](articles/architecture.md).
