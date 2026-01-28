using System.Buffers;
using System.Security.Cryptography;

namespace Pmad.Git.HttpServer.Utilities;

internal sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;
    private bool _hashCompleted;

    public HashingReadStream(Stream inner, HashAlgorithmName algorithm)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _hash = IncrementalHash.CreateHash(algorithm);
    }

    public long BytesRead { get; private set; }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0 && !_hashCompleted)
        {
            _hash.AppendData(buffer, offset, read);
            BytesRead += read;
        }

        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if (read > 0 && !_hashCompleted)
        {
            _hash.AppendData(buffer, offset, read);
            BytesRead += read;
        }

        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => ReadAsync(buffer, cancellationToken, appendHash: !_hashCompleted);

    private async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken, bool appendHash)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0 && appendHash)
        {
            _hash.AppendData(buffer.Span[..read]);
            BytesRead += read;
        }

        return read;
    }

    public async Task ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        await _inner.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (!_hashCompleted)
        {
            _hash.AppendData(buffer);
            BytesRead += buffer.Length;
        }
    }

    public new async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await _inner.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (!_hashCompleted)
        {
            _hash.AppendData(buffer.Span);
            BytesRead += buffer.Length;
        }
    }

    public async Task<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            var read = await _inner.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return -1;
            }

            if (!_hashCompleted)
            {
                _hash.AppendData(buffer, 0, 1);
                BytesRead += 1;
            }

            return buffer[0];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public byte[] CompleteHash()
    {
        _hashCompleted = true;
        return _hash.GetHashAndReset();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
