# Architecture

This document describes the internal architecture of GrpCurl.Net and how its components work together.

## Overview

GrpCurl.Net is organised into several components:

### Descriptor Sources

The descriptor source abstraction allows GrpCurl.Net to discover protobuf schemas from different sources.

#### IDescriptorSource Interface

This interface provides:
- **FileDescriptorSet**: Access to the underlying protobuf descriptors for export
- **ListServicesAsync**: Enumerate all available services
- **FindSymbolAsync**: Look up a specific symbol (service, method, message, etc.)

#### ReflectionSource

[ReflectionSource](xref:GrpCurl.Net.DescriptorSources.ReflectionSource) implements `IDescriptorSource` by querying a running gRPC server using the [Server Reflection Protocol](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md).

- Uses `grpc.reflection.v1alpha.ServerReflection` service
- Caches discovered descriptors to minimize reflection calls
- Supports custom metadata for authenticated reflection requests
- Builds a complete `FileDescriptorSet` from cached descriptors

```csharp
// Create from existing channel
var source = new ReflectionSource(channel, metadata, ownsChannel: true);

// Or create with address
var source = ReflectionSource.Create("https://localhost:9090");
```

#### ProtosetSource

[ProtosetSource](xref:GrpCurl.Net.DescriptorSources.ProtosetSource) implements `IDescriptorSource` by loading pre-compiled FileDescriptorSet files (protoset files).

Protoset files are created using `protoc`:
```bash
protoc --descriptor_set_out=service.protoset --include_imports service.proto
```

- Loads one or more protoset files
- Resolves dependencies between files
- Detects circular dependencies
- Provides offline operation without server access

```csharp
// Load single file
var source = await ProtosetSource.LoadFromFileAsync("service.protoset");

// Load multiple files
var source = await ProtosetSource.LoadFromFilesAsync(new[] { "a.protoset", "b.protoset" });
```

## Dynamic Invocation

The `DynamicInvoker` class handles invoking gRPC methods at runtime without pre-compiled stubs.

#### How It Works

1. **Method Resolution**: Uses `MethodDescriptor` from descriptor source to understand the method signature
2. **Message Creation**: Creates `IMessage` instances dynamically from JSON using `SimpleDynamicMessage`
3. **Method Creation**: Builds a `Method<IMessage, IMessage>` with appropriate serialisers
4. **Invocation**: Uses `Grpc.Net.Client`'s `CallInvoker` to make the actual gRPC call

#### Supported Method Types

```csharp
// Unary: returns InvocationResult with response, headers, and trailers
Task<InvocationResult> InvokeUnaryAsync(MethodDescriptor, IMessage request, ...)

// Server streaming
IAsyncEnumerable<IMessage> InvokeServerStreamingAsync(MethodDescriptor, IMessage request, ...)

// Client streaming
Task<IMessage> InvokeClientStreamingAsync(MethodDescriptor, IAsyncEnumerable<IMessage> requests, ...)

// Bidirectional streaming
IAsyncEnumerable<IMessage> InvokeDuplexStreamingAsync(MethodDescriptor, IAsyncEnumerable<IMessage> requests, ...)
```

#### InvocationResult

The `InvocationResult` class wraps the response from unary calls with three properties:

- `Response` — the deserialised `IMessage` returned by the server.
- `ResponseHeaders` — the `Metadata` sent by the server before the response body. Useful for correlation IDs, authentication echoes, and server identification.
- `ResponseTrailers` — the `Metadata` sent after the response body (includes gRPC status details and any custom trailers the server attaches).

This enables verbose mode to display response headers and trailers alongside the response content.

#### SimpleDynamicMessage

An internal class that implements `IMessage` for dynamic protobuf messages. It:
- Parses JSON to protobuf binary format
- Serialises protobuf binary to JSON
- Handles all protobuf field types including:
  - Scalars (int32, string, bool, etc.)
  - Nested messages
  - Repeated fields
  - Map fields
  - Oneof fields
  - Well-known types (Timestamp, Duration, wrappers)

## Channel Configuration

`GrpcChannelFactory` handles the complexity of creating properly configured gRPC channels.

### Configuration Options

A number of useful configuration options are exposed:

- `Plaintext`
- `InsecureSkipVerify`
- `CaCertPath`
- `ClientCertPath`
- `ClientKeyPath`
- `ClientCertPassword`
- `ConnectTimeout`
- `KeepaliveTime`
- `MaxReceiveMessageSize`
- `MaxSendMessageSize`
- `Authority`
- `ServerName`

### TLS Configuration

The factory supports multiple TLS scenarios:

1. **Plaintext**: HTTP/2 without TLS
2. **Default TLS**: System CA store
3. **Custom CA**: Specify CA certificate file
4. **mTLS (PEM)**: Client certificate and key as separate PEM files
5. **mTLS (PKCS12)**: Client certificate as .p12/.pfx with password
6. **Insecure**: Skip certificate verification

### Metadata Creation

Headers are processed with environment variable expansion. The default `User-Agent` is resolved at runtime by `UserAgentProvider.Default`, which reads the executing assembly's informational version (driven by the `Version` property in the `.csproj`).

```csharp
var metadata = GrpcChannelFactory.CreateMetadata(
    headers: new[] { "Authorization: Bearer ${TOKEN}" },
    userAgent: null // resolves to UserAgentProvider.Default, e.g. "grpcn/1.0.0"
);
```

## Command Structure

Commands are implemented using [System.CommandLine](https://github.com/dotnet/command-line-api):

### Command Handlers

Each command (list, describe, invoke) has a corresponding static handler class:
- `ListCommandHandler`: Handles `list` command
- `DescribeCommandHandler`: Handles `describe` command
- `InvokeCommandHandler`: Handles `invoke` command

## Output Formatting

GrpCurl.Net uses plain text output for core data (service lists, proto definitions, JSON responses) matching Go grpcurl's output format when `--output text` (the default) is in effect. With `--output json`, every command emits stable, line-based JSON envelopes (single line for `list`/`describe`/single-message responses, NDJSON for streaming). [Spectre.Console](https://spectreconsole.net/) is used for:

- **Styled diagnostics on stderr**: errors, suggestions, and verbose chatter route through `Utilities.Diagnostics`, which constructs an `IAnsiConsole` bound to `Console.Error`. stdout never receives Spectre markup; agents and pipelines can parse it cleanly.
- **Timing tables**: Very verbose mode timing breakdown (also stderr).
- **Centralised error rendering**: all catch blocks build an `ErrorEnvelope` and call `ErrorRenderer.RenderAndThrow`, which switches between Spectre markup (text mode) and a single-line JSON envelope (`--output json`) without each handler caring.

## Timing Context

The `TimingContext` class tracks execution phases for very verbose mode:

```csharp
var timing = new TimingContext();

timing.StartPhase("Connection Establishment");
// ... connect ...

timing.StartPhase("Schema Discovery");
// ... discover schema ...

timing.PrintSummary(); // Outputs timing table
```

## Error Handling

### GrpcCommandException

A custom exception that carries an exit code:

This allows commands to signal specific exit codes without calling `Environment.Exit()`, improving testability. The `Silent` flag indicates the error has already been displayed (e.g., as JSON via `--output json`) and should not be printed again. When set, the optional `Envelope` property carries the structured `ErrorEnvelope` already rendered by `ErrorRenderer`.

### Error Mapping

Exit codes follow this contract (shared by `grpcn` and `gql2grpc`):

| Code | Category |
|---|---|
| `0` | Success |
| `1` | Internal (uncaught) |
| `2` | Usage (bad CLI args, JSON-parse failure on `-d`) |
| `3` | Schema/file (missing protoset, symbol not found, refusing to overwrite `--protoset-out`) |
| `4` | Network (HTTP/2 connect failure outside the RPC; rare — most transport failures arrive as `RpcException` and map to `64 + status`) |
| `5` | Timeout (connect or operation deadline exceeded outside the RPC; rare — a `--max-time` hit during connect typically maps to `64 + Cancelled(1)` = `65`) |
| `64 + statusCode` | RPC error from the server or transport (e.g. `StatusCode.NotFound` (5) → 69; connection refused → `Unavailable` (14) → 78) |
| `130` | Cancelled (Ctrl+C / SIGINT) |

Each catch block builds an `ErrorEnvelope` whose `Category` field discriminates these classes; `ErrorRenderer` consults it to produce either Spectre markup (text mode) or a JSON envelope (json mode). The `Network`/`Timeout` categories are reached only when the failure bypasses the gRPC client entirely (`HttpRequestException`/`TimeoutException`); in practice most transport failures surface as `RpcException` and therefore exit `64 + status`, matching upstream grpcurl.

## Extensibility Points

### Adding a New Descriptor Source

1. Implement `IDescriptorSource`
2. Handle `FileDescriptorSet` for protoset export
3. Cache descriptors for performance

### Adding a New Command

1. Create a new command handler class
2. Add to root command in `Program.cs`
3. Follow existing patterns for options and error handling

## Dependencies

| Package | Purpose |
|---------|---------|
| `Grpc.Net.Client` | gRPC client implementation |
| `Google.Protobuf` | Protocol buffers runtime |
| `System.CommandLine` | CLI argument parsing |
| `Spectre.Console` | Terminal output formatting |

## Testing

The codebase has three test projects, all using xUnit v3 with Shouldly assertions:

- `Tests/GrpCurl.DotNet.Tests.Unit/` — unit tests for every subsystem (descriptor sources, dynamic invocation, channel factory, exceptions, command helpers).
- `Tests/GrpCurl.DotNet.Tests.Integration/` — end-to-end tests against an in-process Kestrel gRPC server hosted by `Tests/GrpCurl.Net.TestServer/`. The `GrpcTestFixture` (shared via `[CollectionDefinition("GrpcServer")]`) binds a random port and enables reflection.
- `Tests/Gql2Grpc.Tests/` — covers the GraphQL bridge (GraphQL layer, configuration, translation, response projection, introspection, executor) plus end-to-end scenarios against the same `TestServer`.

### Running the tests

```bash
# Entire solution (.NET 10 Microsoft.Testing.Platform requires --solution / --project)
dotnet restore GrpCurl.Net.slnx --locked-mode
dotnet build GrpCurl.Net.slnx --configuration Release --no-restore /nr:false
dotnet test --solution GrpCurl.Net.slnx --configuration Release --no-build
dotnet run --project Scripts/ValidationRunner/ValidationRunner.csproj --configuration Release --no-restore -- --ci

# Single test class (MTP filter syntax — everything after `--` goes to the test host)
dotnet test --project Tests/Gql2Grpc.Tests/Gql2Grpc.Tests.csproj --configuration Release --no-build -- --filter-class "Gql2Grpc.Tests.Integration.EndToEnd.EndToEndTests"
```

Test fixtures are reused across projects via a linked `GrpcTestFixture.cs` compile item in `Tests/Gql2Grpc.Tests/Fixtures/` — one fixture definition, consumed by both assemblies.

## Gql2Grpc subsystems

`Gql2Grpc` is a second CLI in the same solution (`Src/Gql2Grpc/`) that translates GraphQL operations to gRPC method invocations. It is a thin layer on top of `GrpCurl.Net`: it consumes the existing descriptor sources, channel factory, and dynamic invoker in-process via a `ProjectReference` plus `InternalsVisibleTo` — no subprocess spawn. Everything transport-related (TLS, mTLS, headers, deadlines, message size, authority, user-agent) flows through the same `GrpcChannelFactory` described above, so behaviour is identical between `invoke` and `gql2grpc`.

### Module layout

The Gql2Grpc source tree is organised by pipeline stage. Each directory has one responsibility and no upward references:

| Directory | Responsibility |
|---|---|
| `Commands/` | `QueryCommandHandler` builds the System.CommandLine root command and wires every CLI option into the executor. |
| `GraphQL/` | Parse + resolve a GraphQL document. `GraphQLDocumentParser` produces a `GraphQLDocument` (operations + fragments) via [GraphQL-Parser](https://github.com/graphql-dotnet/parser). `VariableCoercer` applies variable defaults, CLI `--var` scalars, and `--variables-file` overrides (coercing to the declared type). `SelectionResolver` walks the selected `GraphQLOperation`, inlines fragment spreads and inline fragments, evaluates `@include`/`@skip` directives, preserves aliases, and yields `ResolvedSelection` trees. Downstream stages only see resolved selections — no AST leakage. |
| `Configuration/` | `MappingConfig` record graph plus `MappingConfigLoader` (YAML via [YamlDotNet](https://github.com/aaubry/YamlDotNet), JSON via `System.Text.Json`). `MappingResolver` applies precedence (CLI `--default-service` > `defaults.service`, explicit entry > convention fallback). `ConventionDefaults` holds the Relay argument aliases and PascalCase/snake_case helpers. |
| `Translation/` | `JsonRequestTranslator` consumes a `ResolvedSelection` and a `MappingEntry` to produce the request JSON accepted by `DynamicInvoker.CreateMessageFromJson`. Argument rules — rename, nested path, spread, literal, skip — are applied here. `FieldMaskProjector` converts a resolved selection tree into a `google.protobuf.FieldMask` path list (snake_case, dotted) for the `$selection.fieldMask: <target>` mapping-entry rule. |
| `Execution/` | `DescriptorSourceFactory` creates a single `GrpcChannel` per invocation and decides reflection vs protoset based on `--protoset` presence; `GrpcTransport` adapts `DynamicInvoker` to JSON strings so the rest of the pipeline never touches `IMessage` directly. `OperationExecutor` orchestrates unary and server-streaming root fields — including the introspection short-circuit for `__schema`/`__type`/`__typename`. `ParallelFieldScheduler` executes root fields concurrently with a bounded degree of parallelism while preserving document order in the output. |
| `Response/` | `SelectionProjector` prunes the gRPC JSON response to match the resolved selection, honouring aliases and `response.unwrap` hints. `GraphQLResponseBuilder` assembles the spec-compliant envelope (`data`, `errors[]` with array paths and `extensions`). `StreamingResponseWriter` emits NDJSON — one envelope per line — for subscriptions and any other server-streaming operation. |
| `Introspection/` | `GraphQLSchemaBuilder` synthesises a `__Schema` from the `IDescriptorSource` plus the mapping config (proto messages → GraphQL object types, enums → enum types, `oneof` → unions, well-known types mapped via `TypeMappings`). `IntrospectionExecutor` intercepts selections whose name begins with `__` and answers them entirely from the synthesised schema — no RPC is made. |
| `Diagnostics/` | `VerboseLogger` writes `-v`/`--vv` diagnostics to stderr via Spectre.Console with proper markup escaping for user content. `ExceptionTranslator` maps `RpcException`, `JsonException`, `FileNotFoundException`, and friends into `GraphQLError` records (extensions are always populated — `code` for category and `grpcStatus`/`grpcStatusCode` for upstream RPC failures). `ExitCodeFor` returns `2` for usage/JSON, `3` for schema/file, `4` for network, `5` for timeout, `64 + statusCode` for RPC, `130` for cancellation. The previous `--format-error` toggle has been removed; structured extensions are unconditional. |

### Dataflow

```
CLI args ──▶ QueryCommandHandler ──▶ GraphQLDocumentParser ──▶ VariableCoercer ──▶ SelectionResolver
                                                                                          │
                                                                                          ▼
                                                                          IntrospectionExecutor (if __schema/__type)
                                                                                          │
                                                                                          ▼
                                                    MappingResolver ──▶ JsonRequestTranslator ──▶ GrpcTransport
                                                                                                        │
                                                                  DescriptorSourceFactory ◀────── (shared channel)
                                                                                                        │
                                                                                                        ▼
                                                                    SelectionProjector ◀── gRPC JSON response
                                                                                                        │
                                                                                                        ▼
                                                                                     GraphQLResponseBuilder
                                                                                                        │
                                                                                                        ▼
                                                              stdout envelope  or  StreamingResponseWriter (NDJSON)
```

Parallel execution happens at the root-field level inside `OperationExecutor` via `ParallelFieldScheduler`: the schedule is bounded (default parallelism = min(4, root-field-count)) and results are re-ordered into document order before the envelope is serialised. Subscriptions bypass the scheduler — a streaming operation always has exactly one root field.

### Extensibility notes

- To add a new argument-rule shape (e.g. a per-entry JSON transform), extend the `ArgumentRule` discriminated union in `Configuration/MappingConfig.cs` and handle the new case in `JsonRequestTranslator.ApplyCallerArgument`.
- To expose a new well-known type in introspection, add it to the dictionary in `Introspection/TypeMappings.cs`.
- To support proto-annotation-based mapping discovery (deferred per the [future-work backlog](gql2grpc-future-work.md)), read custom options via `FieldDescriptor.GetExtension` and merge them into `MappingConfig` with lowest precedence.
