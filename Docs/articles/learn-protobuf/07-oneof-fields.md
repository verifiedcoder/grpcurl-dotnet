# Oneof: Mutually Exclusive Fields

Sometimes a field should be exactly one of several possible types. A payment method might be a credit card, a bank transfer, or a digital wallet -- but never two at once. A configuration value might be a string, a number, or a boolean. Protocol Buffers models this pattern with **oneof fields**: a group of fields where setting one automatically clears the others.

This chapter explains how oneof works, how it appears in JSON, and how it differs from regular fields in important ways.

## The OneofMessage Definition

The TestServer includes a message that demonstrates the oneof concept:

```protobuf
message OneofMessage {
  oneof value {
    string string_value = 1;
    int32 int_value = 2;
    Payload message_value = 3;
  }
  string name = 4;
}
```

This definition has two parts:

1. **The `oneof value` group** containing three fields: `string_value`, `int_value`, and `message_value`. At most one of these three fields can be set at any given time. They are mutually exclusive.

2. **The `name` field** (field number 4), which sits **outside** the oneof group. It is a regular field that exists independently and can always be set regardless of which oneof variant is active.

Think of the oneof group as a tagged union or discriminated union if you are familiar with those concepts from other languages. The message carries one value from the group, and the "tag" tells you which variant it is.

## Discovering Oneof Fields

Use `describe --msg-template` to see the structure:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.OneofMessage
```

The output includes the proto definition followed by a JSON template with all fields from the oneof alongside regular fields:

```
testing.OneofMessage is a message:
message OneofMessage {
  oneof value {
    string string_value = 1;
    int32 int_value = 2;
    .testing.Payload message_value = 3;
  }
  string name = 4;
}

Message template:
{
  "string_value": "",
  "int_value": 0,
  "message_value": {
    "type": "COMPRESSABLE",
    "body": ""
  },
  "name": ""
}
```

> [!NOTE]
> The template shows all oneof fields with their default values for documentation purposes. In actual usage, you should only set **one** of the fields in the oneof group.

You can also examine the proto definition directly to confirm the oneof grouping:

```bash
grpcurl.net describe --plaintext localhost:9090 testing.OneofMessage
```

Expected output:

```
testing.OneofMessage is a message:
message OneofMessage {
  oneof value {
    string string_value = 1;
    int32 int_value = 2;
    .testing.Payload message_value = 3;
  }
  string name = 4;
}
```

This shows the raw proto definition, where the `oneof value { ... }` block makes the mutual exclusivity explicit. The three fields inside the oneof are indented under the group, while `name` sits outside it as a regular field.

## Setting Oneof Fields in JSON

To use a oneof field, you simply set the one variant you want. The other variants in the group are implicitly unset.

### String Variant

To set the string variant, include `string_value` in your JSON. The other oneof fields (`int_value`, `message_value`) are automatically cleared:

```json
{
  "string_value": "hello",
  "name": "test"
}
```

Here, `value` is the string `"hello"`, and `name` is `"test"`. The `int_value` and `message_value` fields are not set.

### Integer Variant

To carry an integer value instead:

```json
{
  "int_value": 42,
  "name": "test"
}
```

Now `value` is the integer `42`. The `string_value` and `message_value` fields are not set.

### Message Variant

The oneof can also hold a full message type. To set the `Payload` variant:

```json
{
  "message_value": {
    "type": "COMPRESSABLE",
    "body": "AA=="
  },
  "name": "test"
}
```

Now `value` is a `Payload` message. As you learned in the previous chapters, the `body` field uses base64 encoding because it is a `bytes` type.

### Mixing Regular Fields with Oneof

Notice that `name` appears in every example above. This is because `name` is **not part of the oneof group** -- it is an independent field that can always be set regardless of which oneof variant (if any) is active. A message can have any number of regular fields alongside a oneof group, and they do not interfere with each other.

## What Happens with Multiple Oneof Fields in JSON?

The protobuf specification says at most one field in a oneof group should be set. But what if you accidentally include more than one?

```json
{
  "string_value": "hello",
  "int_value": 42,
  "name": "test"
}
```

The behavior in this case is **implementation-dependent**. Most protobuf JSON parsers will accept this input but only keep the **last value** encountered during parsing. In the example above, depending on the parser's field processing order, you might end up with either `string_value` or `int_value` set, but not both.

> [!WARNING]
> Do not rely on any particular behavior when setting multiple oneof fields simultaneously. It is ambiguous by design. Always set exactly one field from a oneof group in your JSON to ensure predictable results.

## Field Presence and Oneof

Oneof fields have a significant difference from regular proto3 fields when it comes to **field presence** -- the ability to distinguish between "field is set" and "field is not set."

### Regular Proto3 Scalar Fields

For a regular proto3 scalar field (like `int32 age = 1`), there is no way to distinguish between "the field was explicitly set to `0`" and "the field was not set at all." Both produce the same serialised output. The default value (`0` for integers, `""` for strings, `false` for booleans) is indistinguishable from an unset field.

### Oneof Fields Have Explicit Presence

Oneof fields behave differently. Because the runtime tracks _which_ field in the oneof group is set, you get **explicit presence information**:

- If `int_value` is set to `0`, the message knows the `int_value` variant is active with value `0`. This is different from the oneof being empty.
- If no field in the oneof group is set, the oneof is **empty** -- and the runtime can tell you that none of the variants are active.

This distinction matters in applications where "not provided" and "provided with default value" have different meanings. For example, an update operation might interpret an empty oneof as "do not change this field" versus a zero-valued oneof as "change this field to zero."

## Default Values and Empty Oneof

Unlike regular proto3 fields that always have a default value, a oneof group has **no default**. If you send a message without setting any field in the oneof:

```json
{
  "name": "test"
}
```

The `value` oneof is simply empty. No variant is active. This is a valid state -- oneof groups are always optional.

When the server sends back a message with an empty oneof, none of the oneof fields will appear in the JSON output. You will only see the regular fields:

```json
{
  "name": "test"
}
```

This is another way oneof differs from a regular scalar field. A regular `int32` field that is unset will be serialised as `0` (its default). A oneof group that is unset will produce no output at all for any of its member fields.

## Exploring Interactively

The `OneofMessage` type is defined in the TestServer's `test.proto` but does not have a dedicated RPC method that uses it as a request or response type. This means you cannot invoke an RPC to send and receive `OneofMessage` instances directly.

However, you can still explore the type thoroughly using `grpcurl.net describe`:

```bash
# See the proto definition with the oneof block
grpcurl.net describe --plaintext localhost:9090 testing.OneofMessage

# See the full JSON template
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.OneofMessage
```

This is a common situation in real-world gRPC development. Not every message type is used directly in an RPC signature -- some are embedded as fields within other messages, or used internally by the server. The `describe` command lets you inspect any type the server exposes through reflection, regardless of whether it appears in an RPC method signature.

## Practical Use Cases for Oneof

Oneof fields appear frequently in real-world protobuf schemas. Common patterns include:

- **Polymorphic values.** A configuration field that can be a string, number, or boolean (similar to `OneofMessage`).
- **Request variants.** A single RPC that accepts different kinds of operations, where the oneof determines which operation to perform.
- **Result types.** A response that carries either a success payload or an error detail, but never both.
- **Transport payloads.** A message that wraps different content types (JSON blob, binary data, structured message), using oneof to indicate which format is present.

## Key Takeaways

| Concept | Detail |
|---------|--------|
| **`oneof` group** | A set of mutually exclusive fields -- at most one can be set at a time |
| **Regular fields alongside** | Fields outside the oneof group are independent and always settable |
| **JSON encoding** | Set exactly one field from the group; others are implicitly absent |
| **Multiple fields set** | Implementation-dependent (usually last wins) -- avoid doing this |
| **Explicit presence** | Oneof fields track whether they are set, unlike regular proto3 scalars |
| **No default** | An empty oneof has no active variant, which is distinct from any default value |
| **Discovery** | Use `grpcurl.net describe --msg-template` to see oneof fields and their types |

## What's Next

You have now covered the core type system of Protocol Buffers: scalars, enums, nested messages, collections, and oneof fields. The next chapter introduces **well-known types** -- Google's standard library of protobuf types for timestamps, durations, nullable wrappers, and dynamic JSON-like structures.

**Next: [Well-Known Types](08-well-known-types.md)**
