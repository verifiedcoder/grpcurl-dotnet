using GrpCurl.Net.Studio.ViewModels.Models.Session;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(SessionState))]
internal sealed partial class SessionStateJsonContext : JsonSerializerContext;
