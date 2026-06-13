# Scalar Types: The Building Blocks

Every protobuf message, no matter how complex, is ultimately built from **scalar types** -- the atomic data types that hold individual values like numbers, strings, and booleans. Understanding these types is essential for reading `.proto` files and constructing correct gRPC requests.

In this chapter, you will learn all 15 proto3 scalar types, how they map to JSON, and what to watch out for when using them with GrpCurl.Net.

## Prerequisites

Make sure the TestServer is running:

```bash
dotnet run --project Tests/GrpCurl.Net.TestServer
```

## The Complete Scalar Type Reference

Proto3 defines 15 scalar types. Here they are, grouped by category, along with their JSON representations and default values:

| Proto Type | JSON Type | Default Value | Notes |
|------------|-----------|---------------|-------|
| `double` | number | `0.0` | 64-bit IEEE 754 floating point |
| `float` | number | `0.0` | 32-bit IEEE 754 floating point |
| `int32` | number | `0` | Variable-length encoding; inefficient for negative numbers |
| `int64` | string | `"0"` | Quoted in JSON to preserve precision |
| `uint32` | number | `0` | Unsigned, variable-length encoding |
| `uint64` | string | `"0"` | Quoted in JSON to preserve precision |
| `sint32` | number | `0` | ZigZag encoding; efficient for negative numbers |
| `sint64` | string | `"0"` | ZigZag encoding; quoted in JSON |
| `fixed32` | number | `0` | Always 4 bytes; efficient for values > 2^28 |
| `fixed64` | string | `"0"` | Always 8 bytes; quoted in JSON |
| `sfixed32` | number | `0` | Signed, always 4 bytes |
| `sfixed64` | string | `"0"` | Signed, always 8 bytes; quoted in JSON |
| `bool` | boolean | `false` | True or false |
| `string` | string | `""` | Must be valid UTF-8 |
| `bytes` | string (base64) | `""` | Base64-encoded in JSON |

> **Key rule:** Any 64-bit numeric type (`int64`, `uint64`, `sint64`, `fixed64`, `sfixed64`) is represented as a **quoted string** in JSON. This is because JavaScript's `Number` type cannot safely represent all 64-bit integers, and JSON was originally designed for JavaScript.

## Deep Dive: Integer Types

### int32 and int64

The most commonly used integer types. They use **variable-length encoding** (varint), meaning small values take fewer bytes on the wire.

```protobuf
message Example {
  int32 count = 1;    // -2,147,483,648 to 2,147,483,647
  int64 big_count = 2; // -9,223,372,036,854,775,808 to 9,223,372,036,854,775,807
}
```

**When to use:** Choose `int32` for values that fit in 32 bits (most counters, IDs, sizes). Choose `int64` when you need the full 64-bit range (timestamps as nanoseconds, database row IDs, financial amounts in cents).

**Wire encoding caveat:** `int32` and `int64` use **two's complement** for negative numbers, which means negative values always take the maximum number of bytes (5 bytes for `int32`, 10 bytes for `int64`). If your field frequently holds negative values, use `sint32` or `sint64` instead.

### uint32 and uint64

Unsigned variants that can only hold non-negative values.

```protobuf
message Example {
  uint32 age = 1;        // 0 to 4,294,967,295
  uint64 file_size = 2;  // 0 to 18,446,744,073,709,551,615
}
```

**When to use:** When the value is inherently non-negative (ages, sizes, counts) and you want to use the full positive range. Be aware that negative values cannot be stored -- the serialiser will reject them.

### Integer Ranges at a Glance

| Type | Minimum | Maximum |
|------|---------|---------|
| `int32` | -2,147,483,648 | 2,147,483,647 |
| `int64` | -9.2 x 10^18 | 9.2 x 10^18 |
| `uint32` | 0 | 4,294,967,295 |
| `uint64` | 0 | 1.8 x 10^19 |

## Deep Dive: Signed Variants (sint32, sint64)

These types use **ZigZag encoding**, which maps signed integers to unsigned integers so that values with small absolute values (including negative ones) take fewer bytes.

```protobuf
message TemperatureReading {
  sint32 celsius = 1;  // Efficient for values like -20, 5, -3, 15
}
```

Here is how ZigZag encoding works:

| Signed Value | Encoded As |
|-------------|------------|
| 0 | 0 |
| -1 | 1 |
| 1 | 2 |
| -2 | 3 |
| 2 | 4 |

Small absolute values, whether positive or negative, get small encoded values and therefore take fewer bytes.

**When to use `sint32`/`sint64` over `int32`/`int64`:** When you expect negative values to be common. For example:

- Temperature changes (could be negative or positive)
- Deltas or offsets (price change: -5, +3, -1)
- Coordinates relative to an origin

If your values are almost always positive, plain `int32`/`int64` is fine. If negatives are common, `sint32`/`sint64` saves significant wire space.

## Deep Dive: Fixed-Width Types

Fixed-width types always use exactly 4 or 8 bytes on the wire, regardless of the value.

| Type | Size | Signed | Range |
|------|------|--------|-------|
| `fixed32` | 4 bytes | No | 0 to 4,294,967,295 |
| `fixed64` | 8 bytes | No | 0 to 1.8 x 10^19 |
| `sfixed32` | 4 bytes | Yes | -2,147,483,648 to 2,147,483,647 |
| `sfixed64` | 8 bytes | Yes | -9.2 x 10^18 to 9.2 x 10^18 |

```protobuf
message HashEntry {
  fixed64 hash = 1;     // Hashes are typically large, random values
  sfixed32 offset = 2;  // File offsets can be negative (relative)
}
```

**When to use:** Fixed-width types are more efficient than varint types when values are consistently large. The crossover point is:

- `fixed32` is cheaper than `uint32` when values are typically **greater than 2^28** (~268 million)
- `fixed64` is cheaper than `uint64` when values are typically **greater than 2^56**

Common use cases: hash values, UUIDs stored as numbers, bitmasks, and memory addresses.

## Deep Dive: Floating Point (float, double)

Protobuf provides two IEEE 754 floating-point types:

| Type | Size | Significant Digits | Range |
|------|------|--------------------|-------|
| `float` | 4 bytes | ~7 decimal digits | +/- 3.4 x 10^38 |
| `double` | 8 bytes | ~15 decimal digits | +/- 1.8 x 10^308 |

```protobuf
message GeoLocation {
  double latitude = 1;   // Need precision: 40.7128° N
  double longitude = 2;  // Need precision: -74.0060° W
  float altitude = 3;    // Less precision needed: 10.5 meters
}
```

**When to use `double`:** When precision matters -- geographic coordinates, scientific measurements, financial calculations (though for money, consider using integer cents instead).

**When to use `float`:** When you need to save space and can tolerate reduced precision -- sensor readings, graphics data, approximate values.

> **Warning:** As with all floating-point arithmetic, be aware of precision limitations. The value `0.1` cannot be exactly represented in binary floating point. For exact decimal values (like money), consider storing amounts as integer cents or using a string representation.

## Deep Dive: Boolean, String, and Bytes

### bool

A simple true/false value. Takes 1 byte on the wire when `true`, and 0 bytes when `false` (because `false` is the default and defaults are not serialised).

```protobuf
message UserPreferences {
  bool email_notifications = 1;
  bool dark_mode = 2;
}
```

In JSON: `true` or `false` (not quoted).

### string

A sequence of UTF-8 encoded characters. Protobuf enforces that strings are valid UTF-8.

```protobuf
message User {
  string name = 1;
  string email = 2;
}
```

In JSON: a regular quoted string -- `"hello world"`.

> **Important:** Protobuf `string` fields must contain valid UTF-8 text. If you need to store arbitrary binary data, use `bytes` instead.

### bytes

A sequence of arbitrary bytes. In JSON, `bytes` fields are represented as **Base64-encoded strings**.

```protobuf
message FileChunk {
  bytes data = 1;
  string filename = 2;
}
```

In JSON, the `data` field would look like:

```json
{
  "data": "SGVsbG8gV29ybGQ=",
  "filename": "greeting.txt"
}
```

The value `"SGVsbG8gV29ybGQ="` is the Base64 encoding of the raw bytes for `"Hello World"`.

## JSON Representation Gotchas

When working with GrpCurl.Net (which uses JSON for input and output), there are a few important things to remember:

### 1. 64-bit integers are quoted strings

This is the most common source of confusion. In JSON, `int64`, `uint64`, `sint64`, `fixed64`, and `sfixed64` values are represented as **strings**, not numbers:

```json
{
  "int32_val": 42,
  "int64_val": "9223372036854775807",
  "uint64_val": "18446744073709551615"
}
```

Why? JSON numbers are IEEE 754 doubles, which can only exactly represent integers up to 2^53. A 64-bit integer can go up to 2^63 (signed) or 2^64 (unsigned), so quoting prevents precision loss.

GrpCurl.Net handles this automatically. When you provide a quoted string for a 64-bit field, it parses it correctly. You can also provide an unquoted number if the value fits safely in a JSON number.

### 2. Bytes are Base64-encoded

The `bytes` type is always Base64-encoded in JSON:

```json
{
  "body": "AQIDBA=="
}
```

This represents the four bytes `0x01, 0x02, 0x03, 0x04`. GrpCurl.Net will Base64-decode the string before sending it as binary protobuf data.

### 3. Default values are omitted

Proto3 does not serialise fields that hold their default value. This means:

- A field set to `0`, `""`, `false`, or empty bytes will **not appear** in the JSON output
- You cannot distinguish between "field was explicitly set to 0" and "field was not set"

This is by design in proto3 and is important to remember when interpreting responses.

## Hands-On: Exploring AllScalarsMessage

The TestServer includes an `AllScalarsMessage` type that contains one field for every scalar type. Let us examine it:

```bash
grpcn describe --plaintext --msg-template localhost:9090 testing.AllScalarsMessage
```

Expected output:

```
testing.AllScalarsMessage is a message:
message AllScalarsMessage {
  double double_val = 1;
  float float_val = 2;
  int32 int32_val = 3;
  int64 int64_val = 4;
  uint32 uint32_val = 5;
  uint64 uint64_val = 6;
  sint32 sint32_val = 7;
  sint64 sint64_val = 8;
  fixed32 fixed32_val = 9;
  fixed64 fixed64_val = 10;
  sfixed32 sfixed32_val = 11;
  sfixed64 sfixed64_val = 12;
  bool bool_val = 13;
  string string_val = 14;
  bytes bytes_val = 15;
}

Message template:
{
  "double_val": 0,
  "float_val": 0,
  "int32_val": 0,
  "int64_val": "0",
  "uint32_val": 0,
  "uint64_val": "0",
  "sint32_val": 0,
  "sint64_val": "0",
  "fixed32_val": 0,
  "fixed64_val": "0",
  "sfixed32_val": 0,
  "sfixed64_val": "0",
  "bool_val": false,
  "string_val": "",
  "bytes_val": ""
}
```

In the template, 64-bit integer types (`int64_val`, `uint64_val`, `sint64_val`, `fixed64_val`, `sfixed64_val`) are shown as quoted strings `"0"` rather than bare numbers. This matches the canonical JSON representation where 64-bit values are quoted to preserve precision, as described in the gotchas section above.

## Hands-On: Using Scalar Fields in a Call

Now let us see scalar types in action. The `StreamingOutputCall` method accepts a `responseParameters` array where each entry's `size` field (`int32`) controls the byte size of the response payload:

```bash
grpcn invoke --plaintext \
  -d '{"responseParameters": [{"size": 10}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

In this request, we are using the `int32` field `size` set to `10`.

Expected output:

```json
{
  "payload": {
    "body": "AAECAwQFBgcICQ=="
  }
}
```

The server generated a 10-byte payload. The `body` field is `bytes`, so it appears as a Base64-encoded string. The bytes contain the sequential values 0, 1, 2, ..., 9.

We can also demonstrate `bool` and `bytes` scalars with `UnaryCall`, which echoes back the payload you send:

```bash
grpcn invoke --plaintext \
  -d '{"payload": {"body": "SGVsbG8="}, "fillUsername": true}' \
  localhost:9090 testing.TestService/UnaryCall
```

Here, `payload.body` is a `bytes` field (Base64 for "Hello") and `fillUsername` is a `bool`. The response echoes the payload:

```json
{
  "payload": {
    "body": "SGVsbG8="
  }
}
```

Fields we did not set (like `response_type`, `fill_oauth_scope`) kept their default values and were not serialised, so they do not appear in the response.

## Choosing the Right Integer Type

Here is a quick decision guide for picking the right integer type:

```
Is the value always non-negative?
  YES --> Is it a large, random value (hash, UUID)?
            YES --> fixed32 or fixed64
            NO  --> uint32 or uint64
  NO  --> Are negative values common?
            YES --> sint32 or sint64
            NO  --> int32 or int64

Do you need more than 32 bits?
  YES --> Use the 64-bit variant
  NO  --> Use the 32-bit variant
```

When in doubt, `int32` and `int64` are the safest defaults and the most commonly used types in practice.

## Summary

| Category | Types | Key Insight |
|----------|-------|-------------|
| Standard integers | `int32`, `int64`, `uint32`, `uint64` | Most common; varint encoding favors small positive values |
| Signed integers | `sint32`, `sint64` | ZigZag encoding; use when negatives are common |
| Fixed-width | `fixed32`, `fixed64`, `sfixed32`, `sfixed64` | Constant size; use for large or random values |
| Floating point | `float`, `double` | IEEE 754; be aware of precision limits |
| Boolean | `bool` | `true`/`false`; 0 bytes when `false` (default) |
| Text | `string` | Must be valid UTF-8 |
| Binary | `bytes` | Base64-encoded in JSON |

Remember the two critical JSON rules:

1. **64-bit integers are quoted strings** in JSON to prevent precision loss
2. **Bytes are Base64-encoded** in JSON

## What's Next

You now understand the atomic types that make up every protobuf message. But what about fields that can only take one of a predefined set of values? In [Chapter 4: Enums](04-enums.md), you will learn how protobuf defines named constants and how to use them in your gRPC calls.
