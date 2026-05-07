# Your First gRPC Call

In this chapter, you will start a gRPC server, discover what services it offers, and make your very first gRPC call using GrpCurl.Net. By the end, you will be comfortable navigating any gRPC API from the command line.

## Prerequisites

Before you begin, make sure you have:

- **.NET 10.0 SDK** installed and available on your PATH
- **GrpCurl.Net** cloned and built (see [Chapter 1](01-what-is-protobuf.md) if you have not done this yet)
- A terminal open in the repository root directory

## Step 1: Start the TestServer

GrpCurl.Net ships with a built-in test server that implements several gRPC services. This server is perfect for learning because it supports **server reflection**, meaning we can explore its API without needing any `.proto` files on hand.

Start the server by running:

```bash
dotnet run --project Tests/GrpCurl.Net.TestServer
```

You should see output indicating the server is listening. By default, the TestServer binds to **localhost:9090** using plaintext HTTP/2 (no TLS). Leave this terminal running and open a second terminal for the commands that follow.

> **Tip:** The `--plaintext` flag in the commands below tells GrpCurl.Net to connect without TLS. This matches the TestServer's configuration. Production gRPC servers typically use TLS, and you would omit this flag (or provide certificates) when connecting to them.

## Step 2: Discover Available Services

The first thing you will want to do when exploring an unfamiliar gRPC server is find out what services it offers. Run:

```bash
grpcurl.net list --plaintext localhost:9090
```

Expected output:

```
grpc.reflection.v1alpha.ServerReflection
testing.TestService
testing.UnimplementedService
```

Three services are listed. But how did GrpCurl.Net know about them?

### What Is Server Reflection?

gRPC servers can optionally expose a **reflection service** -- a special built-in service that describes all other services the server hosts. When you run `grpcurl.net list`, it connects to the server's reflection endpoint and asks: "What services do you have?"

Think of it like a restaurant menu: instead of guessing what dishes are available, you ask the waiter for the menu. The reflection service *is* that menu.

The first entry, `grpc.reflection.v1alpha.ServerReflection`, is the reflection service itself. The other two -- `testing.TestService` and `testing.UnimplementedService` -- are the application services we can call.

## Step 3: List Methods on a Service

Now let us zoom in on `testing.TestService` to see what methods (RPC endpoints) it provides:

```bash
grpcurl.net list --plaintext localhost:9090 testing.TestService
```

Expected output:

```
testing.TestService.EmptyCall
testing.TestService.FullDuplexCall
testing.TestService.HalfDuplexCall
testing.TestService.StreamingInputCall
testing.TestService.StreamingOutputCall
testing.TestService.UnaryCall
```

Six methods are available. The names hint at what each one does:

| Method | Description |
|--------|-------------|
| `EmptyCall` | Takes nothing, returns nothing -- the simplest possible RPC |
| `UnaryCall` | One request in, one response out -- the most common pattern |
| `StreamingOutputCall` | One request in, multiple responses back (server streaming) |
| `StreamingInputCall` | Multiple requests in, one response back (client streaming) |
| `FullDuplexCall` | Both sides stream simultaneously (bidirectional streaming) |
| `HalfDuplexCall` | Client finishes sending before server starts responding |

For now, we will focus on `EmptyCall` and `UnaryCall`. Streaming is covered in a later chapter.

## Step 4: Describe a Service

To see the full protobuf definition of a service -- including method signatures with their request and response types -- use the `describe` command:

```bash
grpcurl.net describe --plaintext localhost:9090 testing.TestService
```

Expected output:

```
testing.TestService is a service:
service TestService {
  rpc EmptyCall ( .testing.Empty ) returns ( .testing.Empty );
  rpc FullDuplexCall ( stream .testing.StreamingOutputCallRequest ) returns ( stream .testing.StreamingOutputCallResponse );
  rpc HalfDuplexCall ( stream .testing.StreamingOutputCallRequest ) returns ( stream .testing.StreamingOutputCallResponse );
  rpc StreamingInputCall ( stream .testing.StreamingInputCallRequest ) returns ( .testing.StreamingInputCallResponse );
  rpc StreamingOutputCall ( .testing.StreamingOutputCallRequest ) returns ( stream .testing.StreamingOutputCallResponse );
  rpc UnaryCall ( .testing.SimpleRequest ) returns ( .testing.SimpleResponse );
}
```

This tells us the exact input and output types for every method. For example, `UnaryCall` takes a `.testing.SimpleRequest` and returns a `.testing.SimpleResponse`. The `stream` keyword in front of a type means that side sends multiple messages rather than just one.

## Step 5: Make Your First Call

It is time to actually call the server. Let us start with the simplest possible RPC -- `EmptyCall`. This method takes an empty request and returns an empty response. It is the gRPC equivalent of a health check or ping.

```bash
grpcurl.net invoke --plaintext -d '{}' localhost:9090 testing.TestService/EmptyCall
```

Let us break down this command:

| Part | Meaning |
|------|---------|
| `grpcurl.net invoke` | Invoke (call) a gRPC method |
| `--plaintext` | Use unencrypted HTTP/2 (no TLS) |
| `-d '{}'` | Send this JSON as the request body (`{}` = empty object) |
| `localhost:9090` | The server address and port |
| `testing.TestService/EmptyCall` | The fully-qualified method: `package.Service/Method` |

Expected output:

```json
{}
```

An empty JSON object comes back. That is expected -- `EmptyCall` accepts an `Empty` message and returns an `Empty` message. There are no fields to populate in either direction.

Congratulations -- you just made your first gRPC call from the command line.

## Step 6: Explore Message Shapes with --msg-template

Before making a more interesting call, let us find out what fields `SimpleRequest` accepts. The `--msg-template` flag generates a JSON template showing every field with its default value:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
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

This template is incredibly useful. It shows you:

- **Field names** using proto field names (snake_case) -- for example, `response_size`, `fill_username`
- **Default values** for every type -- `0` for numbers, `""` for strings, `false` for booleans
- **Nested messages** like `payload` and `response_status`, fully expanded
- **Enum values** shown as their string names, like `"COMPRESSABLE"`
- **The proto definition** printed above the JSON template, so you can see both the schema and the JSON shape together

> **Note:** Protobuf field names in `.proto` files use `snake_case` (e.g., `response_size`), and the `--msg-template` output uses the same snake_case names. GrpCurl.Net accepts both snake_case and camelCase forms when constructing requests.

## Step 7: Make a Call with Data

Now let us make a more meaningful call. The TestServer's `UnaryCall` method echoes back whatever payload you send. We will send a `Payload` message containing a Base64-encoded body:

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

Let us trace what happened in this request/response cycle:

1. **GrpCurl.Net** connected to `localhost:9090` over plaintext HTTP/2.
2. It used **server reflection** to look up the schema for `testing.TestService/UnaryCall`, learning that it expects a `SimpleRequest` and returns a `SimpleResponse`.
3. It **serialised** your JSON data into a protobuf binary `SimpleRequest` message.
4. It **sent** the request to the server.
5. The server **processed** the request: it echoed back the payload from the request.
6. The server **returned** a `SimpleResponse` as protobuf binary.
7. GrpCurl.Net **deserialised** the binary response back to JSON and printed it.

All of this happened in a single round trip. The reflection lookup is cached, so subsequent calls to the same server are even faster.

### Understanding the Response

The response contains one field:

- `payload.body`: The Base64-encoded bytes we sent -- `"SGVsbG8gV29ybGQ="` decodes to the text `"Hello World"`

Fields we did *not* set (like `username` and `oauth_scope`) kept their default values (empty string) and were omitted from the response. In protobuf, **default-valued fields are not serialised**, which keeps messages compact.

## Recap

In this chapter you learned how to:

| Skill | Command |
|-------|---------|
| Start the TestServer | `dotnet run --project Tests/GrpCurl.Net.TestServer` |
| List services on a server | `grpcurl.net list --plaintext localhost:9090` |
| List methods on a service | `grpcurl.net list --plaintext localhost:9090 testing.TestService` |
| Describe a service or message | `grpcurl.net describe --plaintext localhost:9090 testing.TestService` |
| View a message template | `grpcurl.net describe --plaintext --msg-template localhost:9090 testing.SimpleRequest` |
| Invoke an RPC method | `grpcurl.net invoke --plaintext -d '{...}' localhost:9090 testing.TestService/UnaryCall` |

You also learned that:

- **Server reflection** allows tools like GrpCurl.Net to discover services without `.proto` files
- Protobuf uses **snake_case** in `.proto` definitions; GrpCurl.Net templates and responses use the same snake_case field names
- Fields with **default values** are omitted from responses to keep messages compact

## What's Next

Now that you can navigate and call gRPC services, it is time to understand the data types that make up protobuf messages. In [Chapter 3: Scalar Types](03-scalar-types.md), you will learn about the fundamental building blocks -- integers, floats, strings, and more -- that every protobuf message is built from.
