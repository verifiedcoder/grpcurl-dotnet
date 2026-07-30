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
///     Owns the request half of a bidi call (PRD-003): the task pumping the caller's request source
///     into the request stream, the token sources that stop it, and the fault it failed with.
///     <para>
///         Two rules drive the whole design.
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
internal sealed class DuplexRequestProducer
{
    /// <summary>
    ///     How long a failed producer waits for the server to end the response stream on its own
    ///     after the half-close, before aborting the call.
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
    private readonly IClientStreamWriter<IMessage> _requestStream;

    private readonly TaskCompletionSource _responseEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _writerCts;

    private ProducerFault? _fault;
    private int _released;

    private DuplexRequestProducer(IClientStreamWriter<IMessage> requestStream, CancellationTokenSource callCts)
    {
        _requestStream = requestStream;
        _callCts = callCts;
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
    ///     Starts pumping <paramref name="requests" /> into <paramref name="requestStream" />. Takes
    ///     ownership of <paramref name="callCts" />, whose token must already be the call's own, so a
    ///     failed producer can abort the call to release a blocked reader.
    /// </summary>
    public static DuplexRequestProducer Start(
        IAsyncEnumerable<IMessage> requests,
        IClientStreamWriter<IMessage> requestStream,
        CancellationTokenSource callCts)
    {
        var producer = new DuplexRequestProducer(requestStream, callCts);

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
    }

    /// <summary>
    ///     Records that the response side has finished, so a failed producer stops waiting to abort a
    ///     call that is already over.
    /// </summary>
    public void OnResponseEnded() => _responseEnded.TrySetResult();

    /// <summary>Stops the producer without touching the call itself.</summary>
    public ValueTask CancelAsync() => CancelQuietlyAsync(_writerCts);

    /// <summary>
    ///     Stops the producer and waits a bounded grace for it to unwind, returning the fault it
    ///     failed with, if any. Bounded because a source can be parked in an operation that ignores
    ///     cancellation — an already-issued console read cannot be recalled — and blocking on that is
    ///     the filed hang. A producer that merely observed cancellation yields <see langword="null" />.
    /// </summary>
    public async ValueTask<ProducerFault?> DrainAsync(TimeSpan grace)
    {
        await CancelAsync().ConfigureAwait(false);

        try
        {
            await Completion.WaitAsync(grace, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Still parked. Its fault, if it ever raises one, is observed by ReleaseWhenIdle.
            return null;
        }
        catch (OperationCanceledException)
        {
            return Fault;
        }
        catch (Exception ex)
        {
            // Fault is the attributed value; fall back to the raw task exception for anything the
            // pump itself failed with outside the recorded paths.
            return Fault ?? new ProducerFault(ex, FromWrite: false);
        }

        return Fault;
    }

    /// <summary>
    ///     Releases both token sources, immediately if the pump has finished, otherwise when it
    ///     eventually does — the only moment it is safe to dispose a source whose token it still
    ///     holds. Also observes any fault so it cannot escape as an unobserved task exception.
    /// </summary>
    public void ReleaseWhenIdle()
    {
        if (Completion.IsCompleted)
        {
            _ = Completion.Exception;

            Release();

            return;
        }

        _ = Completion.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;

                ((DuplexRequestProducer)state!).Release();
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

        // Graceful first: request EOF is all a server needs if it finishes on it.
        await HalfCloseAsync(_writerCts.Token).ConfigureAwait(false);

        var released = await Task.WhenAny(_responseEnded.Task, Task.Delay(ResponseReleaseGrace)).ConfigureAwait(false);

        if (released == _responseEnded.Task)
        {
            return;
        }

        // The server is under no obligation to finish on request EOF, and the caller is owed this
        // fault within a bound. Abort so the outstanding read is released.
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
