using System;
using System.Buffers;

namespace Pmad.Git.LocalRepositories.Utilities;

/// <summary>
/// Provides a read-only, stream wrapper that supports efficient asynchronous reading with
/// delimiter-based operations.
/// </summary>
/// <remarks>SmartReadAsyncStream is designed for scenarios where reading up to a specific delimiter is
/// required, such as parsing protocol messages or records from a continuous stream. The stream does not support
/// seeking or writing. All read operations are performed on the underlying stream, with internal buffering to
/// optimize delimiter-based reads. The stream and its underlying resources are disposed when the instance is
/// disposed. This type is not thread-safe; callers should ensure appropriate synchronization if accessed
/// concurrently.</remarks>
internal sealed class EfficientAsyncReadStream : Stream
{
    private const int BufferSize = 128;
    private readonly MemoryStream _buffer;
    private readonly Stream _inner;
    private bool _bufferExhausted;

    /// <summary>
    /// Initializes a new instance of the EfficientAsyncReadStream class that wraps the specified underlying stream for
    /// efficient asynchronous reading.
    /// </summary>
    /// <param name="inner">The underlying stream to read from.</param>
    public EfficientAsyncReadStream(Stream inner)
    {
        _buffer = new MemoryStream(BufferSize);
        _inner = inner;
        _bufferExhausted = true;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => _inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _inner.Position - _buffer.Length + _buffer.Position;
        set 
        { 
            _inner.Position = value;
            MarkBufferExhausted();
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!_bufferExhausted)
        {
            var bytesRead = _buffer.Read(buffer, offset, count);
            if (bytesRead > 0)
            {
                return bytesRead;
            }
            MarkBufferExhausted();
        }
        return _inner.Read(buffer, offset, count);
    }

    /// <summary>
    /// Marks the internal buffer as exhausted and resets its state.
    /// </summary>
    private void MarkBufferExhausted()
    {
        _bufferExhausted = true;
        _buffer.Position = 0;
        _buffer.SetLength(0);
    }

    /// <summary>
    /// Asynchronously reads and buffers up to the specified number of bytes from the underlying stream.
    /// This allows to use synchronous reads from the stream up to the preloaded byte count without I/O operations.
    /// </summary>
    /// <param name="byteCount">The maximum number of bytes to read and buffer. Must be non-negative.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the preload operation.</param>
    /// <returns>A task that represents the asynchronous preload operation.</returns>
    public async Task PreLoadAsync(int byteCount, CancellationToken cancellationToken = default)
    {
        var initalBufferPosition = _buffer.Position;
        _buffer.Position = _buffer.Length; 

        var readBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int totalBytesRead = 0;
            while (totalBytesRead < byteCount)
            {
                int bytesToRead = Math.Min(BufferSize, byteCount - totalBytesRead);
                int bytesRead = await _inner.ReadAsync(readBuffer.AsMemory(0, bytesToRead), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break; // End of stream reached
                }
                _buffer.Write(readBuffer, 0, bytesRead);
                totalBytesRead += bytesRead;
            }

            // Make preloaded data available for subsequent reads
            _buffer.Position = initalBufferPosition;
            _bufferExhausted = false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }


    /// <summary>
    /// Asynchronously reads bytes from the stream until the specified delimiter is encountered, returning the data
    /// read up to but not including the delimiter.
    /// </summary>
    /// <remarks>The method advances the stream position past the delimiter. If the delimiter is not
    /// present in the stream, no data is returned and an exception is thrown. The operation is performed
    /// asynchronously and may complete before all requested data is available if the delimiter is encountered
    /// early.</remarks>
    /// <param name="delimiter">The byte value that marks the end of the data to read. Reading stops when this delimiter is found.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the read operation.</param>
    /// <returns>A byte array containing the data read from the stream, excluding the delimiter. The array will be empty if
    /// the delimiter is the first byte read.</returns>
    /// <exception cref="EndOfStreamException">Thrown if the end of the stream is reached before the delimiter is found.</exception>
    public async ValueTask<byte[]> ReadUntilAsync(byte delimiter, CancellationToken cancellationToken = default)
    {
        bool foundDelimiter = false;

        using var chunkBuffer = new MemoryStream();
        var readBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            int remainingStart = 0;
            int remainingLength = 0;

            while ((bytesRead = await ReadAsync(readBuffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    if (readBuffer[i] == delimiter)
                    {
                        foundDelimiter = true;
                        remainingStart = i + 1;
                        remainingLength = bytesRead - remainingStart;
                        break;
                    }
                    chunkBuffer.WriteByte(readBuffer[i]);
                }

                if (foundDelimiter)
                {
                    break;
                }
            }

            var initalBufferPosition = _buffer.Position;
            _buffer.Position = _buffer.Length;
            _buffer.Write(readBuffer, remainingStart, remainingLength);
            _buffer.Position = initalBufferPosition;
            _bufferExhausted = false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
        if (!foundDelimiter)
        {
            throw new EndOfStreamException("Delimiter not found before end of stream.");
        }
        return chunkBuffer.ToArray();
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_bufferExhausted)
        {
            var bytesRead = await _buffer.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead > 0)
            {
                return bytesRead;
            }
            MarkBufferExhausted();
        }
        return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) 
    {
        if (!CanSeek)
        {
            throw new NotSupportedException();
        }
        switch (origin)
        {
            case SeekOrigin.Begin:
                Position = offset;
                break;
            case SeekOrigin.Current:
                Position += offset;
                break;
            case SeekOrigin.End:
                Position = Length + offset;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin), origin, null);
        }
        return Position;
    }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.Dispose();
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _buffer.DisposeAsync().ConfigureAwait(false);
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
