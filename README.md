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
grpcurl list --plaintext localhost:9090

# Describe a service
grpcurl describe --plaintext localhost:9090 my.package.Service

# Invoke a method
grpcurl invoke --plaintext -d '{"name": "World"}' localhost:9090 my.package.Service/SayHello
```

## Documentation

The documentation is a [DocFx](https://github.com/dotnet/docfx) project, so you can serve a self-contained local documentation site.

- [Introduction](Docs/introduction.md) - Learn about GrpCurl.Net and its capabilities
- [Getting Started](Docs/getting-started.md) - Installation and first steps
- [CLI Reference](Docs/articles/cli-reference.md) - Complete command reference
- [Examples](Docs/articles/examples.md) - Usage examples for common scenarios
- [Architecture](Docs/articles/architecture.md) - Internal design and extensibility
- [API Reference](Docs/api/GrpCurl.Net.DescriptorSources.yml) - Public API documentation

## Requirements

- .NET 10.0 or later
- Target gRPC server with reflection enabled (or protoset files)
