using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Studio.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(WorkspaceLockInfo))]
internal sealed partial class WorkspaceLockJsonContext : JsonSerializerContext;
