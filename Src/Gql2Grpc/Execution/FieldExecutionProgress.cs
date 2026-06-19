namespace Gql2Grpc.Execution;

/// <summary>Lifecycle state of a single root field as it moves through the parallel scheduler.</summary>
public enum FieldExecutionState
{
    /// <summary>Accepted into the run but not yet started (waiting on the bounded-concurrency window).</summary>
    Queued,

    /// <summary>Worker is executing (the RPC is in flight, or the introspection field is resolving).</summary>
    InFlight,

    /// <summary>Worker finished and produced a non-failed result.</summary>
    Done,

    /// <summary>Worker finished but the field result is a failure (its errors are populated).</summary>
    Failed
}

/// <summary>
///     An observational progress notification emitted as a root field transitions through
///     <see cref="ParallelFieldScheduler" />. Supplying an <see cref="IProgress{T}" /> is purely a
///     presentation concern (Studio's per-root-field progress rows): it never changes execution
///     behaviour, ordering, or output, so the CLI — which passes no progress sink — is unaffected.
///     <see cref="Elapsed" /> is populated only on the terminal <see cref="FieldExecutionState.Done" />
///     and <see cref="FieldExecutionState.Failed" /> states.
/// </summary>
/// <param name="FieldIndex">Zero-based position of the root field in document order.</param>
/// <param name="ResponseKey">The field's response key (its alias if supplied, else the field name).</param>
/// <param name="State">The state being entered.</param>
/// <param name="Elapsed">Wall-clock time the worker took; null for non-terminal states.</param>
public sealed record FieldExecutionProgress(
    int FieldIndex,
    string ResponseKey,
    FieldExecutionState State,
    TimeSpan? Elapsed = null);
