# Collections: Repeated Fields and Maps

So far, every field you have worked with holds a single value -- one integer, one string, one nested message. But what if you need to send a list of items, or a set of key-value pairs? Protocol Buffers provides two collection types for this: **repeated fields** (ordered lists) and **map fields** (key-value dictionaries).

This chapter covers both, showing you how to discover, construct, and send collection data through GrpCurl.Net.

## Part 1: Repeated Fields (Arrays)

### What `repeated` Means

The `repeated` keyword before a field type means the field holds **zero or more values** of that type, in order. It is protobuf's equivalent of an array or list.

```protobuf
message StreamingOutputCallRequest {
  PayloadType response_type = 1;
  repeated ResponseParameters response_parameters = 2;
  Payload payload = 3;
  EchoStatus response_status = 7;
}
```

The field `response_parameters` is declared as `repeated ResponseParameters`. This means it can hold zero, one, or many `ResponseParameters` messages, and their order is preserved.

### JSON Representation

In JSON, repeated fields are represented as **arrays** (`[]`):

```json
{
  "response_parameters": [
    {"size": 10},
    {"size": 20},
    {"size": 30}
  ]
}
```

Each element in the array corresponds to one instance of the repeated field's type.

### Discovering Repeated Fields

Use `--msg-template` to see which fields are repeated. Repeated fields will appear as arrays in the template:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.StreamingOutputCallRequest
```

The template output shows the proto definition followed by the JSON template, with repeated fields as arrays containing a single example element:

```
testing.StreamingOutputCallRequest is a message:
message StreamingOutputCallRequest {
  .testing.PayloadType response_type = 1;
  repeated .testing.ResponseParameters response_parameters = 2;
  .testing.Payload payload = 3;
  .testing.EchoStatus response_status = 7;
}

Message template:
{
  "response_type": "COMPRESSABLE",
  "response_parameters": [
    {
      "size": 0,
      "interval_us": 0
    }
  ],
  "payload": {
    "type": "COMPRESSABLE",
    "body": ""
  },
  "response_status": {
    "code": 0,
    "message": ""
  }
}
```

The `response_parameters` field shows up as an array with one template element, indicating it is a repeated field of `ResponseParameters` messages.

### Arrays of Messages

The most common use of repeated fields is to hold multiple instances of a message type. The `StreamingOutputCall` method accepts a list of `ResponseParameters`, where each entry specifies the size of one response chunk:

```bash
grpcurl.net invoke --plaintext \
  -d '{"responseParameters": [{"size": 10}, {"size": 20}, {"size": 30}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

This sends a single request containing three `ResponseParameters` entries. The server responds with three separate streaming messages, each containing a payload of sequential bytes (0, 1, 2, ...) sized according to the corresponding parameter. You will see output similar to:

```json
{
  "payload": {
    "body": "AAECAwQFBgcICQ=="
  }
}
{
  "payload": {
    "body": "AAECAwQFBgcICQoLDA0ODxAREhM="
  }
}
{
  "payload": {
    "body": "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwd"
  }
}
```

Each response is a distinct message in the stream. The bodies contain sequential byte values starting from 0, Base64-encoded.

### Arrays of Scalars

Repeated fields are not limited to message types. You can also have arrays of scalars. The TestServer defines a message specifically for this:

```protobuf
message RepeatedScalarsTest {
  repeated int32 int_values = 1;
  repeated bool bool_values = 2;
  repeated PayloadType enum_values = 3;
  repeated double double_values = 4;
  // ... more repeated scalar fields
}
```

Discover its full structure:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.RepeatedScalarsTest
```

In JSON, arrays of scalars look exactly as you would expect:

```json
{
  "int_values": [1, 2, 3, 4, 5],
  "bool_values": [true, false, true],
  "enum_values": ["COMPRESSABLE", "UNCOMPRESSABLE"],
  "double_values": [3.14, 2.718, 1.414]
}
```

Each scalar type follows the same JSON mapping rules you learned in earlier chapters, just wrapped in an array.

### Empty Arrays and Omission

In proto3, an empty repeated field and an omitted repeated field are semantically identical -- both mean "no elements." You can express this in JSON in two equivalent ways:

```json
// Explicitly empty -- zero elements
{"response_parameters": []}

// Omitted entirely -- also zero elements
{}
```

Both produce the same serialised protobuf bytes. When the server sends back a response with an empty repeated field, GrpCurl.Net will omit the field from the JSON output (unless you use `--emit-defaults`).

### Ordering is Preserved

The order of elements in a repeated field is significant and is preserved through serialization and deserialization. If you send `[{"size": 30}, {"size": 10}, {"size": 20}]`, the server receives the elements in that exact order. This makes repeated fields suitable for ordered lists where position matters.

---

## Part 2: Map Fields (Dictionaries)

### What `map<K,V>` Means

A map field is a collection of **key-value pairs**, similar to a dictionary, hash map, or associative array in programming languages. The syntax in protobuf is:

```protobuf
map<KeyType, ValueType> field_name = N;
```

The TestServer defines a comprehensive example:

```protobuf
message MapFieldsMessage {
  map<string, string> string_map = 1;
  map<string, int32> int_map = 2;
  map<int32, string> int_key_map = 3;
  map<string, Payload> message_map = 4;
  map<string, PayloadType> enum_map = 5;
}
```

This single message demonstrates five map variations: string-to-string, string-to-integer, integer-to-string, string-to-message, and string-to-enum.

### Supported Key Types

Map keys must be a scalar type that can act as a unique identifier. The following types are allowed as map keys:

- **String** (`string`) -- the most common key type
- **Integer types** (`int32`, `int64`, `uint32`, `uint64`, `sint32`, `sint64`, `fixed32`, `fixed64`, `sfixed32`, `sfixed64`)
- **Boolean** (`bool`)

The following types are **not allowed** as map keys:

- `float` and `double` (floating-point numbers are unreliable as keys due to precision issues)
- `bytes` (byte arrays are not hashable in a consistent way)
- Enums and messages (complex types cannot serve as keys)

Map values, on the other hand, can be **any type** including messages, enums, and scalars.

### JSON Representation

In JSON, map fields are represented as **objects** (`{}`), where each property is a key-value pair:

```json
{
  "string_map": {
    "key1": "value1",
    "key2": "value2"
  }
}
```

This is a natural fit -- JSON objects _are_ key-value maps.

### Discovering Map Fields

Use `describe --msg-template` to see the map fields and their expected types:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.MapFieldsMessage
```

The template output will show each map field as a JSON object with example entries, helping you understand the expected key and value types.

### Examples for Each Map Type

#### String-to-String Map

The simplest map type. Both keys and values are strings:

```json
{
  "string_map": {
    "name": "Alice",
    "role": "engineer",
    "team": "platform"
  }
}
```

#### String-to-Integer Map

Keys are strings, values are integers:

```json
{
  "int_map": {
    "apples": 42,
    "oranges": 99,
    "bananas": 7
  }
}
```

#### Integer-Key Map

When the key type is an integer (like `int32`), an important JSON detail comes into play: **JSON object keys must always be strings**. So integer keys are represented as string-encoded numbers:

```json
{
  "int_key_map": {
    "1": "one",
    "2": "two",
    "100": "one hundred"
  }
}
```

Even though the proto definition says `map<int32, string>`, the JSON keys are `"1"`, `"2"`, and `"100"` (strings), not bare integers. Protobuf's JSON mapping handles the conversion automatically. The same rule applies to `bool` keys -- they become `"true"` and `"false"` strings in JSON.

#### Message-Value Map

Map values can be full message types. Here, each value is a `Payload` message:

```json
{
  "message_map": {
    "item1": {
      "type": "COMPRESSABLE",
      "body": "AA=="
    },
    "item2": {
      "type": "UNCOMPRESSABLE",
      "body": "SGVsbG8="
    }
  }
}
```

This combines the map concept with the nested message composition you learned in the previous chapter. Each map value is a complete `Payload` object.

#### Enum-Value Map

Map values can also be enum types. Here, each value is a `PayloadType` enum:

```json
{
  "enum_map": {
    "primary": "COMPRESSABLE",
    "secondary": "UNCOMPRESSABLE",
    "fallback": "RANDOM"
  }
}
```

As with standalone enum fields, you can use either the string name (`"COMPRESSABLE"`) or the numeric value (`0`) for each map value.

### Map Ordering is Not Guaranteed

Unlike repeated fields, **map fields have no guaranteed order**. The order in which key-value pairs appear in serialised data or JSON output may differ from the order in which you specified them. If you send:

```json
{"string_map": {"z": "last", "a": "first", "m": "middle"}}
```

The server might process and return the entries in any order. Do not rely on map ordering for correctness.

### Duplicate Keys: Last Value Wins

If you provide the same key more than once in JSON input, the behavior is implementation-dependent, but generally the **last value wins**:

```json
{
  "string_map": {
    "key": "first",
    "key": "second"
  }
}
```

In this case, `"key"` will most likely have the value `"second"`. However, relying on this behavior is discouraged. The protobuf specification states that map keys must be unique, and some implementations may reject duplicate keys.

---

## Combining Collections with Other Types

In practice, messages often combine repeated fields, map fields, scalar fields, and nested messages. Everything you have learned composes naturally. For example, you could have a repeated field of messages where each message contains a map field, or a map whose values are messages containing repeated fields.

The key principle: **protobuf types compose freely**. Scalars, enums, messages, repeated fields, and maps can all be combined to model the data structures your application needs.

## Key Takeaways

| Concept | Detail |
|---------|--------|
| **`repeated` fields** | Zero or more values of a type, represented as JSON arrays `[]` |
| **Ordering** | Repeated fields preserve element order; map fields do not |
| **Empty collections** | Omitting a repeated field or passing `[]` both mean "no elements" |
| **`map<K,V>` fields** | Key-value pairs, represented as JSON objects `{}` |
| **Valid key types** | `string`, integer types, `bool` -- NOT `float`, `bytes`, enums, or messages |
| **Integer/bool keys in JSON** | Become string-encoded (`"1"`, `"true"`) because JSON keys must be strings |
| **Duplicate keys** | Generally last value wins, but avoid relying on this |

## What's Next

You have now covered flat values (scalars, enums), nested messages, and collections (repeated fields, maps). The next chapter introduces **oneof fields** -- a way to model mutually exclusive choices where only one of several possible fields can be set at a time.

**Next: [Oneof: Mutually Exclusive Fields](07-oneof-fields.md)**
