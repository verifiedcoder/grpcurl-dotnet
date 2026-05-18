using System.Globalization;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     Minimal protobuf text-format reader and writer for <see cref="SimpleDynamicMessage"/>.
///     Implemented because Google.Protobuf for .NET does not ship a TextFormat parser. The
///     subset covered is enough for grpcurl parity (CODE-REVIEW.md P2 "Protobuf Text Format
///     Is Missing"): scalars (numbers, bools, strings, bytes, enums), nested messages,
///     repeated fields, and maps. Groups and extensions are not supported.
/// </summary>
internal static class DynamicTextFormat
{
    public static string Print(SimpleDynamicMessage message)
    {
        var sb = new StringBuilder();

        Print(message, sb, indent: 0);

        return sb.ToString();
    }

    private static void Print(SimpleDynamicMessage message, StringBuilder sb, int indent)
    {
        var pad = new string(' ', indent * 2);

        foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
        {
            if (field.IsMap && message.MapFields.TryGetValue(field, out var map))
            {
                var keyField = field.MessageType.FindFieldByNumber(1)!;
                var valueField = field.MessageType.FindFieldByNumber(2)!;

                foreach (var (k, v) in map)
                {
                    sb.Append(pad).Append(field.Name).AppendLine(" {");
                    sb.Append(pad).Append("  ").Append(keyField.Name).Append(": ");
                    PrintScalar(sb, k, keyField);
                    sb.AppendLine();
                    sb.Append(pad).Append("  ").Append(valueField.Name).Append(": ");
                    PrintFieldValue(v, valueField, sb, indent + 1);
                    sb.AppendLine();
                    sb.Append(pad).AppendLine("}");
                }
            }
            else if (field.IsRepeated && message.RepeatedFields.TryGetValue(field, out var list))
            {
                foreach (var value in list)
                {
                    sb.Append(pad).Append(field.Name);

                    if (field.FieldType == FieldType.Message)
                    {
                        sb.AppendLine(" {");
                        Print((SimpleDynamicMessage)value!, sb, indent + 1);
                        sb.Append(pad).AppendLine("}");
                    }
                    else
                    {
                        sb.Append(": ");
                        PrintScalar(sb, value, field);
                        sb.AppendLine();
                    }
                }
            }
            else if (message.Fields.TryGetValue(field, out var value))
            {
                sb.Append(pad).Append(field.Name);

                if (field.FieldType == FieldType.Message)
                {
                    sb.AppendLine(" {");
                    Print((SimpleDynamicMessage)value!, sb, indent + 1);
                    sb.Append(pad).AppendLine("}");
                }
                else
                {
                    sb.Append(": ");
                    PrintScalar(sb, value, field);
                    sb.AppendLine();
                }
            }
        }
    }

    private static void PrintFieldValue(object? value, FieldDescriptor field, StringBuilder sb, int indent)
    {
        if (field.FieldType == FieldType.Message)
        {
            sb.AppendLine("{");
            Print((SimpleDynamicMessage)value!, sb, indent + 1);
            sb.Append(new string(' ', indent * 2)).Append('}');
        }
        else
        {
            PrintScalar(sb, value, field);
        }
    }

    private static void PrintScalar(StringBuilder sb, object? value, FieldDescriptor field)
    {
        switch (field.FieldType)
        {
            case FieldType.String:
                sb.Append('"').Append(EscapeString((string)(value ?? ""))).Append('"');
                break;

            case FieldType.Bytes:
                var bytes = value as ByteString ?? ByteString.Empty;
                sb.Append('"').Append(EscapeBytes(bytes)).Append('"');
                break;

            case FieldType.Bool:
                sb.Append((bool)(value ?? false) ? "true" : "false");
                break;

            case FieldType.Enum:
                var enumNumber = Convert.ToInt32(value ?? 0);
                var enumValue = field.EnumType?.FindValueByNumber(enumNumber);
                sb.Append(enumValue?.Name ?? enumNumber.ToString(CultureInfo.InvariantCulture));
                break;

            case FieldType.Float:
                sb.Append(((float)(value ?? 0f)).ToString("R", CultureInfo.InvariantCulture));
                break;

            case FieldType.Double:
                sb.Append(((double)(value ?? 0d)).ToString("R", CultureInfo.InvariantCulture));
                break;

            default:
                sb.Append(Convert.ToString(value ?? 0, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static string EscapeString(string raw)
    {
        var sb = new StringBuilder(raw.Length);

        foreach (var c in raw)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x7f)
                    {
                        sb.Append($"\\x{(int)c:x2}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private static string EscapeBytes(ByteString bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);

        foreach (var b in bytes)
        {
            if (b >= 0x20 && b < 0x7f && b != '\\' && b != '"')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append($"\\x{b:x2}");
            }
        }

        return sb.ToString();
    }

    public static SimpleDynamicMessage Parse(MessageDescriptor descriptor, string text)
    {
        var lexer = new Lexer(text);
        var message = new SimpleDynamicMessage(descriptor);

        ParseFields(lexer, message);

        if (lexer.Peek() is not null)
        {
            throw new FormatException($"Unexpected trailing token in text-format input: '{lexer.Peek()}'.");
        }

        return message;
    }

    private static void ParseFields(Lexer lexer, SimpleDynamicMessage message)
    {
        while (true)
        {
            var fieldName = lexer.Peek();

            if (fieldName is null || fieldName == "}")
            {
                return;
            }

            lexer.Read();

            var field = message.Descriptor.Fields.InDeclarationOrder()
                .FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.Ordinal)
                                  || string.Equals(f.JsonName, fieldName, StringComparison.Ordinal)) ?? throw new FormatException($"Unknown field '{fieldName}' on {message.Descriptor.FullName}.");
            var separator = lexer.Read();

            object? value;

            if (separator == ":")
            {
                value = ParseScalar(lexer, field);
            }
            else if (separator == "{")
            {
                if (field.FieldType != FieldType.Message)
                {
                    throw new FormatException($"Field '{field.Name}' is not a message; got brace.");
                }

                var nested = new SimpleDynamicMessage(field.MessageType);

                ParseFields(lexer, nested);

                var close = lexer.Read();

                if (close != "}")
                {
                    throw new FormatException($"Expected '}}' to close message '{field.Name}', got '{close}'.");
                }

                value = nested;
            }
            else
            {
                throw new FormatException($"Expected ':' or '{{' after field '{field.Name}', got '{separator}'.");
            }

            if (field.IsRepeated && !field.IsMap)
            {
                if (!message.RepeatedFields.TryGetValue(field, out var list))
                {
                    list = [];
                    message.RepeatedFields[field] = list;
                }

                list.Add(value);
            }
            else if (field.IsMap)
            {
                throw new FormatException("Map fields are not yet supported via text-format input.");
            }
            else
            {
                message.Fields[field] = value;
            }
        }
    }

    private static object? ParseScalar(Lexer lexer, FieldDescriptor field)
    {
        var token = lexer.Read() ?? throw new FormatException($"Missing value for field '{field.Name}'.");

        return field.FieldType switch
        {
            FieldType.String => UnescapeString(StripQuotes(token)),
            FieldType.Bytes => ByteString.CopyFrom(UnescapeBytes(StripQuotes(token))),
            FieldType.Bool => bool.Parse(token),
            FieldType.Enum => ParseEnumOrNumber(token, field),
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32
                => int.Parse(token, CultureInfo.InvariantCulture),
            FieldType.UInt32 or FieldType.Fixed32
                => uint.Parse(token, CultureInfo.InvariantCulture),
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64
                => long.Parse(token, CultureInfo.InvariantCulture),
            FieldType.UInt64 or FieldType.Fixed64
                => ulong.Parse(token, CultureInfo.InvariantCulture),
            FieldType.Float => float.Parse(token, CultureInfo.InvariantCulture),
            FieldType.Double => double.Parse(token, CultureInfo.InvariantCulture),
            _ => ParseEnumOrNumber(token, field)
        };
    }

    private static int ParseEnumOrNumber(string token, FieldDescriptor field)
    {
        if (field.FieldType == FieldType.Enum && field.EnumType is { } enumType)
        {
            var enumValue = enumType.FindValueByName(token);

            if (enumValue is not null)
            {
                return enumValue.Number;
            }
        }

        return int.Parse(token, CultureInfo.InvariantCulture);
    }

    private static string StripQuotes(string token)
    {
        if (token.Length >= 2 && (token[0] == '"' || token[0] == '\'') && token[^1] == token[0])
        {
            return token[1..^1];
        }

        throw new FormatException($"Expected quoted string, got '{token}'.");
    }

    private static string UnescapeString(string escaped)
    {
        var sb = new StringBuilder(escaped.Length);
        var escaping = false;
        var waitingForFirstHexDigit = false;
        char? firstHexDigit = null;

        foreach (var ch in escaped)
        {
            if (waitingForFirstHexDigit)
            {
                firstHexDigit = ch;
                waitingForFirstHexDigit = false;
                continue;
            }

            if (firstHexDigit is { } highHex)
            {
                sb.Append((char)ParseHexByte(highHex, ch));
                firstHexDigit = null;
                continue;
            }

            if (!escaping)
            {
                if (ch == '\\')
                {
                    escaping = true;
                }
                else
                {
                    sb.Append(ch);
                }

                continue;
            }

            escaping = false;

            switch (ch)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case '\'': sb.Append('\''); break;
                case 'x': waitingForFirstHexDigit = true; break;
                default: sb.Append(ch); break;
            }
        }

        if (escaping)
        {
            throw new FormatException("Trailing escape sequence.");
        }

        if (waitingForFirstHexDigit || firstHexDigit is not null)
        {
            throw new FormatException("Hex escape sequence requires two digits.");
        }

        return sb.ToString();
    }

    private static byte[] UnescapeBytes(string escaped)
    {
        var bytes = new List<byte>(escaped.Length);
        var escaping = false;
        var waitingForFirstHexDigit = false;
        char? firstHexDigit = null;

        foreach (var ch in escaped)
        {
            if (waitingForFirstHexDigit)
            {
                firstHexDigit = ch;
                waitingForFirstHexDigit = false;
                continue;
            }

            if (firstHexDigit is { } highHex)
            {
                bytes.Add(ParseHexByte(highHex, ch));
                firstHexDigit = null;
                continue;
            }

            if (!escaping)
            {
                if (ch == '\\')
                {
                    escaping = true;
                }
                else
                {
                    bytes.Add((byte)ch);
                }

                continue;
            }

            escaping = false;

            switch (ch)
            {
                case 'n': bytes.Add(0x0a); break;
                case 'r': bytes.Add(0x0d); break;
                case 't': bytes.Add(0x09); break;
                case '\\': bytes.Add(0x5c); break;
                case '"': bytes.Add(0x22); break;
                case 'x': waitingForFirstHexDigit = true; break;
                default: bytes.Add((byte)ch); break;
            }
        }

        if (escaping)
        {
            throw new FormatException("Trailing escape sequence.");
        }

        if (waitingForFirstHexDigit || firstHexDigit is not null)
        {
            throw new FormatException("Hex escape sequence requires two digits.");
        }

        return [.. bytes];
    }

    private static byte ParseHexByte(char high, char low) => (byte)((HexValue(high) << 4) + HexValue(low));

    private static int HexValue(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'a' and <= 'f' => ch - 'a' + 10,
        >= 'A' and <= 'F' => ch - 'A' + 10,
        _ => throw new FormatException($"Invalid hex digit '{ch}'.")
    };

    /// <summary>
    ///     Minimal lexer that yields field names, ':', '{', '}', quoted strings, and bare
    ///     numeric/identifier tokens. Whitespace and `#`-prefixed comments are skipped.
    /// </summary>
    private sealed class Lexer(string text)
    {
        private readonly string _text = text;
        private int _pos = 0;
        private string? _peeked;

        public string? Peek()
        {
            if (_peeked is not null)
            {
                return _peeked;
            }

            _peeked = ReadNext();

            return _peeked;
        }

        public string? Read()
        {
            if (_peeked is not null)
            {
                var p = _peeked;

                _peeked = null;

                return p;
            }

            return ReadNext();
        }

        private string? ReadNext()
        {
            SkipWhitespaceAndComments();

            if (_pos >= _text.Length)
            {
                return null;
            }

            var c = _text[_pos];

            if (c is ':' or '{' or '}' or ',' or ';' or '[' or ']')
            {
                _pos++;
                return c.ToString();
            }

            if (c == '"' || c == '\'')
            {
                return ReadQuoted(c);
            }

            return ReadIdentifierOrNumber();
        }

        private string ReadQuoted(char quote)
        {
            var start = _pos;

            _pos++;

            while (_pos < _text.Length && _text[_pos] != quote)
            {
                if (_text[_pos] == '\\' && _pos + 1 < _text.Length)
                {
                    _pos++;
                }

                _pos++;
            }

            if (_pos >= _text.Length)
            {
                throw new FormatException("Unterminated quoted string in text-format input.");
            }

            _pos++;

            return _text[start.._pos];
        }

        private string ReadIdentifierOrNumber()
        {
            var start = _pos;

            while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos]) &&
                   _text[_pos] is not ':' and not '{' and not '}' and not ',' and not ';' and not '[' and not ']')
            {
                _pos++;
            }

            return _text[start.._pos];
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                }
                else if (_text[_pos] == '#')
                {
                    while (_pos < _text.Length && _text[_pos] != '\n')
                    {
                        _pos++;
                    }
                }
                else
                {
                    return;
                }
            }
        }
    }
}
