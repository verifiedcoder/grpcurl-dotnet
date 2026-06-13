# What is Protocol Buffers?

Protocol Buffers, commonly known as **protobuf** and abbreviated to **proto**, is a language-neutral, platform-neutral mechanism for serializing structured data. Originally developed at Google, protobuf is the data format at the heart of gRPC, and understanding it is essential for working with gRPC services effectively.

This chapter introduces protobuf conceptually. By the end, you will understand what protobuf is, how it compares to other serialization formats, and how GrpCurl.Net lets you work with protobuf services using familiar JSON.

## Why Protocol Buffers Exist

In the early 2000s, Google needed a way to define and serialise data structures that could be shared across hundreds of services written in different programming languages. Their existing approaches, ad-hoc text formats and early binary protocols, did not scale well. They needed something that was:

- **Compact**: network bandwidth and storage are finite resources at Google's scale
- **Fast**: serialization and deserialization should be as cheap as possible
- **Evolvable**: schemas change constantly, and services cannot all be updated simultaneously
- **Language-neutral**: engineers use C++, Java, Python, Go, and many other languages

Protocol Buffers was the answer. It has since been open-sourced and become the standard serialization format for gRPC, the high-performance RPC framework that is now widely used across the industry.

## The Schema-First Approach

The most important concept in protobuf is **schema-first design**. You define your data structures in a `.proto` file before writing any application code. A compiler (`protoc`) then generates strongly-typed code in your target language from that schema.

This is fundamentally different from schema-less formats like JSON, where the structure of data is implicit and must be validated at runtime.

### Comparing Serialization Formats

To illustrate the difference, consider representing a simple request with three fields: a response size (integer), a flag (boolean), and a payload type (a value chosen from a fixed set of options).

**JSON (schema-less)**

```json
{
  "response_size": 256,
  "fill_username": true,
  "response_type": "COMPRESSABLE"
}
```

JSON is human-readable and ubiquitous, but it has no built-in schema. The producer and consumer must agree on field names and types out of band. A typo in a field name (`reponse_size` instead of `response_size`) goes undetected until runtime, if it is detected at all.

**XML (verbose schema)**

```xml
<SimpleRequest xmlns="urn:testing">
  <responseSize>256</responseSize>
  <fillUsername>true</fillUsername>
  <responseType>COMPRESSABLE</responseType>
</SimpleRequest>
```

XML supports schemas (XSD), but the format is verbose. Tags are repeated for opening and closing, and the text encoding adds significant overhead. An XML representation of the same data can be 5-10 times larger than a protobuf encoding.

**Protocol Buffers (typed schema)**

```protobuf
message SimpleRequest {
  PayloadType response_type = 1;
  int32 response_size = 2;
  bool fill_username = 4;
}
```

The `.proto` file defines the schema precisely: field names, types, and unique numeric identifiers. The protobuf binary encoding of this message is typically 3-10 times smaller than the equivalent JSON, and serialization is significantly faster because the format is not text-based.

| Feature | JSON | XML | Protobuf |
|---------|------|-----|----------|
| Human-readable | Yes | Yes | No (binary) |
| Schema required | No | Optional (XSD) | Yes (.proto) |
| Type safety | No | Partial | Full |
| Relative size | Baseline | 1.5-3x larger | 3-10x smaller |
| Serialization speed | Moderate | Slow | Fast |
| Code generation | Optional | Optional | Built-in |
| Backward compatibility | Ad-hoc | Ad-hoc | Designed-in |

## Key Advantages of Protocol Buffers

### Type Safety

Every field has a declared type. The compiler catches type mismatches at build time, not at runtime. You cannot accidentally send a string where an integer is expected.

### Compact Binary Format

Protobuf uses a binary wire format that encodes data far more efficiently than text-based formats. Integer values use variable-length encoding (small numbers use fewer bytes), field names are replaced by small numeric tags, and default values are omitted entirely.

A typical protobuf message is **3-10 times smaller** than its JSON equivalent. At scale, this reduces network bandwidth, storage costs, and serialization overhead.

### Code Generation

From a single `.proto` file, the `protoc` compiler generates idiomatic code for many languages: C#, Java, Python, Go, C++, Ruby, and more. Each generated client and server agrees on the exact data format, eliminating an entire class of serialization bugs.

### Backward and Forward Compatibility

Protobuf is designed for schema evolution. You can add new fields to a message without breaking existing consumers -- they simply ignore fields they do not recognize. You can remove fields (by reserving their numbers) without breaking existing producers. This makes it practical to evolve APIs in large distributed systems where not all services can be updated simultaneously.

## Proto3 Syntax: Your First .proto File

Protobuf schemas are written in `.proto` files using a domain-specific language. The current version is **proto3**, which simplified the syntax compared to proto2. Let us examine a real example from the `testing.TestService` used throughout this tutorial series:

```protobuf
syntax = "proto3";

package testing;

message SimpleRequest {
  PayloadType response_type = 1;
  int32 response_size = 2;
  Payload payload = 3;
  bool fill_username = 4;
  bool fill_oauth_scope = 5;
  EchoStatus response_status = 7;
}
```

Let us break down each part.

### The `syntax` Declaration

```protobuf
syntax = "proto3";
```

This must be the first non-empty, non-comment line in the file. It tells the protobuf compiler which version of the language to use. If omitted, `proto2` is assumed. All modern protobuf development uses `proto3`.

### The `package` Declaration

```protobuf
package testing;
```

The package provides a namespace for the messages and services defined in the file. It prevents naming collisions when different `.proto` files define messages with the same name. In gRPC, the fully qualified service name includes the package -- for example, `testing.TestService`.

### The `message` Definition

```protobuf
message SimpleRequest {
  // fields go here
}
```

A `message` is the fundamental unit of structured data in protobuf. It is analogous to a `class` in C# or a `struct` in Go. Messages contain typed fields, and messages can reference other messages to build complex data structures.

### Fields: Types and Numbers

Each field in a message has three components:

```
<type> <name> = <number>;
```

Looking at the `SimpleRequest` fields:

| Type | Name | Number | Description |
|------|------|--------|-------------|
| `PayloadType` | `response_type` | 1 | An enum type (defined elsewhere in the file) |
| `int32` | `response_size` | 2 | A 32-bit signed integer |
| `Payload` | `payload` | 3 | A nested message type |
| `bool` | `fill_username` | 4 | A boolean |
| `bool` | `fill_oauth_scope` | 5 | A boolean |
| `EchoStatus` | `response_status` | 7 | A nested message type |

**Field numbers** are the most distinctive feature of protobuf. Notice that the numbers are not sequential -- there is no field number 6. This is perfectly valid. Field numbers serve as unique identifiers in the binary encoding. They are what gets written to the wire, not the field names. This is a key reason protobuf is so compact and why schemas can evolve safely:

- The field **name** is for human readability in the `.proto` file and in generated code. It never appears in the binary encoding.
- The field **number** is what identifies the field in the binary wire format. Once assigned, a field number should never be changed or reused.

> [!IMPORTANT]
> Field numbers 1-15 are encoded in a single byte (including the type tag), while numbers 16-2047 take two bytes. For frequently used fields, prefer numbers in the 1-15 range for optimal encoding efficiency.

## The Wire Format at a High Level

When a protobuf message is serialised, each field is encoded as a pair: a **tag** (combining the field number and wire type) followed by the **value**.

There are several wire types:

| Wire Type | ID | Used For |
|-----------|-----|----------|
| Varint | 0 | int32, int64, uint32, uint64, sint32, sint64, bool, enum |
| 64-bit | 1 | fixed64, sfixed64, double |
| Length-delimited | 2 | string, bytes, nested messages, packed repeated fields |
| 32-bit | 5 | fixed32, sfixed32, float |

For example, when encoding `response_size = 256` (field number 2, type int32):

1. The **tag** encodes field number 2 and wire type 0 (varint): a single byte `0x10`
2. The **value** encodes 256 as a varint: two bytes `0x80 0x02`

Total: **3 bytes** for a field that would take `"response_size": 256` (21 bytes including quotes and colon) in JSON. This illustrates why protobuf is so much more compact.

You do not need to understand the wire format in detail to use protobuf effectively. GrpCurl.Net handles all encoding and decoding for you. But knowing that field numbers (not names) are what travel on the wire helps explain why protobuf's compatibility guarantees work the way they do.

## How GrpCurl.Net Fits In

In a typical gRPC workflow, you would:

1. Write a `.proto` file defining your messages and services
2. Run `protoc` to generate client code in your language
3. Use the generated client to make strongly-typed RPC calls

This workflow is powerful for production applications, but it is cumbersome when you just want to **explore** a gRPC API, **test** an endpoint, or **debug** a service. Generating client code just to send a single request is overkill.

**GrpCurl.Net eliminates the code generation step.** It uses [server reflection](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md) (or protoset files) to discover the protobuf schema at runtime, then translates between JSON and protobuf binary format on the fly. You write requests as JSON, and GrpCurl.Net handles the encoding.

### The Relationship

The flow looks like this:

```
.proto file
    |
    v
Defines schema (messages, services, field types and numbers)
    |
    v
gRPC server implements the service using generated code
    |
    v
Server exposes schema via reflection
    |
    v
GrpCurl.Net discovers schema at runtime
    |
    v
You send JSON --> GrpCurl.Net encodes to protobuf --> Server processes --> Server responds with protobuf --> GrpCurl.Net decodes to JSON --> You read JSON
```

For example, you can describe the `SimpleRequest` message we examined above by running:

```bash
grpcn describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
```

GrpCurl.Net queries the server's reflection endpoint, retrieves the `SimpleRequest` descriptor, and outputs the proto definition followed by a JSON template showing all fields with their default values:

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

The template uses the proto `snake_case` field names (e.g., `response_size`, `fill_username`). GrpCurl.Net accepts both `snake_case` and `camelCase` forms as input.

You can then use that JSON as a starting point for an actual RPC call. The TestServer's `UnaryCall` method echoes back whatever payload you send:

```bash
grpcn invoke --plaintext \
  -d '{"payload": {"body": "SGVsbG8gV29ybGQ="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

GrpCurl.Net takes your JSON, encodes it as a protobuf `SimpleRequest` binary message, sends it to the server over gRPC, receives the protobuf `SimpleResponse` binary message, decodes it back to JSON, and prints the result:

```json
{
  "payload": {
    "body": "SGVsbG8gV29ybGQ="
  }
}
```

The `body` field is `bytes`, so the value is Base64-encoded -- `"SGVsbG8gV29ybGQ="` decodes to the text `"Hello World"`. All of this happens without generating a single line of client code.

> [!TIP]
> GrpCurl.Net accepts both `snake_case` and `camelCase` field names in JSON input. Use whichever you find more readable. Both `--msg-template` and `invoke` output use the proto field names (snake_case).

## Summary

- **Protocol Buffers** is a schema-first serialization format that produces compact binary encodings with built-in type safety and compatibility guarantees.
- **Proto3** is the current syntax version. A `.proto` file defines messages with typed, numbered fields.
- **Field numbers**, not field names, identify fields in the binary encoding. This is the foundation of protobuf's compact format and compatibility model.
- **GrpCurl.Net** bridges the gap between protobuf's binary format and human-readable JSON, letting you explore and call gRPC services from the command line without code generation.

## What's Next

In the next chapter, [Your First gRPC Call](02-getting-started.md), you will start the TestServer, explore its services with `grpcn list` and `grpcn describe`, and make your first RPC calls.
