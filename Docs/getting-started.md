# Getting Started

This guide will help you install GrpCurl.Net and make your first gRPC calls.

## Prerequisites

- The current LTS [.NET SDK](https://dotnet.microsoft.com/download). The exact version is pinned by the project's `global.json` and each `.csproj`'s `TargetFramework` — `dotnet --list-sdks` must show a compatible version.
- A gRPC server to test against (with reflection enabled, or a protoset file).

## Installation

### Option 1: Build from Source

Clone the repository and build:

```bash
git clone https://github.com/verifiedcoder/grpcurl-dotnet.git
cd grpcurl-dotnet
dotnet build
```

Run directly with `dotnet run`:

```bash
dotnet run --project Src/GrpCurl.Net -- list --plaintext localhost:9090
```

### Option 2: Publish as Single-File Executable

Create a self-contained single-file executable. Choose the runtime identifier that matches your target platform: `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, or `osx-arm64` (Apple Silicon).

```bash
# Linux x64
dotnet publish Src/GrpCurl.Net -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true

# Apple Silicon
dotnet publish Src/GrpCurl.Net -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true

# Windows x64
dotnet publish Src/GrpCurl.Net -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Replace `Src/GrpCurl.Net` with `Src/Gql2Grpc` to publish the GraphQL bridge instead. The executable lands in `Src/<Project>/bin/Release/<target-framework>/<runtime>/publish/` — the `<target-framework>` folder tracks whatever the `.csproj` declares, so the path stays correct across .NET upgrades.

## Basic Usage

### List Services

List all services available on a gRPC server:

```bash
grpcurl.net list --plaintext localhost:9090
```

Example Output:
```
grpc.reflection.v1alpha.ServerReflection
testing.TestService
testing.UnimplementedService
```

### List Methods

List methods for a specific service:

```bash
grpcurl.net list --plaintext localhost:9090 testing.TestService
```

Example Output:
```
testing.TestService.EmptyCall
testing.TestService.FullDuplexCall
testing.TestService.HalfDuplexCall
testing.TestService.StreamingInputCall
testing.TestService.StreamingOutputCall
testing.TestService.UnaryCall
```

### Describe a Message Type

Get a JSON template for a message type:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
```

Example Output:
```
testing.SimpleRequest is a message:
message SimpleRequest {
  .testing.PayloadType response_type = 1;
  int32 response_size = 2;
  .testing.Payload payload = 3;
  bool fill_username = 4;
  bool fill_oauth_scope = 5;
  .testing.EchoStatus response_status = 7;
}

Message template:
{
  "response_type": "COMPRESSABLE",
  "response_size": 0,
  "payload": {
    "type": "COMPRESSABLE",
    "body": ""
  },
  "fill_username": false,
  "fill_oauth_scope": false,
  "response_status": {
    "code": 0,
    "message": ""
  }
}
```

### Invoke a Method

Call a unary RPC method with JSON data. The TestServer's `UnaryCall` echoes back whatever payload you send:

```bash
grpcurl.net invoke --plaintext \
  -d '{"payload": {"body": "SGVsbG8gV29ybGQ="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

Example Output:
```json
{
  "payload": {
    "body": "SGVsbG8gV29ybGQ="
  }
}
```

PowerShell handles quotes in native command arguments differently from Bash. If inline JSON quoting gives you an error such as `Invalid JSON in request data`, use stdin and quote the literal `@` argument:

```powershell
@'
{"payload": {"body": "SGVsbG8gV29ybGQ="}}
'@ | grpcurl.net invoke --plaintext -d '@' localhost:9090 testing.TestService/UnaryCall
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
| `--cert-password <password>` | Password for PKCS12 client certificate |

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

### GraphQL bridge (gql2grpc)

The same descriptor source and dynamic-invocation engine also power `gql2grpc`, which translates a GraphQL document into one or more gRPC calls and returns a GraphQL response envelope. Convention-based resolution means you can run a query without any mapping file:

```bash
dotnet run --project Src/Gql2Grpc -- \
  --plaintext --default-service testing.TestService \
  localhost:9090 \
  'query { EmptyCall }'
```

Example output:

```json
{
  "data": { "EmptyCall": {} }
}
```

For more complex schemas, supply a mapping file with `--mapping <path>`. See the [mapping reference](articles/gql2grpc-mapping.md) and the [gql2grpc cookbook](articles/gql2grpc-cookbook.md) for worked patterns including subscriptions (server-streaming → NDJSON), fragments, variables, authentication, and schema introspection.

## Next Steps

- [CLI Reference](articles/cli-reference.md): complete reference for `list`, `describe`, `invoke`, and `gql2grpc`
- [Examples](articles/examples.md): more GrpCurl.Net scenarios
- [Gql2Grpc cookbook](articles/gql2grpc-cookbook.md): ready-to-run GraphQL patterns
- [Troubleshooting](articles/troubleshooting.md): diagnose common errors
- [Architecture](articles/architecture.md): internal design and extension points
