namespace Gql2Grpc.Diagnostics;

/// <summary>Three-level verbosity controller matching the <c>invoke</c> command's <c>-v</c>/<c>--vv</c> semantics.</summary>
public enum VerbosityLevel
{
    /// <summary>Suppress all diagnostic output (default).</summary>
    Quiet = 0,

    /// <summary>Per-field mapping resolution and resolved gRPC method name on stderr.</summary>
    Verbose = 1,

    /// <summary>Everything in <see cref="Verbose" /> plus translated request JSON on stderr.</summary>
    VeryVerbose = 2
}