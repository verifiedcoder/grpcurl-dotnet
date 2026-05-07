---
_layout: landing
---

# GrpCurl.Net

A .NET implementation of grpcurl, a command-line tool for interacting with gRPC servers, plus `gql2grpc` — a GraphQL-to-gRPC bridge built on the same core.

## Overview

GrpCurl.Net lets you interact with gRPC servers using JSON requests instead of binary protocol buffers. It supports server reflection, protoset files, and dynamic method invocation for all four gRPC method types. The companion tool `gql2grpc` translates GraphQL operations to gRPC calls, emitting spec-compliant GraphQL response envelopes.

## Key Features

- **Server Reflection**: discover services and methods at runtime
- **Protoset Support**: pre-compiled descriptor files for offline operation
- **All Streaming Types**: unary, server-streaming, client-streaming, bidirectional
- **Rich CLI**: verbose output, phase timings, and coloured terminal display
- **TLS/mTLS**: full support for secure connections and mutual authentication
- **Cross-Platform**: Windows, Linux, macOS (x64 and arm64)
- **GraphQL Bridge (`gql2grpc`)**: queries, mutations, subscriptions, fragments, aliases, variables, FieldMask projection, schema introspection, NDJSON streaming output

## Quick Start

```bash
# List services on a gRPC server
grpcurl.net list --plaintext localhost:9090

# Describe a service
grpcurl.net describe --plaintext localhost:9090 my.package.Service

# Invoke a method
grpcurl.net invoke --plaintext -d '{"name": "World"}' localhost:9090 my.package.Service/SayHello

# Run a GraphQL query via the bridge
gql2grpc --plaintext --default-service my.package.Service localhost:9090 \
  'query { SayHello(input: { name: "World" }) { greeting } }'
```

## Documentation

- [Introduction](introduction.md): Learn about GrpCurl.Net, gql2grpc, and their capabilities
- [Getting Started](getting-started.md): Installation and first steps
- [CLI Reference](articles/cli-reference.md): Complete command reference for `list`, `describe`, `invoke`, and `gql2grpc`
- [Examples](articles/examples.md): Worked scenarios for GrpCurl.Net
- [Gql2Grpc mapping file](articles/gql2grpc-mapping.md): Schema for the YAML/JSON mapping that drives GraphQL → gRPC translation
- [Gql2Grpc cookbook](articles/gql2grpc-cookbook.md): Worked GraphQL patterns with curl-paste commands
- [Troubleshooting](articles/troubleshooting.md): Common errors and fixes
- [Authentication recipes](articles/authentication.md): Bearer tokens, API keys, cookie auth, mTLS
- [CI/CD integration](articles/ci-cd.md): Exit codes, bash patterns, GitHub Actions / GitLab CI
- [Architecture](articles/architecture.md): Internal design and extensibility
- [Learn Protobuf](articles/learn-protobuf/index.md): Tutorial series — learn protobuf from scratch
- [API Reference](api-reference.md): Public library API
- [Gql2Grpc future work](articles/gql2grpc-future-work.md): Deferred backlog

## Requirements

- The current LTS .NET SDK (see [`global.json`](https://github.com/verifiedcoder/grpcurl-dotnet/blob/main/global.json) and each `.csproj`'s `TargetFramework` for the exact version).
- Target gRPC server with reflection enabled, or a pre-compiled protoset file.
