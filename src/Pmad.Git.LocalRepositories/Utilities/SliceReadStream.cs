namespace Pmad.Git.LocalRepositories.Utilities;

/// <summary>
/// A read-only stream wrapper that provides a limited view of an underlying stream.
/// This stream restricts access to a specific portion (slice) of the inner stream,
/// starting from the current position and spanning a specified length.
/// </summary>
internal class SliceReadStream : Stream
{
    private readonly long _length;
    private readonly long _offset;
    private readonly Stream _inner;
    private readonly bool _leaveOpen;

    private long _position;

    /// <summary>
    /// Initializes a new instance of the <see cref="SliceReadStream"/> class.
    /// </summary>
    /// <param name="inner">The underlying stream to read from. Must be readable.</param>
    /// <param name="length">The maximum number of bytes that can be read from this stream.</param>
    /// <param name="leaveOpen">If <c>true</c>, the underlying stream will not be disposed when this stream is disposed; otherwise, <c>false</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="inner"/> is not readable, or the specified <paramref name="length"/> exceeds the available length of the inner stream.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    public SliceReadStream(Stream inner, long length, bool leaveOpen = false) 
    {
        if (inner == null)
        {
            throw new ArgumentNullException(nameof(inner));
        }
        if (!inner.CanRead)
        {
            throw new ArgumentException("Inner stream must be readable", nameof(inner));
        }
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative");
        }
        if (inner.CanSeek)
        {
            _offset = inner.Position;
            if (_offset + length > inner.Length)
            {
                throw new ArgumentException("Length exceed the length of the inner stream");
            }
        }
        _inner = inner; 
        _length = length; 
        _leaveOpen = leaveOpen; 
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => _inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _length;

    /// <inheritdoc />
    public override long Position 
    { 
        get => _position; 
        set
        {
            if (!_inner.CanSeek)
            {
                throw new NotSupportedException();
            }
            if ( value < 0 || value > _length) 
            { 
                throw new ArgumentOutOfRangeException(nameof(value), "Position must be within the length of the stream"); 
            }
            _position = value; 
            _inner.Position = _offset + value;
        }
    }

    /// <inheritdoc />
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesToRead = (int)Math.Min(count, _length - _position);
        if (bytesToRead == 0)
        {
            return 0;
        }
        var read = _inner.Read(buffer, offset, bytesToRead);
        _position += read; 
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int bytesToRead = (int)Math.Min(buffer.Length, _length - _position);
        if (bytesToRead == 0)
        {
            return 0;
        }
        var read = await _inner.ReadAsync(buffer.Slice(0, bytesToRead), cancellationToken);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!_inner.CanSeek)
        {
            throw new NotSupportedException();
        }
        switch(origin)
        { 
            case SeekOrigin.Begin: 
                Position = offset; 
                break; 
            case SeekOrigin.Current: 
                Position += offset; 
                break; 
            case SeekOrigin.End: 
                Position = _length + offset; 
                break; 
            default: 
                throw new ArgumentOutOfRangeException(nameof(origin), "Invalid seek origin"); 
        }
        return Position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_leaveOpen)
            {
                _inner.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
