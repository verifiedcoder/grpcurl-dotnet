using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     A value that is either a plain string literal or a secret reference (SPEC-040 §3.2
///     <c>stringOrSecret</c>). A secret carries only the <see cref="SecretRef" /> keyref — the workspace
///     file never holds the secret value (SEC, FR-132). Serializes as the bare string for a literal, or
///     <c>{"$secret":"&lt;keyref&gt;"}</c> for a secret.
/// </summary>
[JsonConverter(typeof(StringOrSecretConverter))]
public sealed record StringOrSecret(string? Literal, string? SecretRef)
{
    public bool IsSecret => SecretRef is not null;

    public static StringOrSecret Plain(string value) => new(value, null);

    public static StringOrSecret Secret(string keyRef) => new(null, keyRef);
}

/// <summary>Serializes <see cref="StringOrSecret" /> as a bare string or a <c>{"$secret":…}</c> object.</summary>
public sealed class StringOrSecretConverter : JsonConverter<StringOrSecret>
{
    private const string SecretProperty = "$secret";

    public override StringOrSecret Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return StringOrSecret.Plain(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty(SecretProperty, out var secret) && secret.ValueKind == JsonValueKind.String)
            {
                return StringOrSecret.Secret(secret.GetString()!);
            }
        }

        throw new JsonException("Expected a string or a { \"$secret\": \"…\" } object.");
    }

    public override void Write(Utf8JsonWriter writer, StringOrSecret value, JsonSerializerOptions options)
    {
        if (value.SecretRef is { } keyRef)
        {
            writer.WriteStartObject();
            writer.WriteString(SecretProperty, keyRef);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStringValue(value.Literal ?? string.Empty);
        }
    }
}
