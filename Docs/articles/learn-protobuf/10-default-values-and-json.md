# Default Values, Field Presence, and JSON Mapping

Protobuf's serialization rules are precise and sometimes surprising. Understanding how default values work, when fields are present or absent, and how protobuf maps to JSON is essential for building correct APIs and debugging unexpected behavior. This chapter covers the details that separate a casual protobuf user from someone who truly understands the format.

## Proto3 Default Values

Every proto3 field has a **default value** that is used when the field is not explicitly set. These defaults are baked into the language and cannot be customised:

| Type | Default Value |
|------|--------------|
| `int32`, `int64`, `uint32`, `uint64`, `sint32`, `sint64` | `0` |
| `fixed32`, `fixed64`, `sfixed32`, `sfixed64` | `0` |
| `float`, `double` | `0.0` |
| `bool` | `false` |
| `string` | `""` (empty string) |
| `bytes` | `b""` (empty bytes) |
| `enum` | First defined value (must be `0`) |
| `message` (nested) | `null` (not present) |

These are not arbitrary choices -- they are the "zero values" for each type, following the same philosophy as Go's zero values or C#'s `default(T)`.

## Field Presence: The Fundamental Trade-Off

Proto3 made a deliberate design decision that is both its greatest strength and its most common source of confusion: **scalar fields have no concept of null**.

### What This Means in Practice

Consider this message:

```protobuf
message UserProfile {
  string name = 1;
  int32 age = 2;
  bool active = 3;
}
```

If a client sends a `UserProfile` with `age` set to `0`, the binary encoding is **identical** to a `UserProfile` where `age` was never set at all. Both produce zero bytes for the `age` field. The recipient cannot distinguish between:

- "The user explicitly said their age is 0"
- "The user did not provide an age"

This is a fundamental property of proto3 scalar fields. It exists because it makes serialization simpler and messages smaller -- default-valued fields take zero bytes on the wire.

### Three Strategies for Nullable Scalars

When you need to distinguish "not set" from "set to the default value," proto3 offers three approaches:

**1. Wrapper types** (covered in [Chapter 8](08-well-known-types.md)):

```protobuf
import "google/protobuf/wrappers.proto";

message UserProfile {
  google.protobuf.Int32Value age = 2;  // null means "not provided"
}
```

**2. The `optional` keyword** (reintroduced in proto3 since protobuf v3.15):

```protobuf
message UserProfile {
  optional int32 age = 2;  // has explicit presence tracking
}
```

**3. A separate boolean flag**:

```protobuf
message UserProfile {
  int32 age = 2;
  bool has_age = 5;
}
```

Wrapper types and `optional` are the recommended approaches. The boolean flag pattern is a legacy workaround.

## Observing Default Values with `--emit-defaults`

By default, GrpCurl.Net (and protobuf JSON serialization in general) **omits fields that have their default values**. This keeps output concise but can make it hard to see the full structure of a message.

### Without `--emit-defaults` (Sparse Output)

```bash
grpcurl.net invoke --plaintext -d '{}' localhost:9090 testing.TestService/EmptyCall
```

Example Output:

```json
{}
```

The `Empty` message has no fields, so the output is trivially empty. Let us try a more interesting example:

```bash
grpcurl.net invoke --plaintext \
  -d '{"responseSize": 0}' \
  localhost:9090 testing.TestService/UnaryCall
```

Example Output:

```json
{}
```

Even though we explicitly sent `"responseSize": 0`, the response contains nothing visible -- all fields in `SimpleResponse` have their default values (empty payload, empty username, empty oauth_scope), so they are all omitted.

### With `--emit-defaults` (Complete Output)

Adding `--emit-defaults` forces GrpCurl.Net to include every field, regardless of its value:

```bash
grpcurl.net invoke --plaintext --emit-defaults \
  -d '{"responseSize": 0}' \
  localhost:9090 testing.TestService/UnaryCall
```

Example Output:

```json
{
  "payload": null,
  "username": "",
  "oauth_scope": ""
}
```

Now you can see the complete shape of the `SimpleResponse` message, including all the fields that were previously hidden. Note that `--emit-defaults` shows all fields, but null message fields appear as `null` rather than as expanded objects with default sub-field values. The field names use snake_case proto names (e.g., `oauth_scope`). This is invaluable for:

- **Learning**: Seeing the full message structure without consulting the `.proto` file
- **Debugging**: Confirming that a field is truly empty vs. missing from the schema
- **Documentation**: Generating complete response examples

### Comparing the Two Modes

Sparse -- only non-default fields shown:

```bash
grpcurl.net invoke --plaintext \
  -d '{"payload": {"body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

```json
{
  "payload": {
    "body": "SGVsbG8="
  }
}
```

Complete -- every field shown:

```bash
grpcurl.net invoke --plaintext --emit-defaults \
  -d '{"payload": {"body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

```json
{
  "payload": {
    "body": "SGVsbG8="
  },
  "username": "",
  "oauth_scope": ""
}
```

The sparse version shows only the payload we sent. The complete version additionally shows `username` and `oauth_scope` with their default empty string values. Note that the payload's `type` field (which defaults to `COMPRESSABLE`) is not shown even with `--emit-defaults` -- default-valued fields within nested messages may still be omitted depending on the implementation.

## JSON Field Naming

Protobuf and JSON use different naming conventions, and understanding the mapping between them is critical for constructing correct requests.

### The Rule

- **Proto files** use `snake_case`: `response_size`, `fill_username`, `oauth_scope`
- **Canonical JSON** uses `camelCase`: `responseSize`, `fillUsername`, `oauthScope`

The protobuf specification defines an automatic conversion from snake_case to camelCase using a deterministic algorithm:

1. Remove each underscore
2. Capitalise the letter following each removed underscore

Examples:

| Proto Field Name | camelCase JSON Name |
|-----------------|----------------|
| `response_size` | `responseSize` |
| `fill_username` | `fillUsername` |
| `oauth_scope` | `oauthScope` |
| `aggregated_payload_size` | `aggregatedPayloadSize` |
| `interval_us` | `intervalUs` |

### GrpCurl.Net Accepts Both

When constructing requests, GrpCurl.Net accepts both the camelCase JSON name and the original snake_case proto name:

```bash
# Both of these are equivalent:
grpcurl.net invoke --plaintext -d '{"responseSize": 10}' \
  localhost:9090 testing.TestService/UnaryCall

grpcurl.net invoke --plaintext -d '{"response_size": 10}' \
  localhost:9090 testing.TestService/UnaryCall
```

GrpCurl.Net uses **snake_case** (proto field names) consistently in its own output -- both in `--msg-template` templates and in `invoke` responses. For example, both will show `oauth_scope` rather than `oauthScope`. Both forms are accepted as input when constructing requests.

## Special Float Values

JSON has no way to represent the IEEE 754 special float values (NaN, positive infinity, negative infinity) as numbers. Protobuf's JSON mapping handles this by encoding them as **strings**:

| Float Value | JSON Representation |
|-------------|-------------------|
| Not a Number | `"NaN"` |
| Positive infinity | `"Infinity"` |
| Negative infinity | `"-Infinity"` |

For example, a message with a `double` field set to NaN would serialise as:

```json
{
  "double_val": "NaN"
}
```

This is a deliberate departure from standard JSON (which does not have these values) to preserve the full range of IEEE 754 floating-point numbers.

## 64-Bit Integer Representation

JavaScript (and by extension, JSON as commonly consumed) cannot precisely represent integers larger than 2^53. Since protobuf's `int64`, `uint64`, `sint64`, `fixed64`, and `sfixed64` types can hold values up to 2^64, the JSON mapping represents them as **quoted strings** to preserve precision:

| Proto Type | JSON Representation | Example |
|-----------|-------------------|---------|
| `int32` | Number | `42` |
| `int64` | Quoted string | `"9223372036854775807"` |
| `uint32` | Number | `42` |
| `uint64` | Quoted string | `"18446744073709551615"` |

When *sending* values to GrpCurl.Net, you can use either the quoted string or a bare number (if it fits within safe integer range):

```json
{
  "int64_val": "9223372036854775807",
  "int64_val": 100
}
```

Both are valid, but the string form is recommended for values that exceed 2^53.

## Unknown Fields with `--allow-unknown-fields`

By default, protobuf JSON parsing is strict: if your JSON contains a field name that does not exist in the message definition, the parser rejects it with an error. This catches typos and ensures your data matches the schema.

However, there are situations where you want to be lenient -- for example, when a client has been updated to send new fields that the server does not yet know about. The `--allow-unknown-fields` flag tells GrpCurl.Net to silently ignore unrecognised fields:

```bash
# This would normally fail because "nonExistentField" is not in SimpleRequest:
grpcurl.net invoke --plaintext --allow-unknown-fields \
  -d '{"responseSize": 10, "nonExistentField": "ignored"}' \
  localhost:9090 testing.TestService/UnaryCall
```

With `--allow-unknown-fields`, the `nonExistentField` is silently discarded and the call proceeds with just `responseSize`.

### When to Use It

- **Forward compatibility testing**: Simulating a newer client talking to an older server
- **Migration**: Gradually introducing new fields while some consumers have not updated
- **Copy-paste convenience**: Reusing JSON payloads from a related but slightly different service

> **Caution:** Do not use `--allow-unknown-fields` as a default. Strict parsing catches mistakes. Use it only when you have a specific reason to be lenient.

## Structured Errors with `--output json`

When a gRPC call fails, the server returns a status code and an error message. By default GrpCurl.Net prints the error in a human-readable form on stderr. Passing `--output json` switches both successful responses and errors to a stable, machine-readable envelope shape — useful for scripts, CI pipelines, and AI agents.

### Triggering an Error

Call a method on a service the server does not implement. The `UnimplementedService` exists in the schema but intentionally has no implementation:

```bash
grpcurl.net invoke --plaintext --output json \
  -d '{}' \
  localhost:9090 testing.UnimplementedService/UnimplementedCall 2>error.json ; echo $?
```

The error envelope is written to **stderr** as a single JSON line. **stdout** stays empty so a pipeline like `... | jq` is unaffected. Process exit code is `64 + gRPC status code` (here `64 + 12 = 76`).

`error.json`:
```json
{"kind":"error","category":"rpc","exitCode":76,"message":"Service is unimplemented.","address":"localhost:9090","method":"testing.UnimplementedService/UnimplementedCall","grpc":{"code":12,"status":"Unimplemented","detail":"Service is unimplemented."}}
```

The `grpc.code` field corresponds to [gRPC status codes](https://grpc.io/docs/guides/status-codes/):

| Code | Name | Meaning |
|------|------|---------|
| 0 | OK | Success |
| 1 | CANCELLED | Operation was cancelled |
| 2 | UNKNOWN | Unknown error |
| 3 | INVALID_ARGUMENT | Client specified an invalid argument |
| 4 | DEADLINE_EXCEEDED | Deadline expired before operation completed |
| 5 | NOT_FOUND | Requested entity was not found |
| 7 | PERMISSION_DENIED | Caller does not have permission |
| 12 | UNIMPLEMENTED | Method not implemented by the server |
| 13 | INTERNAL | Internal server error |
| 14 | UNAVAILABLE | Service is currently unavailable |

The envelope's top-level `category` field discriminates the error class: `rpc`, `network`, `timeout`, `usage`, `schema`, `cancelled`, `internal`. Use this to drive automated handling without parsing free-text error messages.

## Combining Flags

These flags can be combined for different use cases:

```bash
# Full output with all defaults, lenient parsing, and structured envelopes
grpcurl.net invoke --plaintext --emit-defaults --allow-unknown-fields --output json \
  -d '{"responseSize": 10, "fillUsername": true, "extraField": "ignored"}' \
  localhost:9090 testing.TestService/UnaryCall
```

### Flag Summary

| Flag | Purpose | Default Behavior |
|------|---------|-----------------|
| `--emit-defaults` | Include all fields in output, even default-valued ones | Omit default-valued fields |
| `--allow-unknown-fields` | Ignore unrecognised JSON fields | Reject unrecognised fields with an error |
| `--output json` | Emit responses and errors as line-based JSON envelopes (NDJSON for streaming) | Human-readable text on stdout, error markup on stderr |

## The Proto3 JSON Specification

Everything described in this chapter follows the [Proto3 JSON Mapping specification](https://protobuf.dev/programming-guides/proto3/#json). This specification is not just a convention -- it is a formal standard that all protobuf implementations must follow. Key rules:

1. **Field names** are converted from snake_case to camelCase
2. **Default values** are omitted by default but can be included optionally
3. **Enums** are represented as their string names, not integer values
4. **64-bit integers** are quoted as strings for JavaScript compatibility
5. **Bytes** are represented as base64-encoded strings
6. **Special floats** (NaN, Infinity) are represented as strings
7. **Well-known types** have their own canonical JSON forms (covered in [Chapter 8](08-well-known-types.md))

Understanding this specification ensures that your protobuf APIs produce predictable, interoperable JSON across every language and tool in the ecosystem.

## Recap

In this chapter you learned:

- **Default values** in proto3 are the zero values for each type, and they cannot be customised
- **Field presence** for scalar fields means you cannot distinguish "not set" from "set to the default" -- use wrapper types or `optional` when you need that distinction
- **`--emit-defaults`** forces GrpCurl.Net to include all fields in output, which is invaluable for learning, debugging, and documentation
- **JSON field naming**: GrpCurl.Net uses snake_case (proto field names) in its output, and accepts both snake_case and camelCase in input
- **Special float values** (NaN, Infinity) and **64-bit integers** have string representations in JSON to handle language limitations
- **`--allow-unknown-fields`** provides forward compatibility by ignoring unrecognised fields
- **`--output json`** emits both successful responses and errors as one-line JSON envelopes — responses on stdout, errors on stderr
- The **Proto3 JSON Mapping specification** formally defines all these rules

## What's Next

You now have a deep understanding of protobuf types, services, and serialization. The final chapter brings it all together with a practical topic that matters for real-world deployments. In [Chapter 11: Schema Management with Protoset Files](11-schema-management.md), you will learn how to capture, version, and reuse protobuf schemas using protoset files -- enabling offline operations, CI/CD pipelines, and robust schema management.
