using System.Security.Cryptography;

namespace Pmad.Git.HttpServer.Utilities;

internal sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash; 
    private readonly bool _leaveOpen;

    private bool _hashCompleted;

    public HashingReadStream(Stream inner, HashAlgorithmName algorithm, bool leaveOpen = false)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _hash = IncrementalHash.CreateHash(algorithm);
        _leaveOpen = leaveOpen;
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

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public async override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0 && !_hashCompleted)
        {
            _hash.AppendData(buffer.Span[..read]);
            BytesRead += read;
        }
        return read;
    }

    public byte[] CompleteHash()
    {
        if (_hashCompleted)
        {
            throw new InvalidOperationException("Hash already finalized");
        }
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
            if (!_leaveOpen)
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
