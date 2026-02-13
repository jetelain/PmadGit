using System.Buffers;

namespace Pmad.Git.LocalRepositories.Utilities;

/// <summary>
/// Provides a read-only, stream wrapper that supports efficient asynchronous reading with
/// delimiter-based operations.
/// </summary>
/// <remarks>EfficientAsyncReadStream is designed for scenarios where reading up to a specific delimiter is
/// required, such as parsing protocol messages or records from a continuous stream. The stream is read-only and does
/// not support writing. Seeking is supported when the underlying stream supports seeking; in that case, seek and
/// position operations are delegated to the underlying stream, and internal buffering is managed accordingly to
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
        if (count == 0)
        {
            // Per Stream.Read contract, a zero-length read should be a no-op
            return 0;
        }
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
        using var chunkBuffer = new MemoryStream(BufferSize);

        // First, scan the internal buffer if it has data
        if (!_bufferExhausted && TryReadUntilFromInternalBuffer(chunkBuffer, delimiter))
        {
            return chunkBuffer.ToArray();
        }

        // If delimiter not found in buffer, read from underlying stream
        bool foundDelimiter = false;
        var readBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int remainingStart = 0;
            int remainingLength = 0;

            int bytesRead;
            while ((bytesRead = await _inner.ReadAsync(readBuffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                int delimiterIndex = readBuffer.AsSpan(0, bytesRead).IndexOf(delimiter);

                if (delimiterIndex >= 0)
                {
                    // Write all bytes before the delimiter in one operation
                    if (delimiterIndex > 0)
                    {
                        chunkBuffer.Write(readBuffer, 0, delimiterIndex);
                    }

                    foundDelimiter = true;
                    remainingStart = delimiterIndex + 1;
                    remainingLength = bytesRead - remainingStart;
                    break;
                }
                else
                {
                    // No delimiter in this chunk, write all bytes to output
                    chunkBuffer.Write(readBuffer, 0, bytesRead);
                }
            }

            // Put any remaining bytes back into the internal buffer
            if (remainingLength > 0)
            {
                var initialBufferPosition = _buffer.Position;
                _buffer.Position = _buffer.Length;
                _buffer.Write(readBuffer, remainingStart, remainingLength);
                _buffer.Position = initialBufferPosition;
                _bufferExhausted = false;
            }
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

    private bool TryReadUntilFromInternalBuffer(MemoryStream chunkBuffer, byte delimiter)
    {
        var bufferPos = (int)_buffer.Position;
        var bufferLen = (int)_buffer.Length;

        var bufferSpan = _buffer.GetBuffer().AsSpan(bufferPos, bufferLen - bufferPos);

        var delimiterIndexInBuffer = bufferSpan.IndexOf(delimiter);

        if (delimiterIndexInBuffer >= 0)
        {
            if (delimiterIndexInBuffer > 0)
            {
                chunkBuffer.Write(bufferSpan.Slice(0, delimiterIndexInBuffer));
            }

            // Move past the delimiter
            _buffer.Position = bufferPos + delimiterIndexInBuffer + 1; 

            if (_buffer.Position == _buffer.Length)
            {
                MarkBufferExhausted();
            }
            return true;
        }

        // Delimiter not in buffer, copy all remaining buffer data to output
        chunkBuffer.Write(bufferSpan);

        MarkBufferExhausted();

        return false;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            // Per Stream.ReadAsync contract, a zero-length read should be a no-op
            return 0;
        }
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
