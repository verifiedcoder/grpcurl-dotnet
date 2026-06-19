using GrpCurl.Net.Studio.ViewModels.Models;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     System.Text.Json source-generation context for <see cref="StudioSettings" /> (ADR-007:
///     source-generated, versioned JSON). camelCase, indented for human-diffable settings files.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(StudioSettings))]
internal sealed partial class StudioSettingsJsonContext : JsonSerializerContext;
