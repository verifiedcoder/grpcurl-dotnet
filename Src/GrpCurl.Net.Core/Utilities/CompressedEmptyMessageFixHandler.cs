using System.Buffers.Binary;
using System.Net;

namespace GrpCurl.Net.Utilities;

/// <summary>
///     Fixes a Grpc.Net.Client wire quirk: when per-call compression is requested, it
///     writes zero-length messages with the length-prefix compressed flag still set but
///     no compressed payload. Strict servers (e.g. grpc-go) fail to decompress the empty
///     body and reject the call. This handler rewrites the 5-byte frame headers of
///     compressed request bodies, clearing the flag on empty messages — which the gRPC
///     wire format explicitly allows per message.
/// </summary>
internal sealed class CompressedEmptyMessageFixHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null
            && request.Headers.TryGetValues("grpc-encoding", out var encodings)
            && !encodings.Contains("identity"))
        {
            request.Content = new FrameFixingContent(request.Content);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class FrameFixingContent : HttpContent
    {
        private readonly HttpContent _inner;

        public FrameFixingContent(HttpContent inner)
        {
            _inner = inner;

            foreach (var header in inner.Headers)
            {
                _ = Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _inner.CopyToAsync(new FrameFixingStream(stream));

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            _inner.CopyToAsync(new FrameFixingStream(stream), cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            // Frame headers are rewritten in place (flag byte only), so the length is
            // whatever the inner content produces; report unknown to keep streaming.
            length = -1;

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///     Write-only stream that parses the gRPC length-prefixed frame structure as it
    ///     passes through and clears the compressed flag on zero-length messages. Holds
    ///     back at most four bytes (an incomplete frame header) between writes.
    /// </summary>
    private sealed class FrameFixingStream(Stream inner) : Stream
    {
        private readonly byte[] _header = new byte[5];
        private int _headerFilled;
        private long _payloadRemaining;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (!buffer.IsEmpty)
            {
                if (_payloadRemaining > 0)
                {
                    var take = (int)Math.Min(_payloadRemaining, buffer.Length);

                    await inner.WriteAsync(buffer[..take], cancellationToken).ConfigureAwait(false);

                    _payloadRemaining -= take;
                    buffer = buffer[take..];

                    continue;
                }

                var copy = Math.Min(5 - _headerFilled, buffer.Length);

                buffer.Span[..copy].CopyTo(_header.AsSpan(_headerFilled));

                _headerFilled += copy;
                buffer = buffer[copy..];

                if (_headerFilled < 5)
                {
                    // Incomplete frame header — wait for the next write.
                    break;
                }

                var messageLength = BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(1));

                if (_header[0] == 1 && messageLength == 0)
                {
                    _header[0] = 0;
                }

                await inner.WriteAsync(_header, cancellationToken).ConfigureAwait(false);

                _headerFilled = 0;
                _payloadRemaining = messageLength;
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        // The transport stream is owned by the HTTP stack; nothing to dispose here.
    }
}
