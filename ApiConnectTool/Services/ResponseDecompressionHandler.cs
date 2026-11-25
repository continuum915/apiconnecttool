using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ApiConnectTool.Services;

// Separate response decoding from request headers: native AutomaticDecompression also appends
// Accept-Encoding values, which would override the user's explicitly selected encodings.
internal sealed class ResponseDecompressionHandler(HttpMessageHandler transport) : DelegatingHandler(transport)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var original = response.Content;
        var encoding = original.Headers.ContentEncoding.LastOrDefault()?.ToLowerInvariant();
        if (encoding is not ("gzip" or "deflate" or "br")) return response;
        try
        {
            var stream = await original.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            Stream decoded = encoding switch
            {
                "gzip" => new GZipStream(stream, CompressionMode.Decompress),
                "deflate" => await CreateDeflateStreamAsync(stream, cancellationToken).ConfigureAwait(false),
                _ => new BrotliStream(stream, CompressionMode.Decompress)
            };
            response.Content = new DecodedContent(decoded, original);
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    private static async Task<Stream> CreateDeflateStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var first = new byte[1];
        var count = await stream.ReadAsync(first, cancellationToken).ConfigureAwait(false);
        var prefixed = new PrefixReadStream(stream, count == 1 ? first[0] : null);
        // HTTP deflate is zlib-wrapped. Also accept raw deflate, as .NET's native decoder does.
        return count == 1 && (first[0] & 0x0F) == 8
            ? new ZLibStream(prefixed, CompressionMode.Decompress)
            : new DeflateStream(prefixed, CompressionMode.Decompress);
    }

    private sealed class PrefixReadStream(Stream stream, byte? prefix) : Stream
    {
        private byte? _prefix = prefix;
        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty) return 0;
            if (_prefix is byte value) { buffer[0] = value; _prefix = null; return 1; }
            return stream.Read(buffer);
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.IsEmpty) return ValueTask.FromResult(0);
            if (_prefix is byte value) { buffer.Span[0] = value; _prefix = null; return ValueTask.FromResult(1); }
            return stream.ReadAsync(buffer, cancellationToken);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) stream.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class DecodedContent : StreamContent
    {
        private readonly HttpContent _original;
        public DecodedContent(Stream stream, HttpContent original) : base(stream)
        {
            _original = original;
            foreach (var header in original.Headers)
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && !header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var encoding in original.Headers.ContentEncoding.SkipLast(1)) Headers.ContentEncoding.Add(encoding);
        }
        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally { if (disposing) _original.Dispose(); }
        }
    }
}
