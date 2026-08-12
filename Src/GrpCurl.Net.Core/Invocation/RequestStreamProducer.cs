using Google.Protobuf;
using Grpc.Core;

namespace GrpCurl.Net.Invocation;

/// <summary>
///     A request-half failure together with which side raised it.
///     <para>
///         The distinction matters for <see cref="RpcException" />. One raised by a write belongs to
///         <i>this</i> call, so it is normalized like a read fault. One raised by the caller's source
///         belongs to whatever that source was doing — a gRPC-backed source carries its own call's
///         status — so it is surfaced untouched. Normalizing it would rewrite a foreign status using
///         this call's deadline and cancellation state, and would make the reported status depend on
///         how quickly this server happened to finish.
///     </para>
/// </summary>
internal sealed record ProducerFault(Exception Exception, bool FromWrite);

/// <summary>
///     What a producer does to the call when its own request half fails.
/// </summary>
internal enum RequestFaultPolicy
{
    /// <summary>
    ///     Half-close, give the server <c>ResponseReleaseGrace</c> to finish on its own, and abort only
    ///     if it does not. Bidi's policy (PRD-003): the responses already streamed stand on their own,
    ///     and request EOF is all a server needs if it finishes on one.
    /// </summary>
    HalfCloseThenAbort,

    /// <summary>
    ///     Skip the half-close, but keep the same bounded window before aborting. Client streaming's
    ///     policy (PRD-004A): the server's single response is an aggregate over the <i>whole</i>
    ///     request stream, so a clean EOF would invite it to compute and commit one over a stream the
    ///     client already knows is truncated.
    ///     <para>
    ///         The window is not the same thing as the half-close, and PRD-004A's first attempt lost
    ///         that distinction by aborting at once. A server that had already failed — and whose
    ///         status was in flight when the write failed — had that status destroyed by the reset,
    ///         leaving the local write shadow as the only error left to report. Waiting costs a
    ///         bounded delay on a call that is failing anyway; not waiting costs the server's word.
    ///     </para>
    /// </summary>
    AbortWithoutHalfClose
}

/// <summary>
///     Owns the request half of a client-streaming or bidi call (PRD-003, extended by PRD-004A): the
///     task pumping the caller's request source into the request stream, the token sources that stop
///     it, and the fault it failed with.
///     <para>
///         Two rules drive the whole design. They are the same for both call shapes; only
///         <see cref="RequestFaultPolicy" /> — what a fault does to the call — differs.
///     </para>
///     <para>
///         <b>Fault attribution is structural, never by exception type.</b> A transport write failure
///         and a caller-source failure can both surface as <see cref="InvalidOperationException" />,
///         <see cref="IOException" />, <see cref="OperationCanceledException" /> or
///         <see cref="RpcException" /> — the latter even carrying <see cref="StatusCode.OK" /> once a
///         call has completed. So post-completion classification is applied strictly around
///         <c>WriteAsync</c>/<c>CompleteAsync</c>, and everything the source raises is recorded as a
///         fault regardless of its type.
///     </para>
///     <para>
///         <b>A recorded fault is stamped before any teardown it triggers, and a fault caused by
///         teardown is not recorded at all.</b> Together those give the read side a causal test: a
///         non-null <see cref="Fault" /> always predates, and plausibly explains, whatever the call
///         finished with. "The call ended, and our own cancellation then made the producer fail" can
///         never masquerade as the reverse — in either direction, so a completed response half stays
///         authoritative whether it ended with an error or with OK.
///     </para>
/// </summary>
internal sealed class RequestStreamProducer
{
    /// <summary>
    ///     How long a failed producer waits for the server to end the response side on its own before
    ///     aborting the call. Applies under both policies — only the half-close preceding it varies.
    ///     <para>
    ///         A half-close is ordinary request EOF; gRPC does not make response completion a
    ///         consequence of it. A duplex server may still be processing, waiting on something
    ///         external, or streaming indefinitely. Waiting for it to volunteer would make fault
    ///         delivery unbounded — the very hang this fix exists to remove — so the graceful signal
    ///         gets a bounded window and then the call is aborted. RST_STREAM is the honest signal
    ///         here: the client could not produce the request stream it promised.
    ///     </para>
    /// </summary>
    private static readonly TimeSpan ResponseReleaseGrace = TimeSpan.FromMilliseconds(250);

    private readonly CancellationTokenSource _callCts;

    /// <summary>Guards <see cref="_writerCancellation" />, which two teardown paths can start.</summary>
    private readonly Lock _cancelGate = new();

    private readonly RequestFaultPolicy _faultPolicy;
    private readonly IClientStreamWriter<IMessage> _requestStream;

    private readonly TaskCompletionSource _responseEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _writerCts;

    private int _abortedCall;
    private ProducerFault? _fault;
    private int _released;
    private Task? _writerCancellation;

    private RequestStreamProducer(
        IClientStreamWriter<IMessage> requestStream,
        CancellationTokenSource callCts,
        RequestFaultPolicy faultPolicy)
    {
        _requestStream = requestStream;
        _callCts = callCts;
        _faultPolicy = faultPolicy;
        _writerCts = CancellationTokenSource.CreateLinkedTokenSource(callCts.Token);
    }

    /// <summary>The pump task. Never awaited unbounded — that would reintroduce the filed hang.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     The first fault raised by the caller's source or by a write that failed on its own merits,
    ///     stamped here <i>before</i> the half-close or abort it triggers. <see langword="null" />
    ///     while the producer is healthy, when it merely observed cancellation, or when it failed
    ///     only <i>because</i> it was cancelled — see <see cref="PumpAsync" />.
    /// </summary>
    public ProducerFault? Fault => Volatile.Read(ref _fault);

    /// <summary>
    ///     Whether this producer aborted the call to release a reader the server was not going to
    ///     release itself. It is a <i>necessary</i> condition for treating a
    ///     <see cref="StatusCode.Cancelled" /> read failure as this producer's own artifact — which is
    ///     what lets the read side keep a genuine write fault instead of the cancellation that fault
    ///     caused — but deliberately not a sufficient one.
    ///     <para>
    ///         It proves this producer issued an abort, not that the particular failure the reader saw
    ///         came from that abort: a server may return CANCELLED on its own account, and one that
    ///         does can coexist with an abort we issued. Provenance is what separates them, so callers
    ///         pair this with <see cref="RpcErrorNormalizer.IsCancellationArtifact" /> rather than
    ///         relying on it alone (PRD-004A review, round 1 finding 2).
    ///     </para>
    /// </summary>
    public bool AbortedCall => Volatile.Read(ref _abortedCall) != 0;

    /// <summary>
    ///     Starts pumping <paramref name="requests" /> into <paramref name="requestStream" />. Takes
    ///     ownership of <paramref name="callCts" />, whose token must already be the call's own, so a
    ///     failed producer can abort the call to release a blocked reader.
    /// </summary>
    public static RequestStreamProducer Start(
        IAsyncEnumerable<IMessage> requests,
        IClientStreamWriter<IMessage> requestStream,
        CancellationTokenSource callCts,
        RequestFaultPolicy faultPolicy = RequestFaultPolicy.HalfCloseThenAbort)
    {
        var producer = new RequestStreamProducer(requestStream, callCts, faultPolicy);

        // CancellationToken.None: a token already cancelled when the task is scheduled must still run
        // the pump's own unwind rather than leave the task Canceled and unshaped.
        producer.Completion = Task.Run(() => producer.PumpAsync(requests), CancellationToken.None);

        return producer;
    }

    /// <summary>
    ///     Cancels a token source that a concurrent teardown may already have disposed.
    ///     <see cref="CancellationTokenSource.Cancel()" /> throws <see cref="ObjectDisposedException" />
    ///     after disposal and <see cref="CancellationTokenSource.CancelAsync" /> returns a faulted task,
    ///     and <c>Dispose</c> is documented as not thread-safe against concurrent calls. <c>CancelAsync</c>
    ///     is used rather than <c>Cancel</c> so a cancellation callback does not run inline on the
    ///     caller's thread.
    ///     <para>
    ///         Quiet about <b>callback</b> failures too, and that is the substantive part.
    ///         <c>CancelAsync</c> faults if any registered callback throws, and the writer token's
    ///         registrations are the caller's own code — code that only ran because we asked the
    ///         producer to stop. Letting that surface would make cleanup capable of manufacturing an
    ///         error, which is exactly what the class invariant forbids: it turned a successful RPC
    ///         into an <see cref="AggregateException" /> (PRD-004A review, round 2). Observed and
    ///         dropped here so no path downstream has to remember to.
    ///     </para>
    /// </summary>
    public static async ValueTask CancelQuietlyAsync(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Teardown already ran; whatever this would have stopped is stopping or stopped.
        }
        catch (Exception ex)
        {
            _ = ex;

            // A registered callback threw. Provoked by this cancellation, so never ours to report.
        }
    }

    /// <summary>
    ///     Records that the response side has finished, so a failed producer stops waiting to abort a
    ///     call that is already over.
    /// </summary>
    public void OnResponseEnded() => _responseEnded.TrySetResult();

    /// <summary>
    ///     Starts stopping the producer and returns at once, handing back the task that completes when
    ///     cancellation has finished propagating.
    ///     <para>
    ///         INVARIANT: callers must not await the returned task outside a bound.
    ///         <see cref="CancellationTokenSource.CancelAsync" /> completes only once every registered
    ///         callback has returned, and the writer token is handed to the caller's own
    ///         <c>GetAsyncEnumerator</c> — so a source may register a callback against it and block
    ///         there. Awaiting cancellation ahead of the drain reinstated exactly the caller-visible
    ///         hang this class exists to prevent, by a route <c>MoveNextAsync</c> no longer had
    ///         (PRD-004A review, finding 1).
    ///     </para>
    ///     Idempotent: repeated calls return the first cancellation task rather than starting another.
    /// </summary>
    public Task BeginCancel()
    {
        lock (_cancelGate)
        {
            return _writerCancellation ??= CancelQuietlyAsync(_writerCts).AsTask();
        }
    }

    /// <summary>
    ///     Stops the producer and waits a bounded grace for it to unwind, returning the fault it
    ///     failed with, if any. Bounded because a source can be parked in an operation that ignores
    ///     cancellation — an already-issued console read cannot be recalled — and blocking on that is
    ///     the filed hang. A producer that merely observed cancellation yields <see langword="null" />.
    ///     <para>
    ///         The grace covers cancellation <i>and</i> the pump together, not the pump alone: a
    ///         blocking cancellation callback is as capable of stranding the caller as a blocking
    ///         <c>MoveNextAsync</c>, and putting it ahead of the wait would leave it unbounded.
    ///     </para>
    /// </summary>
    public async ValueTask<ProducerFault?> DrainAsync(TimeSpan grace)
    {
        var cancellation = BeginCancel();

        try
        {
            await Task.WhenAll(cancellation, Completion).WaitAsync(grace, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Still parked, in the source or in its cancellation callback. Whatever either eventually
            // raises is observed by ReleaseWhenIdle.
            return null;
        }
        catch (OperationCanceledException)
        {
            return Fault;
        }
        catch (Exception ex)
        {
            // INVARIANT: attribution comes from the PUMP, never from the cancellation task. The two
            // are awaited together only so a blocking callback cannot escape the bound — joining them
            // is a timing device, not an attribution one. A callback runs solely because we asked the
            // producer to stop, so treating its failure as a producer fault let cleanup replace an
            // already successful RPC (PRD-004A review, round 2). CancelQuietlyAsync now swallows those
            // as well; this is the second half of the same guarantee, so neither alone is load-bearing.
            //
            // WhenAll only faults once every task has completed, so the pump's state is settled here.
            if (!Completion.IsFaulted)
            {
                return Fault;
            }

            // Fault is the attributed value; fall back to the raw task exception for anything the
            // pump itself failed with outside the recorded paths.
            return Fault ?? new ProducerFault((Completion.Exception as Exception)?.InnerException ?? ex, FromWrite: false);
        }

        return Fault;
    }

    /// <summary>
    ///     Releases both token sources, immediately if the producer is finished, otherwise when it
    ///     eventually is — the only moment it is safe to dispose a source whose token something still
    ///     holds. Also observes any fault so it cannot escape as an unobserved task exception.
    ///     <para>
    ///         "Finished" means the pump has exited <i>and</i> any cancellation has finished
    ///         propagating. A callback still running holds the writer token just as the pump does, and
    ///         <see cref="CancellationTokenSource.Dispose()" /> under either is unsafe.
    ///     </para>
    /// </summary>
    public void ReleaseWhenIdle()
    {
        Task pending;

        lock (_cancelGate)
        {
            pending = _writerCancellation is null
                ? Completion
                : Task.WhenAll(Completion, _writerCancellation);
        }

        if (pending.IsCompleted)
        {
            _ = pending.Exception;

            Release();

            return;
        }

        _ = pending.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;

                ((RequestStreamProducer)state!).Release();
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        _writerCts.Dispose();
        _callCts.Dispose();
    }

    private async Task PumpAsync(IAsyncEnumerable<IMessage> requests)
    {
        var writerToken = _writerCts.Token;

        IAsyncEnumerator<IMessage> enumerator;

        try
        {
            enumerator = requests.GetAsyncEnumerator(writerToken);
        }
        catch (OperationCanceledException) when (writerToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Acquisition is the caller's code too, and gets the same causality guard as every other
            // fault path: a source that blocks in GetAsyncEnumerator and reports our teardown as an
            // ordinary exception must not be able to fault a call that has already completed.
            if (writerToken.IsCancellationRequested)
            {
                return;
            }

            await FailAsync(ex, fromWrite: false).ConfigureAwait(false);

            throw;
        }

        try
        {
            while (true)
            {
                IMessage message;

                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    message = enumerator.Current;
                }
                catch (OperationCanceledException) when (writerToken.IsCancellationRequested)
                {
                    // The response side finished, the consumer walked away, or the caller cancelled:
                    // the source was torn down deliberately. Deliberately no half-close — once writes
                    // are moot, closing only perturbs the wire shape the conformance suite's
                    // before_close_send cases observe.
                    return;
                }
                catch (Exception ex)
                {
                    // A source that reports our own cancellation as something other than an
                    // OperationCanceledException lands here. Its failure is a consequence of the
                    // teardown, not a cause of it, so it must never be recorded: doing so would let
                    // cleanup manufacture the error the read side then reports in place of the
                    // status the call actually finished with.
                    if (writerToken.IsCancellationRequested)
                    {
                        return;
                    }

                    // Anything else the source raises is the caller's failure, whatever its type.
                    await FailAsync(ex, fromWrite: false).ConfigureAwait(false);

                    throw;
                }

                try
                {
                    await _requestStream.WriteAsync(message, writerToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsPostCompletionWriteNoise(ex, writerToken))
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (writerToken.IsCancellationRequested)
                    {
                        return;
                    }

                    // A write that failed on its own merits — an oversize message, a marshaller
                    // failure — rather than because the call had already finished.
                    await FailAsync(ex, fromWrite: true).ConfigureAwait(false);

                    throw;
                }
            }

            await HalfCloseAsync(writerToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeEnumeratorAsync(enumerator, writerToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeEnumeratorAsync(IAsyncEnumerator<IMessage> enumerator, CancellationToken writerToken)
    {
        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (writerToken.IsCancellationRequested)
        {
            // Torn down deliberately.
        }
        catch (Exception ex) when (!writerToken.IsCancellationRequested)
        {
            // Disposal is still the caller's code, so its failure is still the caller's fault — but
            // never let it displace one already recorded on the way here, and never record one that
            // our own cancellation provoked.
            await FailAsync(ex, fromWrite: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = ex;

            // Provoked by teardown; observed and discarded.
        }
    }

    /// <summary>
    ///     Records the first fault and then releases the response reader, in that order: the stamp has
    ///     to be visible before anything it provokes can reach the read side, or the read side cannot
    ///     tell cause from consequence.
    /// </summary>
    private async ValueTask FailAsync(Exception fault, bool fromWrite)
    {
        if (Interlocked.CompareExchange(ref _fault, new ProducerFault(fault, fromWrite), null) is not null)
        {
            return;
        }

        if (_faultPolicy is RequestFaultPolicy.HalfCloseThenAbort)
        {
            // Graceful first: request EOF is all a server needs if it finishes on it. Skipped under
            // AbortWithoutHalfClose, where a clean EOF would tell a client-streaming server that the
            // stream it is aggregating ended normally, and it would answer — committing whatever it
            // had. The client already knows the stream is short.
            await HalfCloseAsync(_writerCts.Token).ConfigureAwait(false);
        }

        // Both policies wait, because the wait answers a different question from the half-close: has
        // the server already said something? A status that was in flight when this fault happened is
        // the call's real outcome, and resetting the stream destroys it — which is how an abort-at-once
        // policy turned a server's CANCELLED into the local write shadow (PRD-004A review, finding 2).
        var released = await Task.WhenAny(_responseEnded.Task, Task.Delay(ResponseReleaseGrace)).ConfigureAwait(false);

        if (released == _responseEnded.Task)
        {
            return;
        }

        // The server is under no obligation to finish on request EOF — and under AbortWithoutHalfClose
        // it was never told the stream ended at all — so the caller is owed this fault within a bound.
        // Abort so the outstanding read is released. Flag it first, so the CANCELLED the reader is
        // about to see is already attributable to us by the time it arrives.
        Volatile.Write(ref _abortedCall, 1);

        await CancelQuietlyAsync(_callCts).ConfigureAwait(false);
    }

    private async ValueTask HalfCloseAsync(CancellationToken writerToken)
    {
        try
        {
            await _requestStream.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPostCompletionWriteNoise(ex, writerToken))
        {
            // The half-close raced the call's own completion; immaterial for the same reason.
        }
    }

    /// <summary>
    ///     True when a write-half failure is a consequence of the call already being finished rather
    ///     than a fault of its own. A gRPC call's status is the server's status, delivered on the read
    ///     half; a write that could not land is not an RPC failure (upstream grpcurl likewise discards
    ///     <c>SendMsg</c>'s <c>io.EOF</c> and reports the status from <c>RecvMsg</c>).
    ///     <para>
    ///         Shapes, verified against Grpc.Net.Client 2.76.0: once the call has completed,
    ///         <c>HttpContentClientStreamWriter.WriteAsync</c> hands back
    ///         <c>GrpcCall.CreateCanceledStatusException()</c>, which carries the completed call's own
    ///         status — so a server that finished cleanly yields an <see cref="RpcException" /> whose
    ///         status code is <see cref="StatusCode.OK" />. Half-close after completion gives
    ///         <see cref="InvalidOperationException" /> (<see cref="ObjectDisposedException" />, for a
    ///         disposed call, derives from it), and a torn-down HTTP/2 stream gives
    ///         <see cref="IOException" />.
    ///     </para>
    ///     <para>
    ///         Applied ONLY around the write itself. Every one of these types is also something a
    ///         caller's source can throw, so using it any wider would silently swallow the caller's
    ///         own errors.
    ///     </para>
    /// </summary>
    private static bool IsPostCompletionWriteNoise(Exception exception, CancellationToken writerToken) => exception switch
    {
        OperationCanceledException => true,
        InvalidOperationException => true,
        IOException => true,
        // OK and CANCELLED are both teardown artifacts rather than statuses a write earned:
        // CreateCanceledStatusException hands back the completed call's status when there is one
        // (OK for a server that finished cleanly) and Status(Cancelled) when there is not, i.e. the
        // call went away underneath the write. A write that failed on its own merits — an oversize
        // message giving RESOURCE_EXHAUSTED, a marshaller failure — carries neither.
        RpcException rpc => writerToken.IsCancellationRequested
                            || rpc.StatusCode is StatusCode.OK or StatusCode.Cancelled,
        _ => false
    };
}
