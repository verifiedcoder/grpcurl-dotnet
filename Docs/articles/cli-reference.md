# CLI Reference

Complete reference for all GrpCurl.Net commands and options.

## Commands

GrpCurl.Net provides three main commands:

- `list` - List services or methods
- `describe` - Describe protobuf symbols
- `invoke` - Invoke gRPC methods

## Global Options

These options are available for all commands:

| Option | Description |
|--------|-------------|
| `--plaintext` | Use plaintext HTTP/2 (no TLS) |
| `--insecure` | Skip TLS certificate verification |
| `--cacert <path>` | CA certificate file for server validation |
| `--cert <path>` | Client certificate file for mutual TLS |
| `--key <path>` | Client private key file for mutual TLS |
| `--connect-timeout <duration>` | Connection timeout (e.g., `10s`, `1m`, `500ms`) |
| `--authority <value>` | Value for `:authority` header and TLS server name |
| `--servername <value>` | Override TLS server name for certificate validation |
| `--user-agent <value>` | Custom User-Agent header value |
| `-v`, `--verbose` | Enable verbose output |
| `--vv`, `--very-verbose` | Enable very verbose output with timing details |
| `-H`, `--header <header>` | Add header to all requests (format: `name: value`) |
| `--reflect-header <header>` | Add header to reflection requests only |
| `--protoset <path>` | Use protoset file(s) instead of server reflection |
| `--protoset-out <path>` | Export FileDescriptorSet to file after operation |

---

## list Command

List available services or methods for a specific service.

### Syntax

```bash
grpcurl list [options] [address] [service]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `address` | Server address (host:port). Required unless using `--protoset`. |
| `service` | Service name to list methods for. If omitted, lists all services. |

### Examples

```bash
# List all services
grpcurl list --plaintext localhost:9090

# List methods for a service
grpcurl list --plaintext localhost:9090 my.package.Service

# List services using protoset (offline)
grpcurl list --protoset service.protoset

# List and export protoset
grpcurl list --plaintext --protoset-out export.protoset localhost:9090
```

---

## describe Command

Describe a service, method, or message type.

### Syntax

```bash
grpcurl describe [options] [address] [symbol]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `address` | Server address (host:port). Required unless using `--protoset`. |
| `symbol` | Symbol to describe (service, method, or message type). If omitted, describes all services. |

### Options

| Option | Description |
|--------|-------------|
| `--msg-template` | Output a JSON template for message types |

### Examples

```bash
# Describe all services
grpcurl describe --plaintext localhost:9090

# Describe a specific service
grpcurl describe --plaintext localhost:9090 my.package.Service

# Describe a message type
grpcurl describe --plaintext localhost:9090 my.package.MyMessage

# Get JSON template for a message
grpcurl describe --plaintext --msg-template localhost:9090 my.package.MyRequest
```

---

## invoke Command

Invoke a gRPC method.

### Syntax

```bash
grpcurl invoke [options] <address> <method>
```

### Arguments

| Argument | Description |
|----------|-------------|
| `address` | Server address (host:port). Required. |
| `method` | Method to invoke in format `Service/Method` or `package.Service/Method`. Required. |

### Options

| Option | Description |
|--------|-------------|
| `-d`, `--data <json>` | Request data as JSON. Use `@` to read from stdin. |
| `--emit-defaults` | Include default values in JSON output |
| `--allow-unknown-fields` | Allow unknown fields in JSON input |
| `--format-error` | Format error responses as JSON |
| `--max-msg-sz <size>` | Maximum message size (e.g., `4MB`, `10MB`) |
| `--max-time <duration>` | Maximum operation time / gRPC deadline |
| `--rpc-header <header>` | Add header to RPC requests only |

### Examples

```bash
# Invoke unary method with inline JSON
grpcurl invoke --plaintext \
  -d '{"name": "World"}' \
  localhost:9090 my.package.Service/SayHello

# Invoke with data from stdin
echo '{"name": "World"}' | grpcurl invoke --plaintext \
  -d @ \
  localhost:9090 my.package.Service/SayHello

# Invoke with custom headers
grpcurl invoke --plaintext \
  -H "Authorization: Bearer token123" \
  -H "X-Request-Id: abc123" \
  -d '{}' \
  localhost:9090 my.package.Service/GetData

# Invoke with timeout
grpcurl invoke --plaintext \
  --max-time 30s \
  -d '{}' \
  localhost:9090 my.package.Service/LongRunningOperation

# Invoke server streaming
grpcurl invoke --plaintext \
  -d '{"count": 5}' \
  localhost:9090 my.package.Service/StreamData

# Invoke client streaming (multiple messages from stdin)
echo '{"value": 1}
{"value": 2}
{"value": 3}' | grpcurl invoke --plaintext \
  -d @ \
  localhost:9090 my.package.Service/AccumulateValues

# Invoke bidirectional streaming
echo '{"message": "hello"}
{"message": "world"}' | grpcurl invoke --plaintext \
  -d @ \
  localhost:9090 my.package.Service/Chat
```

---

## Duration Format

Duration values accept the following formats:

| Format | Example | Description |
|--------|---------|-------------|
| Seconds | `30s` | 30 seconds |
| Milliseconds | `500ms` | 500 milliseconds |
| Minutes | `5m` | 5 minutes |
| Combined | `1m30s` | 1 minute 30 seconds |

## Size Format

Size values accept the following formats:

| Format | Example | Description |
|--------|---------|-------------|
| Bytes | `1024` | 1024 bytes |
| Kilobytes | `64KB` | 64 kilobytes |
| Megabytes | `4MB` | 4 megabytes |
| Gigabytes | `1GB` | 1 gigabyte |

---

## Exit Codes

| Code | Description |
|------|-------------|
| 0 | Success |
| 1 | General error |
| 64+ | gRPC error (64 + StatusCode) |
| 130 | Cancelled by user (Ctrl+C) |

## Environment Variables

Headers can reference environment variables using `${VAR_NAME}` syntax:

```bash
grpcurl invoke --plaintext \
  -H "Authorization: Bearer ${AUTH_TOKEN}" \
  -d '{}' \
  localhost:9090 my.package.Service/SecureMethod
```
