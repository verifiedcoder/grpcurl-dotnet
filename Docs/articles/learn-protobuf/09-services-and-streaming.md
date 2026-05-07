# Services and RPC Patterns

Up to this point, the series has focused on protobuf's type system -- scalars, enums, messages, collections, and well-known types. These are the building blocks of data. But data is only useful when it flows between systems. In this chapter, you will learn how protobuf defines **services** and **RPC methods**, and how gRPC implements the four fundamental communication patterns: unary, server streaming, client streaming, and bidirectional streaming.

## What Is gRPC?

gRPC is a high-performance Remote Procedure Call (RPC) framework originally developed by Google. It uses:

- **Protocol Buffers** for serialization -- the schema and encoding you have been learning throughout this series
- **HTTP/2** for transport -- providing multiplexed streams, header compression, and flow control
- **Code generation** to produce client and server stubs in dozens of languages

The combination of protobuf's compact binary encoding and HTTP/2's efficient transport makes gRPC significantly faster than JSON-over-HTTP/1.1 for most workloads. But the real power lies in the **service definition** -- a single `.proto` file that generates both client and server code, ensuring they always agree on the contract.

## Defining a Service

A gRPC service is defined in a `.proto` file using the `service` keyword. Inside the service block, you declare `rpc` methods, each specifying its request and response types:

```protobuf
service TestService {
  rpc EmptyCall(Empty) returns (Empty);
  rpc UnaryCall(SimpleRequest) returns (SimpleResponse);
  rpc StreamingOutputCall(StreamingOutputCallRequest)
      returns (stream StreamingOutputCallResponse);
  rpc StreamingInputCall(stream StreamingInputCallRequest)
      returns (StreamingInputCallResponse);
  rpc FullDuplexCall(stream StreamingOutputCallRequest)
      returns (stream StreamingOutputCallResponse);
  rpc HalfDuplexCall(stream StreamingOutputCallRequest)
      returns (stream StreamingOutputCallResponse);
}
```

This is the actual `TestService` definition from the TestServer. Let us break down what each element means:

| Element | Meaning |
|---------|---------|
| `service TestService` | Declares a service named `TestService` |
| `rpc MethodName(...)` | Declares an RPC method |
| `RequestType` | The message type the client sends |
| `returns (ResponseType)` | The message type the server sends back |
| `stream` keyword | Indicates that side sends multiple messages |

The `stream` keyword is the key differentiator. Its placement determines which of the four RPC patterns a method uses.

## The Four RPC Patterns

### Pattern 1: Unary RPC

**Single request, single response.** This is the most common pattern, equivalent to a traditional function call or REST API endpoint. The client sends one message and receives one message back.

```protobuf
rpc EmptyCall(Empty) returns (Empty);
rpc UnaryCall(SimpleRequest) returns (SimpleResponse);
```

Neither the request nor the response has the `stream` keyword.

#### Hands-On: Empty Call

The simplest possible unary RPC -- an empty request produces an empty response:

```bash
grpcurl.net invoke --plaintext -d '{}' localhost:9090 testing.TestService/EmptyCall
```

Expected output:

```json
{}
```

#### Hands-On: Unary Call with Data

A more useful example -- send a request with a payload and receive it echoed back:

```bash
grpcurl.net invoke --plaintext \
  -d '{"payload": {"body": "SGVsbG8gV29ybGQ="}}' \
  localhost:9090 testing.TestService/UnaryCall
```

Expected output:

```json
{
  "payload": {
    "body": "SGVsbG8gV29ybGQ="
  }
}
```

The server echoes back the payload we sent. The `body` field contains `SGVsbG8gV29ybGQ=`, which is the Base64 encoding of `Hello World`.

### Pattern 2: Server Streaming

**Single request, stream of responses.** The client sends one message, and the server responds with a sequence of messages. The `stream` keyword appears only on the return type.

```protobuf
rpc StreamingOutputCall(StreamingOutputCallRequest)
    returns (stream StreamingOutputCallResponse);
```

Server streaming is ideal for:

- **Downloading large datasets** in chunks
- **Real-time feeds** such as stock prices or log tails
- **Paginated results** delivered progressively

#### How It Works

1. The client sends a single `StreamingOutputCallRequest`
2. The server sends back multiple `StreamingOutputCallResponse` messages, one after another
3. The server signals completion by closing the stream

#### Hands-On: Server Streaming

The `StreamingOutputCall` method accepts `responseParameters` -- a repeated field where each entry tells the server to send one response of the specified size:

```bash
grpcurl.net invoke --plaintext \
  -d '{"responseParameters": [{"size": 10}, {"size": 20}, {"size": 30}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

Expected output (three separate response messages):

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

Notice how the output contains **three JSON objects**, one for each entry in `responseParameters`. The first has a 10-byte body, the second 20 bytes, and the third 30 bytes. Each body contains sequential bytes (0, 1, 2, ..., n-1). GrpCurl.Net prints each response as it arrives from the server.

#### Understanding the Request Message

Let us examine the `StreamingOutputCallRequest` message to understand all available options:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.StreamingOutputCallRequest
```

The `ResponseParameters` entries configure each response in the stream:

- `size` -- the byte size of the response payload
- `interval_us` -- the delay in microseconds before the server sends this response (useful for simulating slow streams)

### Pattern 3: Client Streaming

**Stream of requests, single response.** The client sends a sequence of messages, and the server responds with a single message after the client finishes. The `stream` keyword appears only on the request type.

```protobuf
rpc StreamingInputCall(stream StreamingInputCallRequest)
    returns (StreamingInputCallResponse);
```

Client streaming is ideal for:

- **Uploading large files** in chunks
- **Aggregation** -- sending many data points and receiving a summary
- **Batch operations** -- submitting multiple items for processing

#### How It Works

1. The client sends multiple `StreamingInputCallRequest` messages
2. The server accumulates them
3. After the client signals completion, the server sends back a single `StreamingInputCallResponse`

In the TestServer, the `StreamingInputCall` method receives payloads from the client and returns the total aggregated size.

#### Hands-On: Client Streaming via Stdin

When sending multiple messages to a client-streaming or bidirectional RPC, you provide each message as a separate JSON object. Using stdin with the `-d @` flag:

```bash
echo '{"payload":{"body":"YQ=="}}
{"payload":{"body":"YmI="}}
{"payload":{"body":"Y2Nj"}}' | \
grpcurl.net invoke --plaintext -d @ localhost:9090 testing.TestService/StreamingInputCall
```

Expected output:

```json
{
  "aggregated_payload_size": 6
}
```

The server received three payloads (`YQ==` decodes to "a" at 1 byte, `YmI=` decodes to "bb" at 2 bytes, `Y2Nj` decodes to "ccc" at 3 bytes) and returned the total size: 6 bytes.

#### Hands-On: Client Streaming via Concatenated JSON

You can also pass multiple JSON objects directly with `-d`, separating them with whitespace:

```bash
grpcurl.net invoke --plaintext \
  -d '{"payload":{"body":"YQ=="}} {"payload":{"body":"YmI="}}' \
  localhost:9090 testing.TestService/StreamingInputCall
```

Expected output:

```json
{
  "aggregated_payload_size": 3
}
```

Two payloads at 1 and 2 bytes yield a total of 3 bytes.

#### Understanding the Messages

The client-streaming messages are straightforward:

- `StreamingInputCallRequest` has a single field: `payload` containing a `Payload` message with a `body` (bytes, base64-encoded in JSON)
- `StreamingInputCallResponse` has a single field: `aggregated_payload_size` (int32) reporting the total bytes received

### Pattern 4: Bidirectional Streaming

**Stream of requests and stream of responses, simultaneously.** The `stream` keyword appears on both the request and the response:

```protobuf
rpc FullDuplexCall(stream StreamingOutputCallRequest)
    returns (stream StreamingOutputCallResponse);

rpc HalfDuplexCall(stream StreamingOutputCallRequest)
    returns (stream StreamingOutputCallResponse);
```

Bidirectional streaming is the most powerful and flexible pattern. It is ideal for:

- **Chat applications** -- messages flowing in both directions
- **Interactive protocols** -- request-response within a long-lived stream
- **Real-time collaboration** -- multiple participants sending and receiving updates

#### Full Duplex vs. Half Duplex

The TestServer provides two bidirectional methods that illustrate an important distinction:

| Method | Behavior |
|--------|----------|
| `FullDuplexCall` | **Immediate**: The server processes and responds to each client message as it arrives. Responses interleave with requests. |
| `HalfDuplexCall` | **Buffered**: The server waits for the client to finish sending all messages, then sends all responses at once. |

Both have the same protobuf signature -- the difference is in the server implementation, not the schema. This highlights that the `stream` keyword defines the *capability* (multiple messages allowed), not the *timing* (when messages are sent).

#### Hands-On: Full Duplex Streaming

Send multiple requests and observe the server responding to each one:

```bash
echo '{"responseParameters": [{"size": 10}]}
{"responseParameters": [{"size": 20}]}
{"responseParameters": [{"size": 30}]}' | \
grpcurl.net invoke --plaintext -d @ localhost:9090 testing.TestService/FullDuplexCall
```

Expected output:

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

Each request triggered an immediate response. In a full duplex scenario, the server could even send responses *before* the client finishes sending all its requests.

#### Hands-On: Half Duplex Streaming

HalfDuplexCall echoes back each request's payload. The server buffers all requests before sending any responses, so all output appears after the client finishes sending.

```bash
echo '{"payload":{"body":"SGVsbG8="}}
{"payload":{"body":"V29ybGQ="}}' | \
grpcurl.net invoke --plaintext -d @ localhost:9090 testing.TestService/HalfDuplexCall
```

Expected output:

```json
{
  "payload": {
    "body": "SGVsbG8="
  }
}
{
  "payload": {
    "body": "V29ybGQ="
  }
}
```

## Choosing the Right Pattern

| Pattern | When to Use | Example |
|---------|-------------|---------|
| **Unary** | Simple request-response. Most API calls. | Get user profile, create record, validate input |
| **Server streaming** | Server has multiple results or a continuous feed | Download file chunks, subscribe to notifications, list with cursor |
| **Client streaming** | Client has multiple items to send | Upload file chunks, batch insert, send telemetry |
| **Bidirectional** | Both sides need to send multiple messages | Chat, collaborative editing, interactive debugging |

> **Rule of thumb:** Start with unary. Move to streaming only when you have a genuine need for multiple messages in one direction. Bidirectional streaming is powerful but adds complexity to both client and server implementations.

## The Service Method Signature at a Glance

A visual summary of how the `stream` keyword determines the pattern:

```
rpc Method(Request)        returns (Response)               → Unary
rpc Method(Request)        returns (stream Response)         → Server streaming
rpc Method(stream Request) returns (Response)               → Client streaming
rpc Method(stream Request) returns (stream Response)         → Bidirectional
```

## Discovering Services with GrpCurl.Net

GrpCurl.Net's `list` and `describe` commands are your primary tools for understanding any gRPC service:

```bash
# List all services on a server
grpcurl.net list --plaintext localhost:9090

# List all methods on a specific service
grpcurl.net list --plaintext localhost:9090 testing.TestService

# Describe a service (shows all method signatures)
grpcurl.net describe --plaintext localhost:9090 testing.TestService

# Describe a specific method
grpcurl.net describe --plaintext localhost:9090 testing.TestService.UnaryCall

# Describe a request message with a template
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.StreamingOutputCallRequest
```

These commands work through **server reflection** -- the server describes its own API at runtime. This means you can explore any reflection-enabled gRPC server without having its `.proto` files on hand.

## Recap

In this chapter you learned:

- **gRPC** is an RPC framework that uses protobuf for serialization and HTTP/2 for transport
- **Service definitions** use the `service` and `rpc` keywords to declare methods with typed request and response messages
- The **`stream` keyword** determines the RPC pattern: its presence (or absence) on the request and response types distinguishes the four patterns
- **Unary RPCs** are simple request-response calls and the most common pattern
- **Server streaming** delivers multiple responses from a single request
- **Client streaming** sends multiple requests and receives a single aggregated response
- **Bidirectional streaming** allows both sides to send multiple messages, with full duplex (immediate) and half duplex (buffered) being implementation choices
- GrpCurl.Net supports all four patterns, using **`-d @`** with stdin or **concatenated JSON** to send multiple messages

## What's Next

You now understand how data flows through gRPC services. But what happens to fields that are not set? How does protobuf handle default values, and how does it map to JSON? In [Chapter 10: Default Values, Field Presence, and JSON Mapping](10-default-values-and-json.md), you will master the serialization details that every protobuf developer needs to know.
