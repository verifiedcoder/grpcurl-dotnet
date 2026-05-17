using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Response wrapper that includes the message and gRPC metadata (headers/trailers).
/// </summary>
internal sealed class InvocationResult
{
    public required IMessage Response { get; init; }
    
    public Metadata? ResponseHeaders { get; init; }
    
    public Metadata? ResponseTrailers { get; init; }
}

/// <summary>
///     Handles dynamic invocation of gRPC methods without pre-compiled stubs.
/// </summary>
internal sealed class DynamicInvoker(GrpcChannel channel)
{
    private readonly GrpcChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>
    ///     Invokes a unary RPC method.
    /// </summary>
    public async Task<InvocationResult> InvokeUnaryAsync(
        MethodDescriptor methodDescriptor,
        IMessage request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        // Check cancellation before starting the call to throw OperationCanceledException
        // instead of RpcException for pre-canceled tokens (more idiomatic .NET behavior)
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.Unary);
        var callInvoker = _channel.CreateCallInvoker();

        var callOptions = new CallOptions(headers, deadline, cancellationToken);

        var call = callInvoker.AsyncUnaryCall(
            method,
            null,
            callOptions,
            request);

        var response = await call.ResponseAsync;
        var responseHeaders = await call.ResponseHeadersAsync;
        
        Metadata? responseTrailers = null;

        try
        {
            responseTrailers = call.GetTrailers();
        }
        catch
        {
            // Trailers may not be available
        }

        return new InvocationResult
        {
            Response = response,
            ResponseHeaders = responseHeaders,
            ResponseTrailers = responseTrailers
        };
    }

    /// <summary>
    ///     Invokes a server-streaming RPC method.
    /// </summary>
    public async IAsyncEnumerable<IMessage> InvokeServerStreamingAsync(
        MethodDescriptor methodDescriptor,
        IMessage request,
        Metadata? headers = null,
        DateTime? deadline = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Check cancellation before starting the call
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.ServerStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncServerStreamingCall(
            method,
            null,
            callOptions,
            request);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <summary>
    ///     Server-streaming variant that surfaces response headers and trailers alongside
    ///     the stream so verbose CLI output can render them uniformly with the unary path.
    ///     Caller must <see cref="StreamingInvocationResult.Dispose"/> when finished.
    /// </summary>
    public StreamingInvocationResult InvokeServerStreamingWithMetadataAsync(
        MethodDescriptor methodDescriptor,
        IMessage request,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.ServerStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncServerStreamingCall(method, null, callOptions, request);

        return new StreamingInvocationResult(
            call.ResponseHeadersAsync,
            call.ResponseStream.ReadAllAsync(cancellationToken),
            call.GetTrailers,
            call.Dispose);
    }

    /// <summary>
    ///     Invokes a client-streaming RPC method.
    /// </summary>
    public async Task<IMessage> InvokeClientStreamingAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeClientStreamingWithMetadataAsync(methodDescriptor, requests, headers, deadline, cancellationToken).ConfigureAwait(false);

        try
        {
            return result.Response;
        }
        finally
        {
            result.Dispose();
        }
    }

    /// <summary>
    ///     Client-streaming variant that returns the response along with headers and trailers
    ///     so verbose CLI output can render them uniformly with the unary path.
    /// </summary>
    public async Task<ClientStreamingInvocationResult> InvokeClientStreamingWithMetadataAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.ClientStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncClientStreamingCall(method, null, callOptions);

        await foreach (var request in requests.WithCancellation(cancellationToken))
        {
            await call.RequestStream.WriteAsync(request, cancellationToken);
        }

        await call.RequestStream.CompleteAsync();

        var response = await call.ResponseAsync;

        return new ClientStreamingInvocationResult(
            call.ResponseHeadersAsync,
            response,
            call.GetTrailers,
            call.Dispose);
    }

    /// <summary>
    ///     Bidi-streaming variant exposing response headers and trailers alongside the
    ///     downstream message enumerable. Internally manages the write task identically
    ///     to <see cref="InvokeDuplexStreamingAsync"/>; caller must dispose the result.
    /// </summary>
    public StreamingInvocationResult InvokeDuplexStreamingWithMetadataAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.DuplexStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncDuplexStreamingCall(method, null, callOptions);

        var requestStream = call.RequestStream;

        var writeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await requestStream.WriteAsync(msg, cancellationToken).ConfigureAwait(false);
                }

                await requestStream.CompleteAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Caller cancelled — let the read side see the cancellation too.
            }
        }, cancellationToken);

        async IAsyncEnumerable<IMessage> ReadAll()
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return response;
            }

            await writeTask.ConfigureAwait(false);
        }

        return new StreamingInvocationResult(
            call.ResponseHeadersAsync,
            ReadAll(),
            call.GetTrailers,
            call.Dispose);
    }

    /// <summary>
    ///     Invokes a bidirectional-streaming RPC method.
    /// </summary>
    public async IAsyncEnumerable<IMessage> InvokeDuplexStreamingAsync(
        MethodDescriptor methodDescriptor,
        IAsyncEnumerable<IMessage> requests,
        Metadata? headers = null,
        DateTime? deadline = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Check cancellation before starting the call
        cancellationToken.ThrowIfCancellationRequested();

        var method = CreateMethod<IMessage, IMessage>(methodDescriptor, MethodType.DuplexStreaming);
        var callInvoker = _channel.CreateCallInvoker();
        var callOptions = new CallOptions(headers, deadline, cancellationToken);
        var call = callInvoker.AsyncDuplexStreamingCall(
            method,
            null,
            callOptions);

        // Create a linked token source for the write task so we can cancel it independently
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        var writeToken = writeCts.Token;
        
        Exception? readException = null;

        // Capture the streams so the write task lambda does not capture 'call' itself.
        // This avoids an AccessToDisposedClosure issue: 'call' is disposed in the finally
        // block below, and the write task must not hold a reference to it.
        var requestStream = call.RequestStream;

        // Start writing requests in background
        var writeTask = Task.Run(async () =>
        {
            var sentCount = 0;

            try
            {
                await foreach (var request in requests.WithCancellation(writeToken))
                {
                    await requestStream.WriteAsync(request, writeToken);
                    sentCount++;
                }

                await requestStream.CompleteAsync();
            }
            catch (OperationCanceledException) when (writeToken.IsCancellationRequested)
            {
                // Write was cancelled - this is expected if response stream ended early or user cancelled
                // Complete the stream if we can, ignore errors
                try
                {
                    await requestStream.CompleteAsync();
                }
                catch
                {
                    // Ignore - stream may already be closed
                }
            }
            catch (RpcException ex)
            {
                // Mid-stream RPC error - provide context about partial results
                throw new RpcException(new Status(ex.StatusCode, $"Error after sending {sentCount} message(s): {ex.Status.Detail}"), ex.Trailers);
            }
            catch (IOException ex)
            {
                // Connection drop during write
                throw new IOException($"Connection lost after sending {sentCount} message(s)", ex);
            }
        }, writeToken);

        // Read responses
        try
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                yield return response;
            }
        }
        finally
        {
            // Cancel the write task if it's still running
            await writeCts.CancelAsync();

            // Await the write task to completion before disposing the call.
            // The cancellation above ensures the task terminates promptly.
            // We must NOT dispose 'call' while the write task is still running,
            // as the task holds a reference to the request stream.
            try
            {
                await writeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected - write was cancelled
            }
            catch (Exception ex)
            {
                // Write task failed - propagate if no read exception occurred
                readException ??= ex;
            }

            // Safe to dispose now - write task has fully completed
            call.Dispose();
        }

        // Propagate any write exception that occurred
        if (readException is not null)
        {
            throw readException;
        }
    }

    private static Method<TRequest, TResponse> CreateMethod<TRequest, TResponse>(
        MethodDescriptor methodDescriptor,
        MethodType methodType)
        where TRequest : class
        where TResponse : class
        => new(methodType,
               methodDescriptor.Service.FullName,
               methodDescriptor.Name,
               CreateMarshaller<TRequest>(methodDescriptor.InputType),
               CreateMarshaller<TResponse>(methodDescriptor.OutputType));

    private static Marshaller<T> CreateMarshaller<T>(MessageDescriptor messageDescriptor)
        where T : class
    {
        return new Marshaller<T>(
            message =>
            {
                if (message is IMessage protoMessage)
                {
                    return protoMessage.ToByteArray();
                }

                throw new ArgumentException($"Message must be an IMessage, got {message.GetType()}");
            },
            bytes =>
            {
                // Create a SimpleDynamicMessage and parse the bytes
                var dynamicMessage = new SimpleDynamicMessage(messageDescriptor);

                using (var input = new CodedInputStream(bytes))
                {
                    dynamicMessage.MergeFrom(input);
                }

                return (T)(object)dynamicMessage;
            });
    }

    /// <summary>
    ///     Creates a request message from JSON input.
    /// </summary>
    public static IMessage CreateMessageFromJson(MessageDescriptor messageDescriptor, string? json, bool allowUnknownFields = true)
    {
        // .NET Google.Protobuf doesn't natively support dynamic message creation
        // As a workaround, we'll create a simple dynamic message implementation
        return new SimpleDynamicMessage(messageDescriptor, json, allowUnknownFields);
    }

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     Converts a message to JSON. Defaults to pretty-printed (matching Go grpcurl);
    ///     pass <paramref name="indent"/> = <c>false</c> for one-line compact output
    ///     suitable for NDJSON streaming.
    /// </summary>
    public static string MessageToJson(IMessage message, bool includeDefaults = false, bool indent = true)
    {
        string compactJson;

        // Handle SimpleDynamicMessage specially
        if (message is SimpleDynamicMessage dynamicMessage)
        {
            compactJson = dynamicMessage.ToJson(includeDefaults);
        }
        else
        {
            // For regular messages, use built-in formatter
            var formatter = new JsonFormatter(new JsonFormatter.Settings(includeDefaults));

            compactJson = formatter.Format(message);
        }

        using var doc = JsonDocument.Parse(compactJson);

        return JsonSerializer.Serialize(doc.RootElement, indent ? IndentedJsonOptions : CompactJsonOptions);
    }
}

/// <summary>
///     Extension methods for async streams.
/// </summary>
public static class AsyncStreamExtensions
{
    /// <summary>
    ///     Reads all items from an async stream reader as an async enumerable.
    /// </summary>
    /// <typeparam name="T">The type of items in the stream.</typeparam>
    /// <param name="stream">The async stream reader to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of all items from the stream.</returns>
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this IAsyncStreamReader<T> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            yield return stream.Current;
        }
    }
}

/// <summary>
///     Simple dynamic message implementation for runtime message creation
/// </summary>
internal class SimpleDynamicMessage : IMessage
{
    internal readonly Dictionary<FieldDescriptor, object?> Fields = [];
    internal readonly Dictionary<FieldDescriptor, Dictionary<object, object?>> MapFields = [];

    // Track which field is set in each oneof (OneofDescriptor -> active FieldDescriptor)
    internal readonly Dictionary<OneofDescriptor, FieldDescriptor?> OneofFields = [];
    internal readonly Dictionary<FieldDescriptor, List<object?>> RepeatedFields = [];

    // List to track unknown fields encountered during JSON parsing
    private readonly List<string> _unknownFields = [];

    // Constructor for creating empty message (for deserialization)
    public SimpleDynamicMessage(MessageDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    // Constructor for creating message from JSON (for request serialization)
    public SimpleDynamicMessage(MessageDescriptor descriptor, string? json, bool allowUnknownFields = true)
    {
        Descriptor = descriptor;

        // Parse JSON and populate fields
        if (json is null)
        {
            return;
        }

        using var jsonDoc = JsonDocument.Parse(json);

        foreach (var property in jsonDoc.RootElement.EnumerateObject())
        {
            var field = descriptor.Fields.InDeclarationOrder().FirstOrDefault(f =>
                f.JsonName.Equals(property.Name, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));

            if (field is null)
            {
                // Track unknown field
                _unknownFields.Add(property.Name);

                if (!allowUnknownFields)
                {
                    throw new ArgumentException($"Unknown field '{property.Name}' in message type '{descriptor.FullName}'. Use --allow-unknown-fields to skip unknown fields.");
                }

                continue;
            }

            if (field.IsMap && property.Value.ValueKind == JsonValueKind.Object)
            {
                // Handle map field (JSON object)
                MapFields[field] = [];

                var mapDescriptor = field.MessageType;
                var keyField = mapDescriptor.FindFieldByNumber(1);
                var valueField = mapDescriptor.FindFieldByNumber(2);

                foreach (var mapEntry in property.Value.EnumerateObject())
                {
                    // Convert the key based on the key field type
                    var key = ConvertMapKey(mapEntry.Name, keyField);

                    // Convert the value based on the value field type
                    var valueElement = mapEntry.Value;
                    var value = ConvertJsonValue(valueElement, valueField);

                    MapFields[field][key] = value;
                }
            }
            else if (field.IsRepeated && property.Value.ValueKind == JsonValueKind.Array)
            {
                // Handle repeated field (array)
                RepeatedFields[field] = [];

                foreach (var element in property.Value.EnumerateArray())
                {
                    // Protocol Buffers do not support null elements in repeated fields
                    if (element.ValueKind == JsonValueKind.Null)
                    {
                        throw new ArgumentException(
                            $"Null values are not allowed in repeated field '{field.Name}'. " +
                            "Protocol Buffers do not support null in repeated fields.");
                    }

                    var value = ConvertJsonValue(element, field);

                    RepeatedFields[field].Add(value);
                }
            }
            else
            {
                // Handle regular field (and oneof fields)
                var value = ConvertJsonValue(property.Value, field);

                // If this field is part of oneof, clear other fields in the same oneof
                if (field.ContainingOneof is { IsSynthetic: false })
                {
                    var oneof = field.ContainingOneof;

                    // Clear any other field in this oneof
                    oneof.Fields
                        .Where(f => f != field)
                        .ToList()
                        .ForEach(f => Fields.Remove(f));

                    // Track which field is active in this oneof
                    OneofFields[oneof] = field;
                }

                Fields[field] = value;
            }
        }
    }

    /// <summary>
    ///     Gets the list of unknown fields encountered during JSON parsing.
    /// </summary>
    public IReadOnlyList<string> UnknownFields
        => _unknownFields.AsReadOnly();

    public MessageDescriptor Descriptor { get; }

    public void WriteTo(CodedOutputStream output)
        => ProtobufWriter.WriteTo(this, output);

    public int CalculateSize()
        => ProtobufWriter.CalculateSize(this);

    public void MergeFrom(CodedInputStream input)
        => ProtobufReader.MergeFrom(this, input);

    private object? ConvertJsonValue(JsonElement element, FieldDescriptor field)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            // Only message types can be null in protobuf; scalars use defaults
            return field.FieldType == FieldType.Message ? null : GetDefaultValue(field);
        }

        return field.FieldType switch
        {
            FieldType.String => element.GetString(),
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => element.GetInt32(),
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 =>
                element.ValueKind == JsonValueKind.String
                    ? long.Parse(element.GetString()!)
                    : element.GetInt64(),
            FieldType.UInt32 or FieldType.Fixed32 => element.GetUInt32(),
            FieldType.UInt64 or FieldType.Fixed64 =>
                element.ValueKind == JsonValueKind.String
                    ? ulong.Parse(element.GetString()!)
                    : element.GetUInt64(),
            FieldType.Bool => element.GetBoolean(),
            FieldType.Float => element.ValueKind == JsonValueKind.String
                ? element.GetString() switch
                {
                    "NaN" => float.NaN,
                    "Infinity" => float.PositiveInfinity,
                    "-Infinity" => float.NegativeInfinity,
                    var s => throw new ArgumentException($"Invalid float value: {s}")
                }
                : (float)element.GetDouble(),
            FieldType.Double => element.ValueKind == JsonValueKind.String
                ? element.GetString() switch
                {
                    "NaN" => double.NaN,
                    "Infinity" => double.PositiveInfinity,
                    "-Infinity" => double.NegativeInfinity,
                    var s => throw new ArgumentException($"Invalid double value: {s}")
                }
                : element.GetDouble(),
            FieldType.Bytes => ByteString.CopyFrom(Convert.FromBase64String(element.GetString() ?? "")),
            FieldType.Enum => ConvertEnum(element, field),
            FieldType.Message => ConvertNestedMessage(element, field),
            _ => null
        };
    }

    private static int ConvertEnum(JsonElement element, FieldDescriptor field)
    {
        var enumType = field.EnumType;

        switch (element.ValueKind)
        {
            // Handle string values (enum names)
            case JsonValueKind.String:
                {
                    var enumName = element.GetString();

                    if (string.IsNullOrEmpty(enumName))
                    {
                        return 0; // Default enum value
                    }

                    // Try to find the enum value by name
                    var enumValue = enumType.Values.FirstOrDefault(v => v.Name == enumName);

                    return enumValue?.Number ?? throw new ArgumentException($"Unknown enum value '{enumName}' for enum type '{enumType.FullName}'");
                }
            // Handle numeric values (for backward compatibility)
            case JsonValueKind.Number:

                return element.GetInt32();

            case JsonValueKind.Undefined:
            case JsonValueKind.Object:
            case JsonValueKind.Array:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:

                throw new ArgumentException($"Invalid value for enum field '{field.Name}'. Expected string or number, got {element.ValueKind}");
        }
    }

    private SimpleDynamicMessage? ConvertNestedMessage(JsonElement element, FieldDescriptor field)
    {
        var messageType = field.MessageType;
        var fullName = messageType.FullName;

        // Handle well-known types with special JSON encoding
        switch (fullName)
        {
            case "google.protobuf.Timestamp":

                return WellKnownTypeHandler.ConvertTimestamp(element, messageType);

            case "google.protobuf.Duration":

                return WellKnownTypeHandler.ConvertDuration(element, messageType);

            case "google.protobuf.StringValue":
            case "google.protobuf.Int32Value":
            case "google.protobuf.Int64Value":
            case "google.protobuf.UInt32Value":
            case "google.protobuf.UInt64Value":
            case "google.protobuf.FloatValue":
            case "google.protobuf.DoubleValue":
            case "google.protobuf.BoolValue":
            case "google.protobuf.BytesValue":

                return WellKnownTypeHandler.ConvertWrapperType(element, messageType, ConvertJsonValue);

            case "google.protobuf.Any":

                return WellKnownTypeHandler.ConvertAny(element, messageType);

            case "google.protobuf.Empty":

                return WellKnownTypeHandler.ConvertEmpty(messageType);

            case "google.protobuf.FieldMask":

                return WellKnownTypeHandler.ConvertFieldMask(element, messageType);

            case "google.protobuf.Struct":

                return WellKnownTypeHandler.ConvertStruct(element, messageType, ConvertValue);

            case "google.protobuf.Value":

                return WellKnownTypeHandler.ConvertValue(element, messageType, ConvertStruct, ConvertListValue);

            case "google.protobuf.ListValue":

                return WellKnownTypeHandler.ConvertListValue(element, messageType, ConvertValue);
        }

        // Regular message - must be JSON object
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Recursively create a SimpleDynamicMessage for the nested message
        var nestedMessage = new SimpleDynamicMessage(messageType);

        // Parse nested fields
        foreach (var property in element.EnumerateObject())
        {
            var nestedField = messageType.Fields.InDeclarationOrder().FirstOrDefault(f =>
                f.JsonName.Equals(property.Name, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));

            if (nestedField is not null)
            {
                nestedMessage.Fields[nestedField] = ConvertJsonValue(property.Value, nestedField);
            }
        }

        return nestedMessage;
    }

    // Adapter methods for WellKnownTypeHandler callbacks
    private SimpleDynamicMessage ConvertValue(JsonElement element, MessageDescriptor messageType)
        => WellKnownTypeHandler.ConvertValue(element, messageType, ConvertStruct, ConvertListValue);

    private SimpleDynamicMessage? ConvertStruct(JsonElement element, MessageDescriptor messageType)
        => WellKnownTypeHandler.ConvertStruct(element, messageType, ConvertValue);

    private SimpleDynamicMessage? ConvertListValue(JsonElement element, MessageDescriptor messageType)
        => WellKnownTypeHandler.ConvertListValue(element, messageType, ConvertValue);

    private static object ConvertMapKey(string keyString, FieldDescriptor keyField)
    {
        // Map keys are always strings in JSON, but need to be converted to the correct type
        return keyField.FieldType switch
        {
            FieldType.String => keyString,
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => int.Parse(keyString),
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => long.Parse(keyString),
            FieldType.UInt32 or FieldType.Fixed32 => uint.Parse(keyString),
            FieldType.UInt64 or FieldType.Fixed64 => ulong.Parse(keyString),
            FieldType.Bool => bool.Parse(keyString),
            _ => keyString
        };
    }

    /// <summary>
    ///     Returns the default value for a scalar field type.
    ///     Protobuf scalars don't have null semantics - they default to zero/empty values.
    /// </summary>
    private static object GetDefaultValue(FieldDescriptor field)
        => field.FieldType switch
        {
            FieldType.String => "",
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => 0,
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => 0L,
            FieldType.UInt32 or FieldType.Fixed32 => 0u,
            FieldType.UInt64 or FieldType.Fixed64 => 0UL,
            FieldType.Bool => false,
            FieldType.Float => 0f,
            FieldType.Double => 0d,
            FieldType.Bytes => ByteString.Empty,
            FieldType.Enum => 0,
            _ => throw new ArgumentException($"No default value for field type: {field.FieldType}")
        };

    /// <summary>
    ///     Converts this dynamic message to JSON string.
    /// </summary>
    /// <param name="includeDefaults">
    ///     When true, includes fields with default/null values in output.
    ///     When false (default), omits fields with null or default values.
    /// </param>
    /// <returns>JSON representation of the message.</returns>
    /// <remarks>
    ///     <para>
    ///         Proto3 semantics: In proto3, there is no distinction between a field that was never
    ///         set and a field explicitly set to its default value. Both null message fields and
    ///         unset fields are omitted from the JSON output when includeDefaults is false.
    ///     </para>
    ///     <para>
    ///         This matches the canonical proto3 JSON encoding behavior where default values are
    ///         not emitted. Use includeDefaults=true (--emit-defaults CLI flag) to see all fields.
    ///     </para>
    /// </remarks>
    public string ToJson(bool includeDefaults = false)
    {
        var sb = new StringBuilder().Append('{');

        var first = true;

        // Write non-repeated fields
        foreach (var (field, value) in Fields)
        {
            // Skip null values unless includeDefaults is true
            // Note: Proto3 does not distinguish between "unset" and "default value" for scalars
            if (value is null && !includeDefaults)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;

            // Write field name (use proto field name for snake_case, matching Go grpcurl)
            sb.Append('"');
            sb.Append(field.Name);
            sb.Append("\":");

            // Write field value
            WriteJsonValue(sb, field, value, includeDefaults);
        }

        // Write repeated fields as arrays
        foreach (var (field, values) in RepeatedFields)
        {
            if (values.Count == 0 && !includeDefaults)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;

            // Write field name (use proto field name for snake_case, matching Go grpcurl)
            sb.Append('"');
            sb.Append(field.Name);
            sb.Append("\":[");

            // Write array elements
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                WriteJsonValue(sb, field, values[i], includeDefaults);
            }

            sb.Append(']');
        }

        // Write map fields as JSON objects
        foreach (var (field, map) in MapFields)
        {
            if (map.Count == 0 && !includeDefaults)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;

            // Write field name (use proto field name for snake_case, matching Go grpcurl)
            sb.Append('"');
            sb.Append(field.Name);
            sb.Append("\":{");

            // Get value field descriptor (key field not needed as FormatMapKey handles all valid key types)
            var mapDescriptor = field.MessageType;
            var valueField = mapDescriptor.FindFieldByNumber(2);

            var firstEntry = true;

            foreach (var (key, value) in map)
            {
                if (!firstEntry)
                {
                    sb.Append(',');
                }

                firstEntry = false;

                // Write key as string (JSON object keys are always strings)
                sb.Append('"');
                sb.Append(FormatMapKey(key));
                sb.Append("\":");

                // Write value
                if (valueField is not null)
                {
                    WriteJsonValue(sb, valueField, value, includeDefaults);
                }
            }

            sb.Append('}');
        }

        // Emit default values for fields not already written
        if (includeDefaults)
        {
            foreach (var field in Descriptor.Fields.InDeclarationOrder())
            {
                // Skip if already written from populated dictionaries
                if (Fields.ContainsKey(field) || RepeatedFields.ContainsKey(field) || MapFields.ContainsKey(field))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;

                sb.Append('"');
                sb.Append(field.Name);
                sb.Append("\":");

                if (field.IsMap)
                {
                    sb.Append("{}");
                }
                else if (field.IsRepeated)
                {
                    sb.Append("[]");
                }
                else
                {
                    WriteDefaultValue(sb, field);
                }
            }
        }

        sb.Append('}');

        return sb.ToString();
    }

    private static void WriteDefaultValue(StringBuilder sb, FieldDescriptor field)
    {
        switch (field.FieldType)
        {
            case FieldType.String:
                
                sb.Append("\"\"");
                
                break;

            case FieldType.Bool:
                
                sb.Append("false");
                
                break;

            case FieldType.Int32:
            case FieldType.SInt32:
            case FieldType.SFixed32:
            case FieldType.UInt32:
            case FieldType.Fixed32:
            case FieldType.Float:
            case FieldType.Double:
                
                sb.Append('0');
                
                break;

            case FieldType.Int64:
            case FieldType.SInt64:
            case FieldType.SFixed64:

            case FieldType.UInt64:
            case FieldType.Fixed64:
                
                sb.Append("\"0\"");
                
                break;

            case FieldType.Bytes:
                
                sb.Append("\"\"");
                
                break;

            case FieldType.Enum:
                
                var enumValue = field.EnumType.Values.FirstOrDefault();
                
                if (enumValue is not null)
                {
                    sb.Append('"');
                    sb.Append(enumValue.Name);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('0');
                }
                
                break;

            // ReSharper disable once DuplicatedSwitchSectionBodies for clarity
            case FieldType.Message:
            case FieldType.Group:

                sb.Append("null");

                break;

            default:

                sb.Append("null");

                break;
        }
    }

    private static string FormatMapKey(object key) // Convert the key to a string for JSON
        => key.ToString() ?? "";

    private void WriteJsonValue(StringBuilder sb, FieldDescriptor field, object? value, bool includeDefaults = false)
    {
        if (value is null)
        {
            sb.Append("null");

            return;
        }

        switch (field.FieldType)
        {
            case FieldType.String:

                sb.Append('"');
                sb.Append(JsonEncodedText.Encode((string)value).ToString());
                sb.Append('"');

                break;

            case FieldType.Int32:
            case FieldType.SInt32:
            case FieldType.SFixed32:

                sb.Append((int)value);

                break;

            case FieldType.Int64:
            case FieldType.SInt64:
            case FieldType.SFixed64:

                sb.Append('"');
                sb.Append((long)value);
                sb.Append('"'); // JSON uses strings for int64

                break;

            case FieldType.UInt32:
            case FieldType.Fixed32:

                sb.Append((uint)value);

                break;

            case FieldType.UInt64:
            case FieldType.Fixed64:

                sb.Append('"');
                sb.Append((ulong)value);
                sb.Append('"'); // JSON uses strings for uint64

                break;

            case FieldType.Bool:

                sb.Append((bool)value ? "true" : "false");

                break;

            case FieldType.Float:

                sb.Append(((float)value).ToString("G", CultureInfo.InvariantCulture));

                break;

            case FieldType.Double:

                sb.Append(((double)value).ToString("G", CultureInfo.InvariantCulture));

                break;

            case FieldType.Bytes:

                sb.Append('"');
                sb.Append(Convert.ToBase64String(((ByteString)value).ToByteArray()));
                sb.Append('"');

                break;

            case FieldType.Enum:
                {
                    var enumValue = field.EnumType.Values.FirstOrDefault(v => v.Number == (int)value);

                    if (enumValue is not null)
                    {
                        sb.Append('"');
                        sb.Append(enumValue.Name);
                        sb.Append('"');
                    }
                    else
                    {
                        // Fallback to integer if enum value not found
                        sb.Append((int)value);
                    }

                    break;
                }

            case FieldType.Message:

                // Recursively serialize nested message
                if (value is SimpleDynamicMessage nestedMessage)
                {
                    // Check for well-known types with special JSON encoding
                    var fullName = field.MessageType.FullName;

                    switch (fullName)
                    {
                        case "google.protobuf.Timestamp":
                            WellKnownTypeHandler.WriteTimestampJson(sb, nestedMessage);
                            break;

                        case "google.protobuf.Duration":
                            WellKnownTypeHandler.WriteDurationJson(sb, nestedMessage);
                            break;

                        case "google.protobuf.StringValue":
                        case "google.protobuf.Int32Value":
                        case "google.protobuf.Int64Value":
                        case "google.protobuf.UInt32Value":
                        case "google.protobuf.UInt64Value":
                        case "google.protobuf.FloatValue":
                        case "google.protobuf.DoubleValue":
                        case "google.protobuf.BoolValue":
                        case "google.protobuf.BytesValue":
                            WellKnownTypeHandler.WriteWrapperJson(sb, nestedMessage, field.MessageType,
                                (s, f, v) => WriteJsonValue(s, f, v, includeDefaults));
                            break;

                        case "google.protobuf.Any":
                            WellKnownTypeHandler.WriteAnyJson(sb, nestedMessage, field.MessageType);
                            break;

                        case "google.protobuf.Empty":
                            WellKnownTypeHandler.WriteEmptyJson(sb);
                            break;

                        case "google.protobuf.FieldMask":
                            WellKnownTypeHandler.WriteFieldMaskJson(sb, nestedMessage);
                            break;

                        case "google.protobuf.Struct":
                            WellKnownTypeHandler.WriteStructJson(sb, nestedMessage, WriteValueJson);
                            break;

                        case "google.protobuf.Value":
                            WellKnownTypeHandler.WriteValueJson(sb, nestedMessage, WriteStructJson, WriteListValueJson);
                            break;

                        case "google.protobuf.ListValue":
                            WellKnownTypeHandler.WriteListValueJson(sb, nestedMessage, WriteValueJson);
                            break;

                        default:
                            // Regular message — pass includeDefaults for recursive emit-defaults
                            sb.Append(nestedMessage.ToJson(includeDefaults));
                            break;
                    }
                }
                else
                {
                    sb.Append("null");
                }

                break;

            case FieldType.Group:
                
                // Groups are a deprecated proto2 feature not supported in proto3.
                // Modern gRPC services use proto3, so Group support is not implemented.
                sb.Append("null");
                break;

            default:
                
                sb.Append("null");
                break;
        }
    }

    // Adapter methods for WellKnownTypeHandler JSON serialization callbacks
    private void WriteStructJson(StringBuilder sb, SimpleDynamicMessage structMsg)
        => WellKnownTypeHandler.WriteStructJson(sb, structMsg, WriteValueJson);

    private void WriteValueJson(StringBuilder sb, SimpleDynamicMessage value)
        => WellKnownTypeHandler.WriteValueJson(sb, value, WriteStructJson, WriteListValueJson);

    private void WriteListValueJson(StringBuilder sb, SimpleDynamicMessage listValue)
        => WellKnownTypeHandler.WriteListValueJson(sb, listValue, WriteValueJson);
}