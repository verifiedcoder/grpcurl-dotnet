# Composing Messages

In the previous chapters you worked with scalar fields and enums -- simple, flat values that live directly inside a message. Real-world data, however, is rarely flat. A purchase order contains line items. A user profile contains an address. A request might carry a payload alongside configuration options.

Protocol Buffers handles this through **message composition**: a message field whose type is another message. This chapter shows you how to build, inspect, and send nested message structures using GrpCurl.Net.

## What is Message Composition?

When a message field has a type that is itself a message (rather than a scalar like `int32` or `string`), the field acts as an **embedded object**. Consider this excerpt from the TestServer's `test.proto`:

```protobuf
message Payload {
  PayloadType type = 1;
  bytes body = 2;
}

message EchoStatus {
  int32 code = 1;
  string message = 2;
}

message SimpleRequest {
  PayloadType response_type = 1;
  int32 response_size = 2;
  Payload payload = 3;
  bool fill_username = 4;
  bool fill_oauth_scope = 5;
  EchoStatus response_status = 7;
}
```

`SimpleRequest` has two scalar fields (`response_size`, `fill_username`, `fill_oauth_scope`), one enum field (`response_type`), and two **message fields**: `payload` (of type `Payload`) and `response_status` (of type `EchoStatus`). Each message field is a complete, self-contained structure embedded inside the parent.

Think of it like object composition in any programming language. A `SimpleRequest` _has a_ `Payload` and _has an_ `EchoStatus`, just as a Java or C# class might contain references to other objects.

## Discovering Nested Structures

Before building a request by hand, you can ask GrpCurl.Net to show you the full structure of a message, including all nested types expanded into a ready-to-use JSON template:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
```

The output includes the proto definition followed by a JSON template showing every field with its default value:

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

Several things are visible here:

- **Nested messages appear as nested JSON objects.** The `payload` field contains a `type` and `body` sub-object. The `response_status` field contains `code` and `message`.
- **Field names use proto snake_case.** The template uses the original proto field names (e.g., `response_size`, `fill_username`). GrpCurl.Net accepts both snake_case and camelCase forms in requests.
- **All fields show their default values.** This gives you a complete skeleton you can copy, fill in, and send.
- **The proto definition is shown first.** This lets you see both the schema and the JSON shape together.

> [!TIP]
> The `--msg-template` flag is one of the most useful tools in your GrpCurl.Net toolbox. Whenever you face an unfamiliar message type, run `describe --msg-template` first. It saves you from having to read through `.proto` files to figure out the expected JSON shape.

## Building Nested JSON Requests

Now that you know the structure, you can construct a request with nested data. You only need to include the fields you want to set -- proto3 uses default values for anything you omit.

### Sending a Payload

The `Payload` message has a `type` field (an enum) and a `body` field (of type `bytes`). Let's send a request with a populated payload:

```bash
grpcurl.net invoke --plaintext \
  -d '{"payload": {"type": "COMPRESSABLE", "body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

There are two important details in this example:

1. **The nested object.** The `payload` value is a JSON object `{"type": "COMPRESSABLE", "body": "SGVsbG8="}`, which maps directly to the `Payload` message definition. You can nest as many levels as the schema requires.

2. **Base64 encoding for bytes.** The `body` field is declared as `bytes` in protobuf. In JSON, byte arrays are represented as **base64-encoded strings**. The value `"SGVsbG8="` is the base64 encoding of the ASCII string `"Hello"`. If you need to encode your own data, most systems provide a base64 utility:

   ```bash
   echo -n "Hello" | base64
   # Output: SGVsbG8=
   ```

### EchoStatus as a Nested Message Example

The `response_status` field on `SimpleRequest` is of type `EchoStatus`, which is another good example of nested message composition. You can explore its structure independently:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.EchoStatus
```

This will show you the `EchoStatus` message definition along with its JSON template:

```
testing.EchoStatus is a message:
message EchoStatus {
  int32 code = 1;
  string message = 2;
}

Message template:
{
  "code": 0,
  "message": ""
}
```

`EchoStatus` contains an `int32 code` field and a `string message` field -- two scalar fields packaged together into a single reusable type. This is a good example of how nested messages allow structured data to be embedded within a parent message. Rather than adding separate `status_code` and `status_message` scalar fields directly on `SimpleRequest`, the proto schema groups them into their own `EchoStatus` type. That grouping keeps the parent message organised and makes the status concept reusable across other messages (for example, `StreamingOutputCallRequest` also has a `response_status` field of the same type).

> [!NOTE]
> The TestServer's `UnaryCall` implementation does not act on the `response_status` field -- it simply echoes back the request payload. The value of `EchoStatus` here is as a **structural** example: it shows how protocol buffers let you compose a meaningful sub-object (an error status with both a code and a descriptive message) inside a larger request.

## Null vs Empty Nested Messages

An important distinction in protobuf is the difference between an **unset** message field and an **empty** message field:

- **Unset (null).** If you omit a message field entirely, it is not present in the serialised data. In JSON output, it will either be absent or shown as `null` (depending on whether `--emit-defaults` is used).

- **Empty but present.** If you set a message field to an empty object `{}`, the field _is_ present in the serialised data, but all of its sub-fields have their default values.

For example, these two requests are different:

```json
// payload is UNSET -- the server receives no payload field at all
{"response_size": 10}

// payload is SET to an empty Payload message
// (type defaults to COMPRESSABLE, body defaults to empty bytes)
{"response_size": 10, "payload": {}}
```

In many scenarios the practical difference is negligible, but some server implementations distinguish between "field not provided" and "field provided with defaults." Understanding this distinction becomes especially important when you work with wrapper types and field presence semantics in later chapters.

## Multiple Levels of Nesting

Message composition is not limited to one level. Consider `StreamingOutputCallRequest`:

```protobuf
message ResponseParameters {
  int32 size = 1;
  int32 interval_us = 2;
}

message StreamingOutputCallRequest {
  PayloadType response_type = 1;
  repeated ResponseParameters response_parameters = 2;
  Payload payload = 3;
  EchoStatus response_status = 7;
}
```

This message contains:
- `response_type` -- a scalar enum
- `response_parameters` -- a **list** of `ResponseParameters` messages (you will explore `repeated` fields in the next chapter)
- `payload` -- a nested `Payload` message
- `response_status` -- a nested `EchoStatus` message

You can discover the full template:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.StreamingOutputCallRequest
```

And construct a request that populates multiple nested layers:

```bash
grpcurl.net invoke --plaintext \
  -d '{"responseType": "COMPRESSABLE", "responseParameters": [{"size": 10}], "payload": {"type": "COMPRESSABLE", "body": "SGVsbG8="}}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

Here, `StreamingOutputCallRequest` contains a `Payload` which itself has two fields. The request also includes `responseParameters`, which is a list of `ResponseParameters` messages -- a pattern you will explore in depth in the next chapter on collections.

## Key Takeaways

| Concept | Detail |
|---------|--------|
| **Message fields** | A field whose type is another message, appearing as a nested JSON object |
| **Bytes in JSON** | The `bytes` type is represented as a base64-encoded string |
| **snake_case field names** | Templates and responses use proto `snake_case` field names; camelCase is also accepted in requests |
| **Discovering structure** | Use `grpcurl.net describe --msg-template` to see the full nested template |
| **Null vs empty** | Omitting a message field (null) is different from setting it to `{}` (empty) |
| **Arbitrary depth** | Messages can nest other messages to any depth the schema defines |

## What's Next

Now that you can compose messages within messages, the next chapter introduces **collections** -- repeated fields (arrays) and map fields (dictionaries) -- which let you work with variable-length lists of values and key-value pairs.

**Next: [Collections: Repeated Fields and Maps](06-collections.md)**
