using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Models.Connections;

/// <summary>
///     A named, persisted invocation request (FR-145; SPEC-040 §3.2 <c>savedRequest</c>): everything needed
///     to re-open an invocation tab against a connection's method. Lives in the workspace file and is
///     git-shareable — it carries no secret values (header values are literals or <c>${VAR}</c> placeholders;
///     real secrets stay in <c>ISecretStore</c>, referenced from environments). Bound to its connection by
///     <see cref="ConnectionId" />.
/// </summary>
public sealed class SavedRequest
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The owning connection's <see cref="SavedConnection.Id" /> (sidebar grouping, FR-145).</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Fully-qualified <c>package.Service/Method</c>.</summary>
    public string Method { get; set; } = string.Empty;

    public RequestBodyFormat BodyFormat { get; set; } = RequestBodyFormat.Json;

    /// <summary>The request body; for client/duplex streams, multiple messages in the CLI's multi-message syntax.</summary>
    public string Body { get; set; } = string.Empty;

    public List<HeaderEntry> Headers { get; set; } = [];

    /// <summary>Optional deadline (duration grammar); no default (FR-069).</summary>
    public string? Deadline { get; set; }

    public bool EmitDefaults { get; set; }

    public bool AllowUnknownFields { get; set; }

    public long? MaxSendBytes { get; set; }

    public long? MaxReceiveBytes { get; set; }

    public SavedRequest Copy() => new()
    {
        Id = Id,
        Name = Name,
        ConnectionId = ConnectionId,
        Method = Method,
        BodyFormat = BodyFormat,
        Body = Body,
        Headers = Headers.Select(h => new HeaderEntry { Name = h.Name, Value = h.Value, IsBin = h.IsBin }).ToList(),
        Deadline = Deadline,
        EmitDefaults = EmitDefaults,
        AllowUnknownFields = AllowUnknownFields,
        MaxSendBytes = MaxSendBytes,
        MaxReceiveBytes = MaxReceiveBytes
    };
}
