# Examples

This page provides practical examples for common GrpCurl.Net use cases.

## Service Discovery

### List All Services

```bash
grpcurl.net list --plaintext localhost:9090
```

### List Services with Verbose Output

```bash
grpcurl.net list --plaintext -v localhost:9090
```

Output includes connection details and timing:
```
[dim]Connecting to localhost:9090...[/]
[dim]Protocol: HTTP/2 (plaintext)[/]
[dim]Connected successfully, querying server reflection...[/]
┌──────────────────────────────────────────────────┐
│ Service                                          │
├──────────────────────────────────────────────────┤
│ grpc.reflection.v1alpha.ServerReflection         │
│ testing.TestService                              │
└──────────────────────────────────────────────────┘

Total: 2 service(s)
[dim]Operation completed in 45ms[/]
```

### List Methods for a Service

```bash
grpcurl list --plaintext localhost:9090 testing.TestService
```

---

## Describing Services and Messages

### Describe a Service

```bash
grpcurl.net describe --plaintext localhost:9090 testing.TestService
```

### Describe a Message Type

```bash
grpcurl.net describe --plaintext localhost:9090 testing.SimpleRequest
```

### Get JSON Template for a Message

Very useful for understanding the expected request format:

```bash
grpcurl.net describe --plaintext --msg-template localhost:9090 testing.SimpleRequest
```

Output:
```json
{
  "responseType": "COMPRESSABLE",
  "responseSize": 0,
  "payload": {
    "type": "COMPRESSABLE",
    "body": ""
  },
  "fillUsername": false,
  "fillOauthScope": false,
  "responseStatus": {
    "code": 0,
    "message": ""
  }
}
```

---

## Invoking Methods

### Simple Unary Call

```bash
grpcurl.net invoke --plaintext localhost:9090 testing.TestService/EmptyCall
```

### Unary Call with Request Data

```bash
grpcurl.net invoke --plaintext \
  -d '{"response_size": 20, "fill_username": true}' \
  localhost:9090 testing.TestService/UnaryCall
```

### Server Streaming

Request one message, receive multiple responses:

```bash
grpcurl.net invoke --plaintext \
  -d '{"response_parameters": [{"size": 10}, {"size": 20}, {"size": 30}]}' \
  localhost:9090 testing.TestService/StreamingOutputCall
```

Output (multiple JSON objects):
```json
{"payload":{"body":"AAAAAAAAAA"}}
{"payload":{"body":"AAAAAAAAAAAAAAAAAAAA"}}
{"payload":{"body":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}}
```

### Client Streaming

Send multiple messages, receive one response:

```bash
echo '{"payload": {"body": "AAAA"}}
{"payload": {"body": "BBBB"}}
{"payload": {"body": "CCCC"}}' | \
grpcurl.net invoke --plaintext -d @ localhost:9090 testing.TestService/StreamingInputCall
```

### Bidirectional Streaming

Send and receive multiple messages:

```bash
echo '{"response_parameters": [{"size": 10}]}
{"response_parameters": [{"size": 20}]}' | \
grpcurl.net invoke --plaintext -d @ localhost:9090 testing.TestService/FullDuplexCall
```

---

## Working with Headers

### Add Custom Headers

```bash
grpcurl.net invoke --plaintext \
  -H "Authorization: Bearer my-token" \
  -H "X-Request-Id: req-12345" \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

### Different Headers for Reflection vs RPC

Use `--reflect-header` for reflection-only headers and `--rpc-header` for RPC-only headers:

```bash
grpcurl.net invoke --plaintext \
  --reflect-header "X-Reflect-Auth: reflect-token" \
  --rpc-header "X-RPC-Auth: rpc-token" \
  -H "X-Common: shared-value" \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

### Headers with Environment Variables

```bash
export AUTH_TOKEN="my-secret-token"
grpcurl.net invoke --plaintext \
  -H "Authorization: Bearer ${AUTH_TOKEN}" \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

---

## Using Protoset Files

### Generate a Protoset File

Using `protoc`:
```bash
protoc --descriptor_set_out=service.protoset \
  --include_imports \
  service.proto
```

Or export from a running server:
```bash
grpcurl.net list --plaintext --protoset-out service.protoset localhost:9090
```

### List Services from Protoset (Offline)

```bash
grpcurl.net list --protoset service.protoset
```

### Invoke Method Using Protoset

```bash
grpcurl.net invoke --plaintext \
  --protoset service.protoset \
  -d '{"name": "World"}' \
  localhost:9090 my.package.Service/SayHello
```

---

## Verbose and Timing Output

### Verbose Mode

```bash
grpcurl.net invoke --plaintext -v \
  -d '{"response_size": 10}' \
  localhost:9090 testing.TestService/UnaryCall
```

### Very Verbose Mode with Timing

```bash
grpcurl.net invoke --plaintext --vv \
  -d '{"response_size": 10}' \
  localhost:9090 testing.TestService/UnaryCall
```

Output includes detailed timing:
```
┌────────────────────────────┬──────────────┐
│ Phase                      │ Duration     │
├────────────────────────────┼──────────────┤
│ Connection Establishment   │ 12.34 ms     │
│ Schema Discovery           │ 23.45 ms     │
│ Request Preparation        │ 1.23 ms      │
│ RPC Channel Setup          │ 2.34 ms      │
│ Request Serialization      │ 0.45 ms      │
│ Network Round Trip         │ 15.67 ms     │
│ Response Deserialization   │ 0.56 ms      │
├────────────────────────────┼──────────────┤
│ Total                      │ 56.04 ms     │
└────────────────────────────┴──────────────┘

Request size: 24 bytes
Response size: 42 bytes
Messages: 1
```

---

## Timeouts and Limits

### Connection Timeout

```bash
grpcurl.net invoke --plaintext \
  --connect-timeout 5s \
  -d '{}' \
  localhost:9090 testing.TestService/EmptyCall
```

### Operation Timeout (gRPC Deadline)

```bash
grpcurl.net invoke --plaintext \
  --max-time 30s \
  -d '{}' \
  localhost:9090 testing.TestService/LongRunningOperation
```

### Message Size Limits

```bash
grpcurl.net invoke --plaintext \
  --max-msg-sz 10MB \
  -d '{"large_payload": "..."}' \
  localhost:9090 testing.TestService/ProcessLargeData
```

---

## Error Handling

### View Error as JSON

```bash
grpcurl.net invoke --plaintext \
  --format-error \
  -d '{"response_status": {"code": 3, "message": "Custom error"}}' \
  localhost:9090 testing.TestService/UnaryCall
```

Output:
```json
{
  "error": {
    "code": 3,
    "message": "Custom error",
    "status": "InvalidArgument"
  }
}
```

### Allow Unknown Fields

If your JSON contains fields not in the proto definition:

```bash
grpcurl.net invoke --plaintext \
  --allow-unknown-fields \
  -d '{"known_field": "value", "unknown_field": "ignored"}' \
  localhost:9090 testing.TestService/UnaryCall
```

---

## TLS Connections

### TLS with Default CA

```bash
grpcurl.net invoke \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### TLS with Custom CA Certificate

```bash
grpcurl.net invoke \
  --cacert /path/to/ca.crt \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### Mutual TLS (mTLS)

```bash
grpcurl.net invoke \
  --cacert /path/to/ca.crt \
  --cert /path/to/client.crt \
  --key /path/to/client.key \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### Skip Certificate Verification (Testing Only)

```bash
grpcurl.net invoke --insecure \
  -d '{}' \
  secure-server.example.com:443 my.package.Service/Method
```

### Custom Authority Header

Useful for virtual hosting or when the server expects a specific host:

```bash
grpcurl.net invoke \
  --authority api.example.com \
  -d '{}' \
  10.0.0.1:443 my.package.Service/Method
```
