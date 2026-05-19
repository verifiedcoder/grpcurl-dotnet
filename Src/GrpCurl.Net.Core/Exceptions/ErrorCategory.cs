namespace GrpCurl.Net.Exceptions;

/// <summary>
///     Discriminator for the cause of a CLI error. Drives both stderr text rendering
///     and the JSON envelope's <c>category</c> field.
/// </summary>
internal enum ErrorCategory
{
    /// <summary>Bad CLI args, missing required option, JSON parse error.</summary>
    Usage,

    /// <summary>Schema or file problem: protoset missing, symbol not found, file overwrite refused.</summary>
    Schema,

    /// <summary>Network failure before or around the gRPC call.</summary>
    Network,

    /// <summary>Connect or operation timeout.</summary>
    Timeout,

    /// <summary>RPC returned a non-OK status.</summary>
    Rpc,

    /// <summary>User interrupt (Ctrl+C).</summary>
    Cancelled,

    /// <summary>Unhandled or unknown error.</summary>
    Internal
}