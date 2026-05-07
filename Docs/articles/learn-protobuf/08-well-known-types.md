# Well-Known Types

Protobuf provides a set of **well-known types** -- standard message definitions that ship with every protobuf installation. These types solve common problems that nearly every application encounters: representing timestamps, durations, nullable values, and dynamic data structures. Rather than reinventing the wheel in each project, you import these types from the `google/protobuf/` package and get consistent, interoperable behavior across languages and tools.

In this chapter, you will explore every major well-known type, understand how each one maps to JSON, and use GrpCurl.Net to examine them.

## Why Well-Known Types Exist

Proto3 scalar fields (integers, strings, booleans) cover most use cases, but they fall short in several situations:

- **Timestamps and durations** have no native scalar type in protobuf. You could use an `int64` for Unix seconds, but then every team would represent time differently.
- **Nullable scalars** are not possible in proto3. An `int32` field that is not set is indistinguishable from one set to `0`. Wrapper types solve this.
- **Dynamic or schema-less data** cannot be represented by fixed message definitions. Sometimes you genuinely need to pass arbitrary JSON.
- **"No data" returns** need a standard empty message rather than each service defining its own.

Well-known types provide Google's official, battle-tested solutions to all of these problems.

## The Import System

To use a well-known type, you import its `.proto` file from the `google/protobuf/` directory:

```protobuf
syntax = "proto3";

import "google/protobuf/timestamp.proto";
import "google/protobuf/duration.proto";
import "google/protobuf/wrappers.proto";
import "google/protobuf/any.proto";
import "google/protobuf/struct.proto";
import "google/protobuf/empty.proto";
import "google/protobuf/field_mask.proto";
```

These files are part of every protobuf SDK. You do not need to download them separately. The import path `google/protobuf/` is resolved automatically by `protoc` and by gRPC server reflection.

## Exploring Well-Known Types with GrpCurl.Net

The project includes a `WellKnownTypesMessage` defined in a proto file that uses every major well-known type. Let us start by examining the message structure using a protoset file:

```bash
grpcurl.net describe --protoset Tests/TestProtosets/well-known-types.protoset --msg-template wkttesting.WellKnownTypesMessage
```

> **Note:** The `WellKnownTypesService` is defined in the project's proto files but is not running on the TestServer. We use a **protoset file** (a binary schema snapshot -- covered in detail in [Chapter 11](11-schema-management.md)) to explore its message types offline. The `--protoset` flag tells GrpCurl.Net to read the schema from a file instead of connecting to a server.

This command shows the proto definition followed by a JSON template for the `WellKnownTypesMessage`, revealing how GrpCurl.Net represents each well-known type in its canonical JSON form. For example, `Timestamp` appears as `"1970-01-01T00:00:00Z"`, `Duration` as `"0s"`, `Struct` as `{"google.protobuf.Struct": "supports arbitrary JSON objects"}`, and `FieldMask` as `{"paths": [""]}`. Let us walk through each type individually.

## Timestamp

`google.protobuf.Timestamp` represents a specific point in time, independent of any time zone or calendar. Internally, it consists of two fields:

- `seconds` (int64): Seconds since the Unix epoch (1970-01-01T00:00:00Z)
- `nanos` (int32): Non-negative fractions of a second at nanosecond resolution

However, you almost never work with these raw fields directly. The canonical JSON representation is an **RFC 3339 string**:

```json
{
  "timestamp_field": "2024-01-15T10:30:00Z"
}
```

### JSON Format Rules

- Always uses UTC (the `Z` suffix), unless an explicit offset is provided
- Fractional seconds are optional: `"2024-01-15T10:30:00.123456789Z"`
- Trailing zeros in fractional seconds may be omitted: `"2024-01-15T10:30:00.100Z"` is equivalent to `"2024-01-15T10:30:00.1Z"`

### When to Use Timestamp

Use `Timestamp` whenever you need to record *when* something happened: creation dates, modification times, event timestamps, expiration times. It is far superior to using a raw `int64` of Unix seconds because the JSON representation is human-readable and unambiguous.

## Duration

`google.protobuf.Duration` represents a signed, fixed-length span of time. Like `Timestamp`, it has `seconds` (int64) and `nanos` (int32) fields internally, but its JSON representation is a **string ending in `s`**:

```json
{
  "duration_field": "30.5s"
}
```

### JSON Format Examples

| Duration | JSON Representation |
|----------|-------------------|
| 30 seconds | `"30s"` |
| 1.5 seconds | `"1.5s"` |
| 1 millisecond | `"0.001s"` |
| 1 microsecond | `"0.000001s"` |
| 1 nanosecond | `"0.000000001s"` |
| Negative 5 seconds | `"-5s"` |
| Zero | `"0s"` |

### When to Use Duration

Use `Duration` for time spans: timeouts, retry intervals, processing times, age calculations. The string format with the `s` suffix makes it immediately clear that the value represents seconds, unlike a bare number that could be seconds, milliseconds, or anything else.

## Wrapper Types

The wrapper types are one of the most practically important well-known types. They solve a fundamental limitation of proto3: **scalar fields cannot be null**.

In proto3, if you have a field `int32 age = 1;` and it is not set, it defaults to `0`. There is no way to distinguish "the age is zero" from "the age was not provided." Wrapper types fix this by wrapping each scalar in a message:

| Proto Type | Wrapper Type | JSON When Set | JSON When Null |
|------------|-------------|---------------|----------------|
| `string` | `google.protobuf.StringValue` | `"hello"` | `null` |
| `int32` | `google.protobuf.Int32Value` | `42` | `null` |
| `int64` | `google.protobuf.Int64Value` | `"100"` | `null` |
| `uint32` | `google.protobuf.UInt32Value` | `42` | `null` |
| `uint64` | `google.protobuf.UInt64Value` | `"100"` | `null` |
| `float` | `google.protobuf.FloatValue` | `3.14` | `null` |
| `double` | `google.protobuf.DoubleValue` | `2.718` | `null` |
| `bool` | `google.protobuf.BoolValue` | `true` | `null` |
| `bytes` | `google.protobuf.BytesValue` | `"base64data"` | `null` |

### Key Insight: JSON Is Just the Raw Value

A common mistake is to assume that a `StringValue` serialises as `{"value": "hello"}`. It does not. The canonical JSON representation of a wrapper type is **just the raw value itself**:

```json
{
  "string_value": "hello",
  "int32_value": 42,
  "bool_value": true,
  "float_value": 3.14
}
```

This is a special JSON mapping rule that applies only to wrapper types. It makes the JSON clean and natural -- consumers do not need to know they are dealing with wrapper messages.

### When an Unset Wrapper Appears as Null

If a wrapper field is not set on the message, it appears as `null` in JSON (or is omitted entirely if defaults are not emitted):

```json
{
  "string_value": null,
  "int32_value": null
}
```

This three-state capability (absent, null, or present with a value) is exactly what makes wrapper types invaluable for APIs that need to distinguish "not provided" from "explicitly set to the default."

## Empty

`google.protobuf.Empty` is the simplest well-known type. It represents "no data" -- the protobuf equivalent of `void` in C# or Java, or `()` (unit) in functional languages.

```json
{}
```

Use `Empty` when an RPC method does not need a meaningful request or response. For example, the TestServer's `EmptyCall` uses `Empty` for both its request and response:

```bash
grpcurl.net invoke --plaintext -d '{}' localhost:9090 testing.TestService/EmptyCall
```

> **Why not just define your own empty message?** You could, but using `google.protobuf.Empty` signals intent clearly and avoids every team creating slightly different "nothing" messages. It is a convention that the entire ecosystem understands.

## FieldMask

`google.protobuf.FieldMask` specifies a subset of fields in a message. It is commonly used in update operations to indicate which fields the client wants to modify, or in read operations to request only certain fields (partial responses).

### JSON Format

A `FieldMask` serialises as a **comma-separated string of field paths** using camelCase naming:

```json
{
  "field_mask": "name,email,address.city"
}
```

### Nested Paths

Paths can reference nested fields using dot notation. Given a message like:

```protobuf
message User {
  string name = 1;
  string email = 2;
  Address address = 3;
}

message Address {
  string street = 1;
  string city = 2;
}
```

The field mask `"name,address.city"` means "only the `name` field and the `city` field within `address`."

### Common Use Cases

- **Partial updates**: "Update only the email field, leave everything else unchanged"
- **Partial reads**: "I only need the name and email, skip the large profile photo"
- **Permissions**: "This user is allowed to see these fields but not those"

## Any

`google.protobuf.Any` enables **dynamic typing** in protobuf. It can hold any protobuf message, even one the recipient has never seen before. This is the escape hatch for when you need maximum flexibility.

### JSON Format

An `Any` value serialises as a JSON object with a special `@type` field that identifies the contained message type, plus all the fields of that message:

```json
{
  "any_field": {
    "@type": "type.googleapis.com/wkttesting.WellKnownTypesMessage",
    "timestamp_field": "2024-01-15T10:30:00Z",
    "string_value": "hello"
  }
}
```

### How It Works

1. The `@type` URL tells the deserialiser which message type is packed inside
2. The remaining fields are the serialised content of that message
3. The type URL format is `type.googleapis.com/<fully.qualified.MessageName>`

### When to Use Any

- **Error details**: gRPC error responses often carry structured error information in `Any` fields
- **Plugin systems**: When the set of possible message types is open-ended
- **Event buses**: Heterogeneous event streams where different events have different schemas

> **Caution:** Overusing `Any` defeats the purpose of protobuf's strong typing. Prefer concrete message types when the set of possibilities is known at design time.

## Struct, Value, and ListValue

These three types work together to represent **schema-less, JSON-like data** within protobuf. They are the protobuf equivalent of `JObject`/`JsonElement` in .NET or `dict`/`list` in Python.

### Struct

`google.protobuf.Struct` maps directly to a JSON object -- an unordered collection of key-value pairs where keys are strings:

```json
{
  "struct_field": {
    "name": "Alice",
    "age": 30,
    "active": true,
    "tags": ["admin", "user"],
    "address": {
      "city": "London"
    }
  }
}
```

### Value

`google.protobuf.Value` can hold any single JSON value: a number, string, boolean, null, object (Struct), or array (ListValue):

```json
{
  "value_field": "just a string"
}
```

Or:

```json
{
  "value_field": 42
}
```

Or:

```json
{
  "value_field": {
    "nested": "object"
  }
}
```

### ListValue

`google.protobuf.ListValue` maps to a JSON array:

```json
{
  "list_value_field": [1, "two", true, null, {"key": "value"}]
}
```

### When to Use Struct/Value/ListValue

- **Configuration blobs**: When schema varies per deployment or customer
- **Metadata**: Free-form key-value metadata alongside strongly-typed data
- **API gateways**: Proxying arbitrary JSON payloads through a protobuf pipeline
- **Logging**: Capturing arbitrary structured context in log entries

> **Trade-off:** These types sacrifice compile-time type safety for runtime flexibility. Use them only when the data truly has no fixed schema.

## Complete JSON Example

Here is a complete `WellKnownTypesMessage` with every field populated:

```json
{
  "timestamp_field": "2024-01-15T10:30:00Z",
  "duration_field": "30.5s",
  "string_value": "hello world",
  "int32_value": 42,
  "int64_value": "9223372036854775807",
  "uint32_value": 100,
  "uint64_value": "18446744073709551615",
  "float_value": 3.14,
  "double_value": 2.718281828,
  "bool_value": true,
  "bytes_value": "SGVsbG8gV29ybGQ=",
  "any_field": {
    "@type": "type.googleapis.com/google.protobuf.StringValue",
    "value": "packed string"
  },
  "struct_field": {
    "name": "Alice",
    "scores": [95, 87, 92],
    "active": true
  },
  "value_field": "any JSON value here",
  "list_value_field": [1, "two", true, null],
  "field_mask": "timestamp_field,string_value,struct_field.name"
}
```

### Hands-On: Exploring the Full Message

Since the `WellKnownTypesService` is defined in a protoset file but not running on the TestServer, you can explore its complete structure offline:

```bash
grpcurl.net describe --protoset Tests/TestProtosets/well-known-types.protoset --msg-template wkttesting.WellKnownTypesMessage
```

This outputs a JSON template showing every well-known type field with its canonical default representation, letting you study the canonical JSON forms without a running server.

## Summary of JSON Mappings

| Well-Known Type | JSON Representation | Example |
|----------------|--------------------|---------|
| `Timestamp` | RFC 3339 string | `"2024-01-15T10:30:00Z"` |
| `Duration` | Seconds string with `s` suffix | `"30.5s"` |
| `StringValue` | Raw string | `"hello"` |
| `Int32Value` | Raw number | `42` |
| `Int64Value` | Quoted string (precision) | `"9223372036854775807"` |
| `UInt32Value` | Raw number | `100` |
| `UInt64Value` | Quoted string (precision) | `"18446744073709551615"` |
| `FloatValue` | Raw number | `3.14` |
| `DoubleValue` | Raw number | `2.718` |
| `BoolValue` | Raw boolean | `true` |
| `BytesValue` | Base64 string | `"SGVsbG8="` |
| `Empty` | Empty object | `{}` |
| `FieldMask` | Comma-separated paths | `"name,email,address.city"` |
| `Any` | Object with `@type` field | `{"@type": "...", ...}` |
| `Struct` | JSON object | `{"key": "value"}` |
| `Value` | Any JSON value | `42`, `"text"`, `true`, `null` |
| `ListValue` | JSON array | `[1, "two", true]` |

## Recap

In this chapter you learned:

- **Well-known types** are standard message definitions that ship with every protobuf installation, imported from `google/protobuf/`
- **Timestamp** and **Duration** provide standardised time representations with human-readable JSON forms
- **Wrapper types** enable nullable scalars in proto3, serializing as raw JSON values rather than `{"value": ...}` objects
- **Empty** is the standard "no data" message for void-like RPCs
- **FieldMask** specifies subsets of fields for partial reads and updates
- **Any** provides dynamic typing by packing arbitrary messages with a type URL
- **Struct**, **Value**, and **ListValue** represent schema-less JSON-like data within protobuf

## What's Next

Now that you understand all of protobuf's type system -- from basic scalars through well-known types -- it is time to see how these types are used in practice. In [Chapter 9: Services and RPC Patterns](09-services-and-streaming.md), you will learn about gRPC service definitions and the four RPC streaming patterns: unary, server streaming, client streaming, and bidirectional.
