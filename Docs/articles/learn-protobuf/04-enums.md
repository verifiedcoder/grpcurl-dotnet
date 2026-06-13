# Enums: Defining Choices

Some fields should not accept arbitrary values. A payment status should be one of `PENDING`, `COMPLETED`, or `FAILED` -- not just any string. A compression format should be `GZIP` or `SNAPPY`, not the number 42. Protobuf handles this with **enums** (enumerations): a type that restricts a field to a predefined set of named values.

In this chapter, you will learn how enums work in proto3, how to discover them with GrpCurl.Net, and how to use them in your gRPC calls.

## Prerequisites

Make sure the TestServer is running:

```bash
dotnet run --project Tests/GrpCurl.Net.TestServer
```

## What Enums Represent

An enum defines a **closed set of named integer values**. Here is the `PayloadType` enum from the TestServer's proto definition:

```protobuf
enum PayloadType {
  COMPRESSABLE = 0;
  UNCOMPRESSABLE = 1;
  RANDOM = 2;
}
```

Each name maps to a numeric value:

| Name | Value |
|------|-------|
| `COMPRESSABLE` | 0 |
| `UNCOMPRESSABLE` | 1 |
| `RANDOM` | 2 |

When a message field has type `PayloadType`, it can only hold one of these three values. The field in the `.proto` file looks like this:

```protobuf
message SimpleRequest {
  PayloadType response_type = 1;  // Which compression to use for the response
  int32 response_size = 2;
  // ... other fields
}
```

## Proto3 Enum Rules

Proto3 enforces a few important rules for enums:

### Rule 1: The First Value Must Be Zero

Every proto3 enum **must** have a value with the number `0`, and it must be the first entry. This zero value serves as the default.

```protobuf
// Correct: first value is 0
enum Status {
  UNKNOWN = 0;
  ACTIVE = 1;
  INACTIVE = 2;
}

// INVALID: first value is not 0 -- this will not compile
enum BadStatus {
  ACTIVE = 1;
  INACTIVE = 2;
}
```

The zero value is typically named to represent an "unset" or "unknown" state. Common conventions include `UNKNOWN`, `UNSPECIFIED`, or a name with a `_UNSPECIFIED` suffix (e.g., `STATUS_UNSPECIFIED`).

### Rule 2: Values Must Be Unique (by Default)

Each numeric value in an enum must be unique:

```protobuf
// INVALID: duplicate value 1
enum Broken {
  A = 0;
  B = 1;
  C = 1;  // Error!
}
```

There is an escape hatch -- the `allow_alias` option -- that permits multiple names for the same value, but this is rarely used and beyond the scope of this tutorial.

### Rule 3: Values Must Be 32-bit Integers

Enum values are stored as `int32` on the wire, so they must be in the range -2,147,483,648 to 2,147,483,647. In practice, most enums use small positive numbers starting from 0.

## Discovering Enums with GrpCurl.Net

You can inspect an enum type just like you inspect a message. Use the `describe` command:

```bash
grpcn describe --plaintext localhost:9090 testing.PayloadType
```

Expected output:

```
testing.PayloadType is an enum:
enum PayloadType {
  COMPRESSABLE = 0;
  UNCOMPRESSABLE = 1;
  RANDOM = 2;
}
```

This shows the complete definition, including all named values and their numeric assignments.

You can also see enums in context by examining messages that use them. Let us look at `SimpleRequest`:

```bash
grpcn describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
```

Expected output:

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

Notice that `response_type` shows `"COMPRESSABLE"` (a string) rather than `0` (a number). In JSON, enum values are represented by their **name** by default. The `payload.type` field also uses `PayloadType` and shows the same default.

## Using Enums in JSON

When sending enum values in a gRPC request via GrpCurl.Net, you have two options:

### Option 1: By Name (Recommended)

Use the string name of the enum value. This is the **preferred** approach because it is self-documenting and less error-prone. The `StreamingOutputCall` method reflects the `responseType` enum back in the response payload, making it easy to see the effect:

```bash
grpcn invoke --plaintext \
  -d '{"responseType": "UNCOMPRESSABLE", "responseParameters": [{"size": 5}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

The value `"UNCOMPRESSABLE"` is a quoted string that matches exactly the name defined in the `.proto` file. The name is **case-sensitive** -- `"UNCOMPRESSABLE"` works but `"uncompressable"` or `"Uncompressable"` will not.

### Option 2: By Number

Use the integer value directly. This is equivalent but harder to read:

```bash
grpcn invoke --plaintext \
  -d '{"responseType": 1, "responseParameters": [{"size": 5}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

Here, `1` corresponds to `UNCOMPRESSABLE` (as defined in the enum). Both commands produce identical results on the wire.

**When might you use numeric values?**

- In scripts that compute enum values dynamically
- When working with enums that have many values and you are iterating over them
- When the proto definition is not readily available

In all other cases, prefer the name for clarity.

### Comparing Both Forms

These two commands are exactly equivalent:

```bash
# By name (recommended -- clear and self-documenting)
grpcn invoke --plaintext \
  -d '{"responseType": "UNCOMPRESSABLE", "responseParameters": [{"size": 5}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall

# By number (equivalent -- but what does 1 mean?)
grpcn invoke --plaintext \
  -d '{"responseType": 1, "responseParameters": [{"size": 5}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

Both will return a response like:

```json
{
  "payload": {
    "type": "UNCOMPRESSABLE",
    "body": "AAECAwQ="
  }
}
```

Notice that the **response** always uses the string name (`"UNCOMPRESSABLE"`), regardless of whether you sent the request using a name or number.

## Default Values

When an enum field is not explicitly set in a request, it takes the **zero value** -- the enum member with value `0`. For `PayloadType`, that is `COMPRESSABLE`:

```bash
# responseType is not set, so it defaults to COMPRESSABLE (value 0)
grpcn invoke --plaintext \
  -d '{"responseParameters": [{"size": 5}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

Expected output:

```json
{
  "payload": {
    "body": "AAECAwQ="
  }
}
```

Notice that `payload.type` does not appear in the response. That is because it holds the default value `COMPRESSABLE` (0), and proto3 omits default-valued fields from serialization. The payload type *is* `COMPRESSABLE`; it is just not printed because there is nothing to distinguish it from "not set."

This is why the zero value should represent a sensible default or an explicit "unspecified" state. If you see an enum field missing from a response, it means the value is `0`.

## Unknown Enum Values

What happens if the server sends an enum value that your client does not recognize? This can happen when:

- The server has been updated with new enum values that the client's proto definition does not include
- You explicitly send a numeric value that is not in the enum definition

Proto3 handles this gracefully: **unknown enum values are preserved as their numeric value**. They are not rejected or silently dropped.

For example, if a response contained a `PayloadType` field with the numeric value `99` (which is not defined in the enum), the JSON representation would show:

```json
{
  "response_type": 99
}
```

Rather than a string name, you see the raw number. This tells you the value is not one of the known enum members. This behavior is important for **forward compatibility** -- older clients can still process messages from newer servers without crashing, even if they do not understand every enum value.

### Best Practices for Handling Unknown Values

- **Always handle the possibility of unknown enum values** in production code
- Use the zero value as an "unspecified" or "unknown" sentinel so that unset fields have a clearly defined meaning
- When adding new values to an enum, add them at the end with the next available number to maintain clarity

## Enums vs. Integers

You might wonder: why not just use an `int32` field instead of an enum? Enums provide several advantages:

| Feature | Enum | int32 |
|---------|------|-------|
| Self-documenting | Yes -- names describe meaning | No -- just a number |
| Validation | Defined set of valid values | Any 32-bit integer |
| JSON representation | Human-readable strings | Raw numbers |
| IDE support | Autocomplete, type checking | None |
| Schema evolution | Clear addition of new values | No structure |

Use enums whenever a field has a known, finite set of meaningful values. Use integers when the value is truly numeric (counts, sizes, IDs).

## Summary

| Concept | Detail |
|---------|--------|
| What enums are | A fixed set of named integer values |
| Zero value | Required as the first entry; serves as the default |
| JSON by name | `"COMPRESSABLE"` -- preferred, case-sensitive |
| JSON by number | `1` -- equivalent but less readable |
| Default behavior | Unset enum fields take value `0` (omitted in JSON) |
| Unknown values | Preserved as raw integers; not rejected |

Key commands for working with enums:

```bash
# Discover an enum's values
grpcn describe --plaintext localhost:9090 testing.PayloadType

# See enums in message context
grpcn describe --plaintext --msg-template localhost:9090 testing.SimpleRequest

# Use enum by name in a call
grpcn invoke --plaintext \
  -d '{"responseType": "UNCOMPRESSABLE", "responseParameters": [{"size": 5}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

## What's Next

You have now covered scalar types and enums -- the fundamental value types in protobuf. In [Chapter 5](05-nested-messages.md), you will learn about **messages**: how to compose these building blocks into structured, nested data types that represent real-world entities.
