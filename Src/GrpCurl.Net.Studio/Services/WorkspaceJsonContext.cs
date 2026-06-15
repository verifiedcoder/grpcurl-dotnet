using System.Text.Json.Serialization;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     System.Text.Json source-generation context for the workspace model (ADR-007). Enums
///     serialize as camelCase strings so the file stays human-diffable.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WorkspaceModel))]
internal sealed partial class WorkspaceJsonContext : JsonSerializerContext;
