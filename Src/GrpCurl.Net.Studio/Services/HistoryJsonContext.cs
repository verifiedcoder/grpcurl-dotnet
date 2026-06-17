using System.Text.Json.Serialization;
using GrpCurl.Net.Studio.ViewModels.Models.History;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Source-generation context for history entries (SPEC-040 §5, ADR-008). One <see cref="HistoryEntry" />
///     per NDJSON line — compact (not indented) so a line is one entry — with camelCase names and
///     string-valued enums so the file is greppable and stable for the audit (FR-121, AC-13).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(HistoryEntry))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;
