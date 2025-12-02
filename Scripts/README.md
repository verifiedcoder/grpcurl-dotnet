# GrpCurl.Net Demo Scripts

This directory contains numbered bash scripts demonstrating all GrpCurl.Net features.

## Prerequisites

### 1. Build GrpCurl.Net

```bash
cd /path/to/GrpCurl.Net
dotnet build
```

The scripts expect the executable at:
`../Src/GrpCurl.Net/bin/Debug/net10.0/GrpCurl.Net`

### 2. Start the TestServer

In a separate terminal:

```bash
cd /path/to/GrpCurl.Net
dotnet run --project Tests/GrpCurl.Net.TestServer
```

The server runs on `localhost:9090` by default.

### 3. Run Scripts

```bash
cd Scripts
./02-list-services.sh
```

## Script Overview

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
| 24-authority-header.sh | Override :authority header |
| 25-user-agent.sh | Set custom User-Agent header |
| **Comprehensive** | |
| 26-all-features-demo.sh | Combined demo of multiple features |

## TestServer Services

The TestServer provides `testing.TestService` with:

- `EmptyCall` - Unary RPC (empty request/response)
- `UnaryCall` - Unary RPC with payload
- `StreamingOutputCall` - Server streaming
- `StreamingInputCall` - Client streaming
- `FullDuplexCall` - Bidirectional streaming (immediate)
- `HalfDuplexCall` - Bidirectional streaming (buffered)

## Troubleshooting

**"Connection refused"**: Ensure TestServer is running on port 9090

**"File not found"**: Run `dotnet build` from the GrpCurl.Net directory

**Permission denied**: Run `chmod +x *.sh` to make scripts executable
