namespace GrpCurl.Net.Utilities;

/// <summary>
///     Enforces the gRPC spec's trailers-only rule on responses. Grpc.Net.Client treats
///     any response whose HEADERS frame carries <c>grpc-status</c> as trailers-only, even
///     when a body or a real trailers block follows — which lets a broken server's header
///     status mask the authoritative one. When <c>grpc-status</c> appears in the response
///     headers, this handler peeks the body: if data follows, or the (empty) body is
///     followed by trailers that themselves carry <c>grpc-status</c>, the header status is
///     removed so normal message/trailer processing decides the outcome. Genuine
///     trailers-only responses (empty body, no status-bearing trailers) pass through
///     untouched.
/// </summary>
internal sealed class TrailersOnlyGuardHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private const string GrpcStatusHeader = "grpc-status";
    private const string GrpcMessageHeader = "grpc-message";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.Headers.Contains(GrpcStatusHeader))
        {
            return response;
        }

        var originalContent = response.Content;
        var body = await originalContent.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var firstByte = new byte[1];
        var read = await body.ReadAsync(firstByte.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);

        if (read == 0)
        {
            // Empty body; the stream hit EOF, so trailing headers (if any) are populated.
            // A grpc-status there is the authoritative one — drop the header copy.
            if (response.TrailingHeaders.Contains(GrpcStatusHeader))
            {
                StripHeaderStatus(response);
            }

            return response;
        }

        // A body alongside grpc-status in headers is not a trailers-only response.
        StripHeaderStatus(response);

        var replayContent = new StreamContent(new PushbackStream(firstByte[0], body));

        foreach (var header in originalContent.Headers)
        {
            replayContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = replayContent;

        return response;
    }

    private static void StripHeaderStatus(HttpResponseMessage response)
    {
        response.Headers.Remove(GrpcStatusHeader);
        response.Headers.Remove(GrpcMessageHeader);
    }

    /// <summary>
    ///     Read-only stream that replays one already-consumed byte ahead of the inner stream.
    /// </summary>
    private sealed class PushbackStream(byte first, Stream inner) : Stream
    {
        private bool _firstConsumed;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (!_firstConsumed)
            {
                if (buffer.IsEmpty)
                {
                    return 0;
                }

                _firstConsumed = true;
                buffer[0] = first;

                return 1;
            }

            return inner.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_firstConsumed)
            {
                if (buffer.IsEmpty)
                {
                    return ValueTask.FromResult(0);
                }

                _firstConsumed = true;
                buffer.Span[0] = first;

                return ValueTask.FromResult(1);
            }

            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
