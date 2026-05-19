# Learn Protocol Buffers with GrpCurl.Net

This tutorial series teaches you Protocol Buffers (protobuf) from the ground up, using GrpCurl.Net as your hands-on exploration tool. Whether you have never seen a `.proto` file or you want to deepen your understanding of advanced protobuf features, this series has you covered.

Rather than just reading about protobuf in the abstract, every chapter includes real commands you can run against a live gRPC server (you can use the test server in this repository). You will see how protobuf schemas translate into JSON requests and responses, how different field types behave "on the wire", and how GrpCurl.Net bridges the gap between human-readable JSON and compact binary encoding.

## Learning Path

The series is organised into four progressive tiers. Each tier builds on the previous one, so working through the chapters in order is recommended.

### Foundations (Chapters 1-2)

| Chapter | Title | What You Will Learn |
|---------|-------|---------------------|
| [1. What is Protocol Buffers?](01-what-is-protobuf.md) | Conceptual introduction | What protobuf is, why it exists, schema-first design, proto3 syntax basics |
| [2. Your First gRPC Call](02-getting-started.md) | Hands-on quickstart | Starting the TestServer, listing services, describing messages, invoking your first RPC |

### Core Types (Chapters 3-5)

| Chapter | Title | What You Will Learn |
|---------|-------|---------------------|
| [3. Scalar Types](03-scalar-types.md) | Primitive field types | Integers, floats, booleans, strings, bytes, and how each maps to JSON |
| [4. Enums](04-enums.md) | Enumeration types | Defining enums, zero values, aliases, and JSON representation |
| [5. Composing Messages](05-nested-messages.md) | Nested and referenced messages | Message composition, nested types, reusing message definitions |

### Complex Types (Chapters 6-8)

| Chapter | Title | What You Will Learn |
|---------|-------|---------------------|
| [6. Collections](06-collections.md) | Repeated fields and maps | Lists, packed encoding, map fields, and their JSON representations |
| [7. Oneof Fields](07-oneof-fields.md) | Mutually exclusive fields | Oneof semantics, JSON encoding, and practical use cases |
| [8. Well-Known Types](08-well-known-types.md) | Google standard types | Timestamp, Duration, wrappers, Any, Struct, and their canonical JSON forms |

### Advanced (Chapters 9-11)

| Chapter | Title | What You Will Learn |
|---------|-------|---------------------|
| [9. Services and Streaming](09-services-and-streaming.md) | gRPC service definitions | Unary, server streaming, client streaming, bidirectional streaming |
| [10. Default Values and JSON Mapping](10-default-values-and-json.md) | Serialization details | Default value rules, field presence, proto3 JSON mapping specification |
| [11. Schema Management](11-schema-management.md) | Evolution and tooling | Backward/forward compatibility, field number rules, protoset files, reflection |

## Prerequisites

Before starting this series, make sure you have the following:

- **[.NET 10.0 SDK](https://dotnet.microsoft.com/download)** or later installed on your machine
- **The GrpCurl.Net repository** cloned and built:

```bash
git clone https://github.com/verifiedcoder/grpcurl-dotnet.git
cd grpcurl-dotnet
dotnet build
```

- **Basic terminal knowledge** -- you should be comfortable running commands in a terminal (bash, PowerShell, or your shell of choice)

No prior experience with protobuf or gRPC is required. That is what this series is for.

## How to Use This Series

### Start the TestServer

Most chapters require a running gRPC server. The GrpCurl.Net repository includes a TestServer that exposes a variety of protobuf message types and RPC methods, making it an ideal sandbox.

Start it in a separate terminal:

```bash
dotnet run --project Tests/GrpCurl.Net.TestServer
```

The server will start listening on `localhost:9090` with server reflection enabled.

### Follow the Chapters in Order

Each chapter introduces new concepts and builds on what came before. The hands-on commands in later chapters assume familiarity with techniques covered earlier (such as using `grpcurl.net describe` and `grpcurl.net invoke`).

### Run the Commands

Every chapter includes `grpcurl.net` commands that you can copy and run directly. For example:

```bash
grpcurl.net list --plaintext localhost:9090
```

If you have not published or installed a `grpcurl.net` executable on your PATH yet, run the same command from the repository checkout with `dotnet run` and place the command arguments after `--`:

```bash
dotnet run --project Src/GrpCurl.Net -- list --plaintext localhost:9090
```

Most JSON examples use POSIX shell quoting, such as `-d '{"field": "value"}'`. In PowerShell, use stdin and quote the literal `@` argument:

```powershell
@'
{"field": "value"}
'@ | grpcurl.net invoke --plaintext -d '@' localhost:9090 package.Service/Method
```

Experiment freely -- modify the JSON payloads, try different flags, and observe how the output changes. Hands-on exploration is the fastest way to internalise protobuf concepts.

> [!NOTE]
> This series uses `grpcurl.net`, which is the .NET implementation of grpcurl. It is command-compatible with the Go-based `grpcurl` tool but runs on .NET and provides additional features such as detailed timing output. All command examples use the installed-tool form; source-tree users can replace `grpcurl.net` with `dotnet run --project Src/GrpCurl.Net --`.

## Chapter Index

1. **[What is Protocol Buffers?](01-what-is-protobuf.md)** -- Understand what protobuf is, how it compares to JSON and XML, and why it matters for gRPC.

2. **[Your First gRPC Call](02-getting-started.md)** -- Get the TestServer running and make your first list, describe, and invoke calls with GrpCurl.Net.

3. **[Scalar Types](03-scalar-types.md)** -- Learn every primitive type protobuf offers: integers of various sizes, floating-point numbers, booleans, strings, and bytes.

4. **[Enums](04-enums.md)** -- Define named constants with enumeration types and understand how they serialise to JSON.

5. **[Composing Messages](05-nested-messages.md)** -- Build complex data structures by nesting messages inside other messages.

6. **[Collections, Repeated Fields and Maps](06-collections.md)** -- Work with lists of values using repeated fields and key-value associations using map fields.

7. **[Oneof Fields](07-oneof-fields.md)** -- Model mutually exclusive fields where only one of several options can be set at a time.

8. **[Well-Known Types](08-well-known-types.md)** -- Use Google's standard protobuf types for timestamps, durations, nullable wrappers, and dynamic JSON-like structures.

9. **[Services and Streaming](09-services-and-streaming.md)** -- Define gRPC services and understand all four streaming patterns: unary, server streaming, client streaming, and bidirectional.

10. **[Default Values and JSON Mapping](10-default-values-and-json.md)** -- Master proto3's default value semantics, field presence rules, and the official JSON mapping specification.

11. **[Schema Management](11-schema-management.md)** -- Learn how to evolve protobuf schemas safely, manage backward and forward compatibility, and work with protoset files.
