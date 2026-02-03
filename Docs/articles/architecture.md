# Architecture

This document describes the internal architecture of GrpCurl.Net and how its components work together.

## Overview

GrpCurl.Net is organized into several components:

```
GrpCurl.Net
├── Commands/           # CLI command handlers
│   ├── ListCommand     # List services/methods
│   ├── DescribeCommand # Describe symbols
│   └── InvokeCommand   # Invoke methods
├── DescriptorSources/  # Schema discovery
│   ├── IDescriptorSource
│   ├── ReflectionSource
│   └── ProtosetSource
├── Invocation/         # Dynamic method invocation
│   └── DynamicInvoker
├── Utilities/          # Shared utilities
│   ├── GrpcChannelFactory
│   └── ProtosetExporter
└── Program.cs          # Entry point
```

## Descriptor Sources

The descriptor source abstraction allows GrpCurl.Net to discover protobuf schemas from different sources.

This `IDescriptorSource` interface provides:
- Access to the underlying protobuf descriptors for export
- Enumeration all available services
- Look up of a specific symbol (service, method, message, etc.)

### ReflectionSource

[ReflectionSource](xref:GrpCurl.Net.DescriptorSources.ReflectionSource) implements `IDescriptorSource` by querying a running gRPC server using the [Server Reflection Protocol](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md).

Key features:
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

### ProtosetSource

[ProtosetSource](xref:GrpCurl.Net.DescriptorSources.ProtosetSource) implements `IDescriptorSource` by loading pre-compiled FileDescriptorSet files (protoset files).

Protoset files are created using `protoc`:
```bash
protoc --descriptor_set_out=service.protoset --include_imports service.proto
```

Key features:
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

### How It Works

1. **Method Resolution** - Uses `MethodDescriptor` from descriptor source to understand the method signature
2. **Message Creation** - Creates `IMessage` instances dynamically from JSON using `SimpleDynamicMessage`
3. **Method Creation** - Builds a `Method<IMessage, IMessage>` with appropriate serializers
4. **Invocation** - Uses `Grpc.Net.Client`'s `CallInvoker` to make the actual gRPC call

### Supported Method Types

```csharp
// Unary
Task<IMessage> InvokeUnaryAsync(MethodDescriptor, IMessage request, ...)

// Server streaming
IAsyncEnumerable<IMessage> InvokeServerStreamingAsync(MethodDescriptor, IMessage request, ...)

// Client streaming
Task<IMessage> InvokeClientStreamingAsync(MethodDescriptor, IAsyncEnumerable<IMessage> requests, ...)

// Bidirectional streaming
IAsyncEnumerable<IMessage> InvokeDuplexStreamingAsync(MethodDescriptor, IAsyncEnumerable<IMessage> requests, ...)
```

### SimpleDynamicMessage

An internal class that implements `IMessage` for dynamic protobuf messages. It:
- Parses JSON to protobuf binary format
- Serializes protobuf binary to JSON
- Handles all protobuf field types including:
  - Scalars (int32, string, bool, etc.)
  - Nested messages
  - Repeated fields
  - Map fields
  - Oneof fields
  - Well-known types (Timestamp, Duration, wrappers)

## Channel Configuration

`GrpcChannelFactory` handles the complexity of creating properly configured gRPC channels.

### TLS Configuration

The factory supports multiple TLS scenarios:

1. **Plaintext** - HTTP/2 without TLS
2. **Default TLS** - System CA store
3. **Custom CA** - Specify CA certificate file
4. **mTLS** - Client certificate and key
5. **Insecure** - Skip certificate verification

### Metadata Creation

Headers are processed with environment variable expansion:

```csharp
var metadata = GrpcChannelFactory.CreateMetadata(
    headers: new[] { "Authorization: Bearer ${TOKEN}" },
    userAgent: "grpcurl-dotnet/1.0.0"
);
```

## Command Structure

Commands are implemented using [System.CommandLine](https://github.com/dotnet/command-line-api):

### Command Handlers

Each command (list, describe, invoke) has a corresponding handler class:
- `ListCommandHandler` - Handles `list` command
- `DescribeCommandHandler` - Handles `describe` command
- `InvokeCommandHandler` - Handles `invoke` command

### Common Pattern

```csharp
internal static class CommandHandler
{
    public static Command Create()
    {
        // Define arguments and options
        var arg = new Argument<string>("name");
        var opt = new Option<bool>("--flag");

        // Create command
        var command = new Command("name", "description") { arg, opt };

        // Set handler
        command.SetAction(async (parseResult, _) =>
        {
            var value = parseResult.GetValue(arg);
            await ExecuteAsync(value, ...);
        });

        return command;
    }
}
```

## Output Formatting

GrpCurl.Net uses [Spectre.Console](https://spectreconsole.net/) for rich terminal output:

- **Tables** - Service and method listings
- **Panels** - Symbol descriptions
- **Colors** - Syntax highlighting and status indicators
- **Markup** - Styled messages (`[red]Error:[/]`, `[dim]verbose text[/]`)

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

```csharp
public class GrpcCommandException : Exception
{
    public int ExitCode { get; }
}
```

This allows commands to signal specific exit codes without calling `Environment.Exit()`, improving testability.

### Error Mapping

gRPC status codes are mapped to exit codes:
- Exit code = 64 + gRPC status code
- Example: `StatusCode.NotFound` (5) = exit code 69

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

The codebase includes comprehensive tests:

- **Unit Tests** - Test individual components in isolation
- **Integration Tests** - Test against a real gRPC server (`TestServer`)

Test categories:
- SimpleDynamicMessage tests - All protobuf field types
- Descriptor source tests - ReflectionSource and ProtosetSource
- Channel factory tests - TLS, metadata, environment variables
- Command tests - CLI argument parsing and execution
