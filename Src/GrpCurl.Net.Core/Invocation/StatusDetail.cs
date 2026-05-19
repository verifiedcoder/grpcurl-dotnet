using Google.Protobuf;

namespace GrpCurl.Net.Invocation;

/// <summary>One detail entry from a <see cref="StatusDetails" />.</summary>
public sealed record StatusDetail(string TypeUrl, byte[] RawValue, IMessage? ParsedMessage);