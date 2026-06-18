using System.Text.Json.Serialization;
using GrpCurl.Net.Studio.ViewModels.Models.Session;

namespace GrpCurl.Net.Studio.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(SessionState))]
internal sealed partial class SessionStateJsonContext : JsonSerializerContext;
