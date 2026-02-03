# Getting Started

This guide will help you install GrpCurl.Net and make your first gRPC calls.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- A gRPC server to test against (with reflection enabled, or protoset files)

## Installation

### Option 1: Build from Source

Clone the repository and build:

```bash
git clone https://github.com/your-repo/GrpCurl.Net.git
cd GrpCurl.Net
dotnet build
```

Run directly with `dotnet run`:

```bash
dotnet run --project Src/GrpCurl.Net -- list --plaintext localhost:9090
```

### Option 2: Publish as Single-File Executable

Create a self-contained single-file executable:

```bash
# Linux
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true

# Windows
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# macOS
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
```

The executable will be in `Src/GrpCurl.Net/bin/Release/net10.0/<runtime>/publish/`.

## Basic Usage

### List Services

List all services available on a gRPC server:

```bash
grpcurl.net list --plaintext localhost:9090
```

Output:
```
┌──────────────────────────────────────────────────┐
│ Service                                          │
├──────────────────────────────────────────────────┤
│ grpc.reflection.v1alpha.ServerReflection         │
│ testing.TestService                              │
└──────────────────────────────────────────────────┘

Total: 2 service(s)
```

### List Methods

List methods for a specific service:

```bash
grpcurl.net list --plaintext localhost:9090 testing.TestService
```

Output:
```
┌────────────────────────┬──────────────────┬─────────────────────────────────┬────────────────────────────────────┐
│ Method                 │ Type             │ Input                           │ Output                             │
├────────────────────────┼──────────────────┼─────────────────────────────────┼────────────────────────────────────┤
│ EmptyCall              │ Unary            │ testing.Empty                   │ testing.Empty                      │
│ UnaryCall              │ Unary            │ testing.SimpleRequest           │ testing.SimpleResponse             │
│ StreamingOutputCall    │ Server Streaming │ testing.StreamingOutputCallReq  │ testing.StreamingOutputCallResp    │
│ StreamingInputCall     │ Client Streaming │ testing.StreamingInputCallReq   │ testing.StreamingInputCallResp     │
│ FullDuplexCall         │ Bidi Streaming   │ testing.StreamingOutputCallReq  │ testing.StreamingOutputCallResp    │
│ HalfDuplexCall         │ Bidi Streaming   │ testing.StreamingOutputCallReq  │ testing.StreamingOutputCallResp    │
└────────────────────────┴──────────────────┴─────────────────────────────────┴────────────────────────────────────┘
```

### Describe a Message Type

Get a JSON template for a message type:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
```

Output:
```json
{
  "responseType": "COMPRESSABLE",
  "responseSize": 0,
  "payload": {
    "type": "COMPRESSABLE",
    "body": ""
  },
  "fillUsername": false,
  "fillOauthScope": false,
  "responseStatus": {
    "code": 0,
    "message": ""
  }
}
```

### Invoke a Method

Call a unary RPC method with JSON data:

```bash
grpcurl.net invoke --plaintext \
  -d '{"response_size": 10, "fill_username": true}' \
  localhost:9090 testing.TestService/UnaryCall
```

Output:
```json
{
  "payload": {
    "body": "AAAAAAAAAA"
  },
  "username": "test-user"
}
```

## Common Options

### Connection Options

| Option | Description |
|--------|-------------|
| `--plaintext` | Use HTTP/2 without TLS |
| `--insecure` | Skip TLS certificate verification |
| `--cacert <path>` | CA certificate for server validation |
| `--cert <path>` | Client certificate for mTLS |
| `--key <path>` | Client private key for mTLS |

### Output Options

| Option | Description |
|--------|-------------|
| `-v`, `--verbose` | Show verbose output |
| `--vv`, `--very-verbose` | Show detailed timing information |
| `--emit-defaults` | Include default values in JSON output |

### Request Options

| Option | Description |
|--------|-------------|
| `-d <json>` | Request data as JSON |
| `-d @` | Read request data from stdin |
| `-H <header>` | Add custom header (format: `name: value`) |

## Next Steps

- [CLI Reference](articles/cli-reference.md) - Complete command and option reference
- [Examples](articles/examples.md) - More usage examples
- [Architecture](articles/architecture.md) - Learn about the internal design
