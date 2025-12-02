using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Handles binary protobuf serialization for SimpleDynamicMessage.
/// </summary>
internal static class ProtobufWriter
{
    /// <summary>
    ///     Writes the message to a CodedOutputStream in protobuf binary format.
    /// </summary>
    public static void WriteTo(SimpleDynamicMessage message, CodedOutputStream output)
    {
        // Write non-repeated fields
        foreach (var (field, value) in message.Fields)
        {
            if (value is null)
            {
                continue;
            }

            // Write field tag and value using the simplified API
            switch (field.FieldType)
            {
                case FieldType.String:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                    output.WriteString((string)value);

                    break;

                case FieldType.Int32:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteInt32((int)value);

                    break;

                case FieldType.SInt32:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteSInt32((int)value);

                    break;

                case FieldType.SFixed32:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                    output.WriteSFixed32((int)value);

                    break;

                case FieldType.Int64:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteInt64((long)value);

                    break;

                case FieldType.SInt64:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteSInt64((long)value);

                    break;

                case FieldType.SFixed64:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                    output.WriteSFixed64((long)value);

                    break;

                case FieldType.UInt32:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteUInt32((uint)value);

                    break;

                case FieldType.Fixed32:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                    output.WriteFixed32((uint)value);

                    break;

                case FieldType.UInt64:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteUInt64((ulong)value);

                    break;

                case FieldType.Fixed64:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                    output.WriteFixed64((ulong)value);

                    break;

                case FieldType.Bool:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteBool((bool)value);

                    break;

                case FieldType.Float:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                    output.WriteFloat((float)value);

                    break;

                case FieldType.Double:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                    output.WriteDouble((double)value);

                    break;

                case FieldType.Bytes:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                    output.WriteBytes((ByteString)value);

                    break;

                case FieldType.Enum:

                    output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                    output.WriteEnum((int)value);

                    break;

                case FieldType.Message:

                    if (value is SimpleDynamicMessage nestedMessage)
                    {
                        output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                        output.WriteLength(CalculateSize(nestedMessage));
                        WriteTo(nestedMessage, output);
                    }

                    break;

                case FieldType.Group:
                    // Groups are a deprecated proto2 feature not supported in proto3.
                    // Modern gRPC services use proto3, so Group support is not implemented.
                    throw new NotSupportedException("FieldType.Group is deprecated and not supported.");

                default:
                    throw new InvalidOperationException($"Invalid field type: {field.FieldType}");
            }
        }

        // Write repeated fields
        foreach (var (field, values) in message.RepeatedFields)
        {
            // Check if field should use packed encoding.
            // Packed encoding is only for primitive types (proto3 default for numeric/bool/enum)
            var isPackable = field.FieldType is FieldType.Int32 or FieldType.Int64 or FieldType.UInt32 or FieldType.UInt64
                or FieldType.SInt32 or FieldType.SInt64 or FieldType.Fixed32 or FieldType.Fixed64
                or FieldType.SFixed32 or FieldType.SFixed64 or FieldType.Float or FieldType.Double
                or FieldType.Bool or FieldType.Enum;

            if (isPackable && field.IsPacked)
            {
                // Packed encoding: tag + length + values (no tags for individual values)
                // First, calculate the total size of all values
                var packedSize = values.OfType<object>()
                    .Sum(value => field.FieldType switch
                    {
                        // Integer types - ComputeSize returns the actual value size
                        FieldType.Int32 => CodedOutputStream.ComputeInt32Size((int)value),
                        FieldType.Int64 => CodedOutputStream.ComputeInt64Size((long)value),
                        FieldType.UInt32 => CodedOutputStream.ComputeUInt32Size((uint)value),
                        FieldType.UInt64 => CodedOutputStream.ComputeUInt64Size((ulong)value),
                        FieldType.SInt32 => CodedOutputStream.ComputeSInt32Size((int)value),
                        FieldType.SInt64 => CodedOutputStream.ComputeSInt64Size((long)value),
                        FieldType.Fixed32 => 4, // Fixed32 is always 4 bytes
                        FieldType.Fixed64 => 8, // Fixed64 is always 8 bytes
                        FieldType.SFixed32 => 4, // SFixed32 is always 4 bytes
                        FieldType.SFixed64 => 8, // SFixed64 is always 8 bytes
                        FieldType.Bool => 1, // Bool is always 1 byte
                        FieldType.Float => 4, // Float is always 4 bytes
                        FieldType.Double => 8, // Double is always 8 bytes
                        FieldType.Enum => CodedOutputStream.ComputeEnumSize((int)value), // Enums are encoded as ints
                        _ => 0
                    });

                switch (packedSize)
                {
                    // Write the packed field if it has any values
                    case > 0:
                        {
                            output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                            output.WriteLength(packedSize);

                            // Write all values without tags
                            foreach (var value in values.OfType<object>())
                            {
                                switch (field.FieldType)
                                {
                                    case FieldType.Int32:

                                        output.WriteInt32((int)value);

                                        break;

                                    case FieldType.SInt32:

                                        output.WriteSInt32((int)value);

                                        break;

                                    case FieldType.SFixed32:

                                        output.WriteSFixed32((int)value);

                                        break;

                                    case FieldType.Int64:

                                        output.WriteInt64((long)value);

                                        break;

                                    case FieldType.SInt64:

                                        output.WriteSInt64((long)value);

                                        break;

                                    case FieldType.SFixed64:

                                        output.WriteSFixed64((long)value);

                                        break;

                                    case FieldType.UInt32:

                                        output.WriteUInt32((uint)value);

                                        break;

                                    case FieldType.Fixed32:

                                        output.WriteFixed32((uint)value);

                                        break;

                                    case FieldType.UInt64:

                                        output.WriteUInt64((ulong)value);

                                        break;

                                    case FieldType.Fixed64:

                                        output.WriteFixed64((ulong)value);

                                        break;

                                    case FieldType.Bool:

                                        output.WriteBool((bool)value);

                                        break;

                                    case FieldType.Float:

                                        output.WriteFloat((float)value);

                                        break;

                                    case FieldType.Double:

                                        output.WriteDouble((double)value);

                                        break;

                                    case FieldType.Enum:

                                        output.WriteEnum((int)value);

                                        break;

                                    case FieldType.String:
                                    case FieldType.Group:
                                    case FieldType.Message:
                                    case FieldType.Bytes:

                                        break;

                                    default:

                                        throw new InvalidOperationException("Invalid Field Type");
                                }
                            }

                            break;
                        }
                }
            }
            else
            {
                // Unpacked encoding: tag + value for each element (proto2 style or non-packable types)
                foreach (var value in values.OfType<object>())
                {
                    // Write each element with the same tag
                    switch (field.FieldType)
                    {
                        case FieldType.String:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                            output.WriteString((string)value);

                            break;

                        case FieldType.Int32:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteInt32((int)value);

                            break;

                        case FieldType.SInt32:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteSInt32((int)value);

                            break;

                        case FieldType.SFixed32:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                            output.WriteSFixed32((int)value);

                            break;

                        case FieldType.Int64:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteInt64((long)value);

                            break;

                        case FieldType.SInt64:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteSInt64((long)value);

                            break;

                        case FieldType.SFixed64:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                            output.WriteSFixed64((long)value);

                            break;

                        case FieldType.UInt32:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteUInt32((uint)value);

                            break;

                        case FieldType.Fixed32:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                            output.WriteFixed32((uint)value);

                            break;

                        case FieldType.UInt64:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteUInt64((ulong)value);

                            break;

                        case FieldType.Fixed64:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                            output.WriteFixed64((ulong)value);

                            break;

                        case FieldType.Bool:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteBool((bool)value);

                            break;

                        case FieldType.Float:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                            output.WriteFloat((float)value);

                            break;

                        case FieldType.Double:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                            output.WriteDouble((double)value);

                            break;

                        case FieldType.Bytes:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                            output.WriteBytes((ByteString)value);

                            break;

                        case FieldType.Enum:

                            output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                            output.WriteEnum((int)value);

                            break;

                        case FieldType.Message:

                            if (value is SimpleDynamicMessage nestedMsg)
                            {
                                output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                                output.WriteLength(CalculateSize(nestedMsg));
                                WriteTo(nestedMsg, output);
                            }

                            break;

                        case FieldType.Group:
                            // Groups are a deprecated proto2 feature not supported in proto3.
                            // Modern gRPC services use proto3, so Group support is not implemented.
                            throw new NotSupportedException("FieldType.Group is deprecated and not supported.");

                        default:
                            throw new InvalidOperationException($"Invalid field type: {field.FieldType}");
                    }
                }
            }
        }

        // Write map fields as repeated entry messages
        foreach (var (field, map) in message.MapFields)
        {
            var mapDescriptor = field.MessageType;
            var keyField = mapDescriptor.FindFieldByNumber(1);
            var valueField = mapDescriptor.FindFieldByNumber(2);

            foreach (var (key, value) in map)
            {
                // Create an entry message for each key-value pair
                var entryMessage = new SimpleDynamicMessage(mapDescriptor);

                if (keyField is not null)
                {
                    entryMessage.Fields[keyField] = key;
                }

                if (valueField is not null)
                {
                    entryMessage.Fields[valueField] = value;
                }

                // Write the entry message
                output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                output.WriteLength(CalculateSize(entryMessage));

                WriteTo(entryMessage, output);
            }
        }
    }

    /// <summary>
    ///     Calculates the serialized size of the message in bytes.
    /// </summary>
    public static int CalculateSize(SimpleDynamicMessage message)
    {
        var size = 0;

        // Calculate size for non-repeated fields
        foreach (var (field, value) in message.Fields)
        {
            if (value is null)
            {
                continue;
            }

            size += CodedOutputStream.ComputeTagSize(field.FieldNumber);
            size += field.FieldType switch
            {
                FieldType.String => CodedOutputStream.ComputeStringSize((string)value),
                FieldType.Int32 => CodedOutputStream.ComputeInt32Size((int)value),
                FieldType.SInt32 => CodedOutputStream.ComputeSInt32Size((int)value),
                FieldType.SFixed32 => CodedOutputStream.ComputeSFixed32Size((int)value),
                FieldType.Int64 => CodedOutputStream.ComputeInt64Size((long)value),
                FieldType.SInt64 => CodedOutputStream.ComputeSInt64Size((long)value),
                FieldType.SFixed64 => CodedOutputStream.ComputeSFixed64Size((long)value),
                FieldType.UInt32 => CodedOutputStream.ComputeUInt32Size((uint)value),
                FieldType.Fixed32 => CodedOutputStream.ComputeFixed32Size((uint)value),
                FieldType.UInt64 => CodedOutputStream.ComputeUInt64Size((ulong)value),
                FieldType.Fixed64 => CodedOutputStream.ComputeFixed64Size((ulong)value),
                FieldType.Bool => CodedOutputStream.ComputeBoolSize((bool)value),
                FieldType.Float => CodedOutputStream.ComputeFloatSize((float)value),
                FieldType.Double => CodedOutputStream.ComputeDoubleSize((double)value),
                FieldType.Bytes => CodedOutputStream.ComputeBytesSize((ByteString)value),
                FieldType.Enum => CodedOutputStream.ComputeEnumSize((int)value),
                FieldType.Message => value is SimpleDynamicMessage msg
                    ? CodedOutputStream.ComputeLengthSize(CalculateSize(msg)) + CalculateSize(msg)
                    : 0,
                _ => 0
            };
        }

        // Add size for repeated fields
        foreach (var (field, values) in message.RepeatedFields)
        {
            // Check if field should use packed encoding
            var isPackable = field.FieldType is FieldType.Int32 or FieldType.Int64 or FieldType.UInt32 or FieldType.UInt64
                or FieldType.SInt32 or FieldType.SInt64 or FieldType.Fixed32 or FieldType.Fixed64
                or FieldType.SFixed32 or FieldType.SFixed64 or FieldType.Float or FieldType.Double
                or FieldType.Bool or FieldType.Enum;

            if (isPackable && field.IsPacked)
            {
                // Packed encoding: calculate total size of values without tags
                var packedSize = values.OfType<object>()
                    .Sum(value => field.FieldType switch
                    {
                        // Integer types - ComputeSize returns the actual value size
                        FieldType.Int32 => CodedOutputStream.ComputeInt32Size((int)value),
                        FieldType.Int64 => CodedOutputStream.ComputeInt64Size((long)value),
                        FieldType.UInt32 => CodedOutputStream.ComputeUInt32Size((uint)value),
                        FieldType.UInt64 => CodedOutputStream.ComputeUInt64Size((ulong)value),
                        FieldType.SInt32 => CodedOutputStream.ComputeSInt32Size((int)value),
                        FieldType.SInt64 => CodedOutputStream.ComputeSInt64Size((long)value),
                        FieldType.Fixed32 => 4, // Fixed32 is always 4 bytes
                        FieldType.Fixed64 => 8, // Fixed64 is always 8 bytes
                        FieldType.SFixed32 => 4, // SFixed32 is always 4 bytes
                        FieldType.SFixed64 => 8, // SFixed64 is always 8 bytes
                        FieldType.Bool => 1, // Bool is always 1 byte
                        FieldType.Float => 4, // Float is always 4 bytes
                        FieldType.Double => 8, // Double is always 8 bytes
                        FieldType.Enum => CodedOutputStream.ComputeEnumSize((int)value), // Enums are encoded as ints
                        _ => 0
                    });

                // Add size for tag + length + packed values
                if (packedSize <= 0)
                {
                    continue;
                }

                size += CodedOutputStream.ComputeTagSize(field.FieldNumber);
                size += CodedOutputStream.ComputeLengthSize(packedSize);
                size += packedSize;
            }
            else
            {
                // Unpacked encoding: tag + size for each value
                foreach (var value in values.OfType<object>())
                {
                    size += CodedOutputStream.ComputeTagSize(field.FieldNumber);
                    size += field.FieldType switch
                    {
                        FieldType.String => CodedOutputStream.ComputeStringSize((string)value),
                        FieldType.Int32 => CodedOutputStream.ComputeInt32Size((int)value),
                        FieldType.SInt32 => CodedOutputStream.ComputeSInt32Size((int)value),
                        FieldType.SFixed32 => CodedOutputStream.ComputeSFixed32Size((int)value),
                        FieldType.Int64 => CodedOutputStream.ComputeInt64Size((long)value),
                        FieldType.SInt64 => CodedOutputStream.ComputeSInt64Size((long)value),
                        FieldType.SFixed64 => CodedOutputStream.ComputeSFixed64Size((long)value),
                        FieldType.UInt32 => CodedOutputStream.ComputeUInt32Size((uint)value),
                        FieldType.Fixed32 => CodedOutputStream.ComputeFixed32Size((uint)value),
                        FieldType.UInt64 => CodedOutputStream.ComputeUInt64Size((ulong)value),
                        FieldType.Fixed64 => CodedOutputStream.ComputeFixed64Size((ulong)value),
                        FieldType.Bool => CodedOutputStream.ComputeBoolSize((bool)value),
                        FieldType.Float => CodedOutputStream.ComputeFloatSize((float)value),
                        FieldType.Double => CodedOutputStream.ComputeDoubleSize((double)value),
                        FieldType.Bytes => CodedOutputStream.ComputeBytesSize((ByteString)value),
                        FieldType.Enum => CodedOutputStream.ComputeEnumSize((int)value),
                        FieldType.Message => value is SimpleDynamicMessage msg
                            ? CodedOutputStream.ComputeLengthSize(CalculateSize(msg)) + CalculateSize(msg)
                            : 0,
                        _ => 0
                    };
                }
            }
        }

        // Add size for map fields (each entry is a message)
        foreach (var (field, map) in message.MapFields)
        {
            var mapDescriptor = field.MessageType;
            var keyField = mapDescriptor.FindFieldByNumber(1);
            var valueField = mapDescriptor.FindFieldByNumber(2);

            foreach (var (key, value) in map)
            {
                // Create temporary entry message to calculate size
                var entryMessage = new SimpleDynamicMessage(mapDescriptor);

                if (keyField is not null)
                {
                    entryMessage.Fields[keyField] = key;
                }

                if (valueField is not null)
                {
                    entryMessage.Fields[valueField] = value;
                }

                var entrySize = CalculateSize(entryMessage);

                size += CodedOutputStream.ComputeTagSize(field.FieldNumber);
                size += CodedOutputStream.ComputeLengthSize(entrySize);
                size += entrySize;
            }
        }

        return size;
    }
}