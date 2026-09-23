namespace Pmad.Git.RemoteClient;

/// <summary>
/// A stream that sequentially concatenates two streams without buffering their contents in memory.
/// </summary>
internal sealed class ConcatenatedStream : Stream
{
    private readonly Stream _first;
    private readonly Stream _second;
    private readonly bool _leaveSecondOpen;
    private bool _readingSecond;

    public ConcatenatedStream(Stream first, Stream second, bool leaveSecondOpen = false)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
        _leaveSecondOpen = leaveSecondOpen;
    }

    public override bool CanRead => _first.CanRead && _second.CanRead;
    public override bool CanSeek => _first.CanSeek && _second.CanSeek;
    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            if (!CanSeek)
            {
                throw new NotSupportedException("Stream does not support seeking.");
            }
            return _first.Length + _second.Length;
        }
    }

    public override long Position
    {
        get
        {
            if (!CanSeek)
            {
                throw new NotSupportedException("Stream does not support seeking.");
            }
            return _readingSecond ? _first.Length + _second.Position : _first.Position;
        }
        set
        {
            if (!CanSeek)
            {
                throw new NotSupportedException("Stream does not support seeking.");
            }
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Length);

            if (value <= _first.Length)
            {
                _first.Position = value;
                _second.Position = 0;
                _readingSecond = false;
            }
            else
            {
                _first.Position = _first.Length;
                _second.Position = value - _first.Length;
                _readingSecond = true;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (!_readingSecond)
        {
            var read = _first.Read(buffer);
            if (read > 0)
            {
                return read;
            }
            _readingSecond = true;
        }

        return _second.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (!_readingSecond)
        {
            var read = await _first.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                return read;
            }
            _readingSecond = true;
        }

        return await _second.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!CanSeek)
        {
            throw new NotSupportedException("Stream does not support seeking.");
        }

        long targetPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        Position = targetPosition;
        return Position;
    }

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _first.Dispose();
            if (!_leaveSecondOpen)
            {
                _second.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _first.DisposeAsync().ConfigureAwait(false);
        if (!_leaveSecondOpen)
        {
            await _second.DisposeAsync().ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

