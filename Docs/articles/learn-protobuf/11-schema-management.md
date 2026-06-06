# Schema Management with Protoset Files

Throughout this series, every command you have run has relied on **server reflection** -- GrpCurl.Net connects to a live gRPC server and asks it to describe its own services. This works well for development and ad-hoc exploration, but what about environments where you cannot reach the server? What about CI/CD pipelines, air-gapped networks, or situations where you want a stable, versioned snapshot of a schema?

This is where **protoset files** come in. They are the key to offline protobuf operations, and they are the final tool in your GrpCurl.Net toolkit.

## What Are Protoset Files?

A protoset file (also called a **descriptor set**) is a binary file containing one or more serialised `FileDescriptorProto` messages, wrapped in a `FileDescriptorSet`. In plain terms, it is a **binary snapshot of your `.proto` schema** -- every message, enum, service, and method definition, serialised into protobuf's own binary format.

Think of it this way:

| Source | Format | Human-Readable | Machine-Readable |
|--------|--------|----------------|-----------------|
| `.proto` files | Text | Yes | Requires parsing |
| Server reflection | Network protocol | No | Yes (live only) |
| Protoset file | Binary protobuf | No | Yes (offline) |

A protoset file captures the same information as your `.proto` files but in a format that tools can consume instantly without parsing text or connecting to a running server.

## Why Use Protoset Files?

### 1. Offline Operations

With a protoset file, you can list services, describe messages, and generate templates without any network access:

```bash
# No server needed for these commands
grpcurl.net list --protoset service.protoset
grpcurl.net describe --protoset service.protoset testing.TestService
grpcurl.net describe --protoset service.protoset --msg-template testing.SimpleRequest
```

### 2. CI/CD Pipelines

Build pipelines often need to validate gRPC contracts, generate documentation, or verify backward compatibility. A protoset file checked into version control provides a reliable, reproducible schema source that does not depend on a running server.

### 3. Air-Gapped Environments

In secure environments where development machines cannot reach production servers, a protoset file exported from one environment can be carried to another.

### 4. Schema Caching and Performance

Server reflection adds a network round-trip before the first call. With a protoset file, GrpCurl.Net skips reflection entirely, which can noticeably speed up scripted workflows that make many calls.

### 5. Schema Snapshots for Debugging

When investigating a production issue, having the exact schema from the time of the incident (not the current version) can be invaluable. Protoset files serve as point-in-time snapshots.

## Exporting a Protoset from a Running Server

The `--protoset-out` flag tells GrpCurl.Net to save the schema it discovers via server reflection to a file:

```bash
grpcurl.net list --plaintext --max-time 10s --protoset-out service.protoset localhost:9090
```

This command does two things:

1. Lists all services on the server (the normal `list` behavior)
2. Saves the complete schema to `service.protoset`

The exported file contains all services, messages, enums, and their dependencies -- everything needed to work with the server's API offline.

`--protoset-out` refuses to overwrite an existing file unless you pass `--force`. When exporting from reflection in scripts, pair it with `--max-time` so discovery has an explicit total deadline.

### Verifying the Export

After exporting, verify the protoset file works by using it without a server connection:

```bash
grpcurl.net list --protoset service.protoset
```

Expected output:

```
grpc.reflection.v1alpha.ServerReflection
testing.TestService
testing.UnimplementedService
```

The services are listed without any network access. The protoset file contains everything GrpCurl.Net needs.

## Creating Protoset Files with protoc

If you have the `.proto` source files, you can also create protoset files using the `protoc` compiler. This is common in build systems where the proto sources are available. To try it with this repository's own schema, run from the repo root:

```bash
protoc --descriptor_set_out=service.protoset \
  --include_imports \
  --proto_path=Tests/GrpCurl.Net.TestServer/Protos \
  test.proto
```

| Flag | Purpose |
|------|---------|
| `--descriptor_set_out` | Output path for the protoset file |
| `--include_imports` | Include all imported `.proto` files (critical -- without this, imported types are missing) |
| `--proto_path` | Directory to search for `.proto` files |

> **Important:** Always use `--include_imports`. Without it, the protoset file will not contain the definitions of imported types (like well-known types), and tools will fail to resolve references.

## Using Protoset Files with GrpCurl.Net

Once you have a protoset file, it works with every GrpCurl.Net command.

### Listing Services (Offline)

```bash
grpcurl.net list --protoset service.protoset
```

No server address is needed. The protoset file is the sole source of schema information.

### Listing Methods (Offline)

```bash
grpcurl.net list --protoset service.protoset testing.TestService
```

Expected output:

```
testing.TestService.EmptyCall
testing.TestService.FullDuplexCall
testing.TestService.HalfDuplexCall
testing.TestService.StreamingInputCall
testing.TestService.StreamingOutputCall
testing.TestService.UnaryCall
```

### Describing Services and Messages (Offline)

```bash
grpcurl.net describe --protoset service.protoset testing.TestService
```

```bash
grpcurl.net describe --protoset service.protoset --msg-template testing.SimpleRequest
```

These commands produce the same output as their server-reflection counterparts but without any network access.

### Invoking RPCs (Server Still Required)

For actual RPC calls, you still need a running server -- the protoset file provides the schema, but the server processes the request:

```bash
grpcurl.net invoke --plaintext --protoset service.protoset \
  -d '{"payload": {"body": "SGVsbG8gV29ybGQ="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

The response echoes back the payload we sent. GrpCurl.Net uses the protoset file for schema resolution instead of querying the server's reflection endpoint.

### Streaming with Protoset Files

Protoset files work with all four streaming patterns:

```bash
# Server streaming
grpcurl.net invoke --plaintext --protoset service.protoset \
  -d '{"responseParameters": [{"size": 10}, {"size": 20}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall

# Client streaming
echo '{"payload":{"body":"YQ=="}}
{"payload":{"body":"YmI="}}' | \
grpcurl.net invoke --plaintext --protoset service.protoset \
  --max-stdin-bytes 1048576 -d @ localhost:9090 testing.TestService/StreamingInputCall

# Bidirectional streaming
echo '{"responseParameters": [{"size": 10}]}
{"responseParameters": [{"size": 20}]}' | \
grpcurl.net invoke --plaintext --protoset service.protoset \
  --max-stdin-bytes 1048576 -d @ localhost:9090 testing.TestService/FullDuplexCall
```

## Working with Multiple Protoset Files

When your system spans multiple services defined in separate protoset files, you can specify more than one:

```bash
grpcurl.net invoke --plaintext \
  --protoset testing.protoset \
  --protoset wkttesting.protoset \
  -d '{"payload": {"body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

GrpCurl.Net merges the schemas from all specified protoset files. This is useful when:

- Different teams own different services and export their schemas separately
- You want to keep protoset files small and focused on individual services
- Different services were exported at different times

### Listing Across Multiple Protosets

```bash
grpcurl.net list --protoset testing.protoset --protoset wkttesting.protoset
```

This shows services from both protoset files combined.

## Schema Versioning

Protoset files fit naturally into version control workflows. Because they are binary snapshots, they capture the exact schema at a point in time.

### A Versioning Workflow

**Step 1: Export the schema from your staging environment**

```bash
grpcurl.net list --plaintext --max-time 10s --protoset-out schemas/v1.0.0.protoset staging-server:9090
```

**Step 2: Commit to version control**

```bash
git add schemas/v1.0.0.protoset
git commit -m "Capture schema v1.0.0 from staging"
```

**Step 3: Use the versioned schema in CI/CD**

Your pipeline can use the committed protoset file to validate requests, generate documentation, or run contract tests without needing a live server.

**Step 4: Compare schema versions**

When you export a new version, you can compare the two schemas by describing them side by side:

```bash
# Describe the old schema
grpcurl.net describe --protoset schemas/v1.0.0.protoset testing.SimpleRequest

# Describe the new schema
grpcurl.net describe --protoset schemas/v1.1.0.protoset testing.SimpleRequest
```

Differences in the output reveal added, removed, or changed fields.

### Naming Conventions

Some common approaches to organizing protoset files:

| Convention | Example | Best For |
|-----------|---------|----------|
| By version | `schemas/v1.0.0.protoset` | Release-based versioning |
| By service | `schemas/test-service.protoset` | Service-per-team ownership |
| By date | `schemas/2024-01-15.protoset` | Daily snapshots |
| By environment | `schemas/staging.protoset` | Environment-specific schemas |

## Hands-On Workflow: Export, Inspect, Version, Reuse

Let us walk through a complete workflow that demonstrates the practical value of protoset files.

### 1. Export the Schema

Start by capturing the schema from the running TestServer:

```bash
grpcurl.net list --plaintext --max-time 10s --protoset-out testserver.protoset localhost:9090
```

### 2. Inspect the Schema Offline

Now stop thinking of the server -- everything from here can be done offline:

```bash
# What services are available?
grpcurl.net list --protoset testserver.protoset

# What methods does TestService offer?
grpcurl.net list --protoset testserver.protoset testing.TestService

# What does a SimpleRequest look like?
grpcurl.net describe --protoset testserver.protoset --msg-template testing.SimpleRequest

# What about the streaming request?
grpcurl.net describe --protoset testserver.protoset --msg-template testing.StreamingOutputCallRequest

# Explore the well-known types message (requires the separate WKT protoset)
grpcurl.net describe --protoset Tests/TestProtosets/well-known-types.protoset --msg-template wkttesting.WellKnownTypesMessage
```

### 3. Use the Schema for Calls

When you do need to make a call, the protoset file eliminates the reflection lookup:

```bash
grpcurl.net invoke --plaintext --protoset testserver.protoset \
  -d '{"payload": {"body": "SGVsbG8gV29ybGQ="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

### 4. Version and Share

The protoset file can now be:

- **Committed to git** alongside your source code
- **Published to an artifact repository** for other teams
- **Included in Docker images** for production tooling
- **Attached to release notes** as a schema reference

## When to Use Protoset Files vs. Server Reflection

| Scenario | Best Choice |
|----------|-------------|
| Ad-hoc exploration of a running server | Server reflection |
| CI/CD pipeline that needs schema access | Protoset file |
| Air-gapped or restricted network environment | Protoset file |
| Server does not support reflection | Protoset file |
| Scripted workflows making many calls | Protoset file (faster) |
| Comparing schema versions over time | Protoset files |
| Quick debugging during development | Server reflection |

Both approaches use the same underlying schema information. The choice is about where that information comes from: the network (reflection) or a file (protoset).

## Safety Limits and Generated Files

GrpCurl.Net treats descriptor input as untrusted. Local protoset files are capped at 64 MiB each before they are read, and reflection descriptor responses are capped at 16 MiB by default. Descriptor loading also limits the retained graph to 2,048 files, 65,536 symbols, and an import dependency depth of 128.

When you export schemas, `--protoset-out` creates the file only if it does not already exist; use `--force` for intentional replacement. If you reconstruct `.proto` files with `--proto-out-dir`, descriptor file names must be relative and must stay inside the chosen output directory. Rooted paths and `..` traversal are rejected.

## Series Conclusion

Congratulations -- you have completed the Learn Protocol Buffers series. Over the course of eleven chapters, you have progressed from the fundamentals to advanced topics:

1. **What is Protocol Buffers?** -- You learned what protobuf is, why it exists, and how it compares to JSON and XML
2. **Your First gRPC Call** -- You started the TestServer and made your first `list`, `describe`, and `invoke` calls
3. **Scalar Types** -- You explored every primitive type: integers, floats, booleans, strings, and bytes
4. **Enums** -- You defined named constants and understood their JSON representation
5. **Composing Messages** -- You built complex data structures by nesting messages
6. **Collections** -- You worked with repeated fields (lists) and map fields (dictionaries)
7. **Oneof Fields** -- You modeled mutually exclusive choices within a message
8. **Well-Known Types** -- You mastered Timestamp, Duration, wrapper types, Any, Struct, and the rest of Google's standard library
9. **Services and Streaming** -- You understood all four RPC patterns and sent streaming requests with GrpCurl.Net
10. **Default Values and JSON Mapping** -- You learned the serialization rules that govern how protobuf maps to JSON
11. **Schema Management** -- You captured schemas as protoset files for offline use, versioning, and CI/CD

### What to Explore Next

With this foundation, you are well-equipped to:

- **Build gRPC services** in your language of choice (C#, Go, Java, Python, and more)
- **Design protobuf schemas** that are clean, extensible, and backward-compatible
- **Debug gRPC APIs** efficiently using GrpCurl.Net's full toolkit
- **Integrate gRPC into CI/CD** pipelines using protoset files and scripted workflows
- **Contribute to the GrpCurl.Net project** with a deep understanding of the protobuf ecosystem

The combination of protobuf's efficient serialization, gRPC's powerful transport, and GrpCurl.Net's command-line accessibility gives you a complete toolkit for modern API development. Happy building.
