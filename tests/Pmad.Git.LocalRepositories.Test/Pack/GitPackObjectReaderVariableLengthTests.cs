using Pmad.Git.LocalRepositories.Pack;

namespace Pmad.Git.LocalRepositories.Test.Pack;

/// <summary>
/// Tests for ReadVariableLengthBytesAsync focusing on edge cases and partial read scenarios.
/// </summary>
public sealed class GitPackObjectReaderVariableLengthTests
{
    #region ReadVariableLengthBytesAsync Edge Cases

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithSingleByte_ShouldReadCorrectly()
    {
        // Arrange: Single byte without continuation bit
        var data = new byte[] { 0x42 }; // 01000010
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(1, bytesRead);
        Assert.Equal(0x42, buffer[0]);
        Assert.Equal(1, stream.Position); // Stream should be positioned after the byte
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithTwoBytes_ShouldReadCorrectly()
    {
        // Arrange: Two bytes, first has continuation bit
        var data = new byte[] { 0x80, 0x01 }; // First byte: continue, Second: end
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(2, bytesRead);
        Assert.Equal(0x80, buffer[0]);
        Assert.Equal(0x01, buffer[1]);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithMaximumBytes_ShouldReadCorrectly()
    {
        // Arrange: 10 bytes, all with continuation bit except last
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F };
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(10, bytesRead);
        Assert.Equal(10, stream.Position);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_ExceedingMaximum_ShouldThrowInvalidDataException()
    {
        // Arrange: 11 bytes, all with continuation bit (invalid)
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None));

        Assert.Contains("Variable length encoding exceeds maximum size", exception.Message);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithEmptyStream_ShouldThrowEndOfStreamException()
    {
        // Arrange
        var stream = new MemoryStream(Array.Empty<byte>());
        var buffer = new byte[10];

        // Act & Assert
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None));
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithExtraDataAfterEncoding_ShouldSeekBackCorrectly()
    {
        // Arrange: Variable length encoding followed by extra data
        var data = new byte[] { 0x42, 0xFF, 0xFF, 0xFF }; // First byte ends encoding, rest is extra
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(1, bytesRead);
        Assert.Equal(1, stream.Position); // Should be positioned after only the first byte
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithPartialInitialRead_ShouldContinueReading()
    {
        // Arrange: Stream that returns partial data on first read
        var data = new byte[] { 0x80, 0x80, 0x01 }; // 3-byte encoding
        var stream = new PartialReadStream(data, 1); // Returns 1 byte at a time
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(0x80, buffer[0]);
        Assert.Equal(0x80, buffer[1]);
        Assert.Equal(0x01, buffer[2]);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithMultiplePartialReads_ShouldHandleCorrectly()
    {
        // Arrange: Stream that returns 2 bytes at a time
        var data = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x01 }; // 5-byte encoding
        var stream = new PartialReadStream(data, 2); // Returns 2 bytes at a time
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(5, bytesRead);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(0x80, buffer[i]);
        }
        Assert.Equal(0x01, buffer[4]);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_EndingMidPartialRead_ShouldSeekBackCorrectly()
    {
        // Arrange: Encoding ends in middle of a partial read, with extra data
        var data = new byte[] { 0x80, 0x80, 0x01, 0xAA, 0xBB }; // 3-byte encoding, 2 extra bytes
        var stream = new PartialReadStream(data, 4); // Returns 4 bytes on first read
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(3, stream.Position); // Should seek back to skip the extra bytes
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithStreamThatReturnsZeroMidway_ShouldThrowEndOfStreamException()
    {
        // Arrange: Stream that returns data then EOF
        var data = new byte[] { 0x80, 0x80 }; // Incomplete encoding
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act & Assert
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None));
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithSmallBuffer_ShouldRespectBufferSize()
    {
        // Arrange: Buffer smaller than max 10 bytes
        var data = new byte[] { 0x80, 0x80, 0x01 };
        var stream = new MemoryStream(data);
        var buffer = new byte[5]; // Small buffer

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(0x80, buffer[0]);
        Assert.Equal(0x80, buffer[1]);
        Assert.Equal(0x01, buffer[2]);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithRealWorldTypeAndSize_ShouldHandleCorrectly()
    {
        // Arrange: Real Git pack object header - Commit type (1), size 1500
        // Type=1, Size=1500 requires multi-byte encoding
        // First byte: type in bits 4-6, size low 4 bits, continuation bit
        var data = new byte[] { 0x9C, 0x0B }; // Actual encoding from Git
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act
        var bytesRead = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert
        Assert.Equal(2, bytesRead);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_ConsecutiveCalls_ShouldNotInterfere()
    {
        // Arrange: Multiple variable-length values in sequence
        var data = new byte[] { 0x42, 0x80, 0x01, 0x7F };
        var stream = new MemoryStream(data);
        var buffer = new byte[10];

        // Act 1: Read first value (single byte)
        var bytesRead1 = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert 1
        Assert.Equal(1, bytesRead1);
        Assert.Equal(0x42, buffer[0]);
        Assert.Equal(1, stream.Position);

        // Act 2: Read second value (two bytes)
        var bytesRead2 = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert 2
        Assert.Equal(2, bytesRead2);
        Assert.Equal(0x80, buffer[0]);
        Assert.Equal(0x01, buffer[1]);
        Assert.Equal(3, stream.Position);

        // Act 3: Read third value (single byte)
        var bytesRead3 = await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, CancellationToken.None);

        // Assert 3
        Assert.Equal(1, bytesRead3);
        Assert.Equal(0x7F, buffer[0]);
        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public async Task ReadVariableLengthBytesAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var data = new byte[] { 0x80, 0x80, 0x01 };
        var stream = new MemoryStream(data);
        var buffer = new byte[10];
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await GitPackObjectReader.ReadVariableLengthBytesAsync(stream, buffer, cts.Token));
    }

    #endregion

    #region Integration Tests with ReadTypeAndSizeAsync

    [Fact]
    public async Task ReadTypeAndSizeAsync_WithPartialReadStream_ShouldWorkCorrectly()
    {
        // Arrange: Type=3 (blob), Size=1000, stream returns 1 byte at a time
        var data = new byte[] { 0xB8, 0x07 }; // Multi-byte encoding
        var stream = new PartialReadStream(data, 1);

        // Act
        var (kind, size) = await GitPackObjectReader.ReadTypeAndSizeAsync(stream, CancellationToken.None);

        // Assert
        Assert.Equal(3, kind);
        Assert.True(size > 0);
    }

    [Fact]
    public async Task ReadOfsDeltaOffsetAsync_WithPartialReadStream_ShouldWorkCorrectly()
    {
        // Arrange: Offset encoding with partial reads
        var data = new byte[] { 0x80, 0x01 };
        var stream = new PartialReadStream(data, 1);

        // Act
        var offset = await GitPackObjectReader.ReadOfsDeltaOffsetAsync(stream, CancellationToken.None);

        // Assert
        Assert.True(offset > 0);
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Stream that simulates partial reads by returning a limited number of bytes per read.
    /// </summary>
    private class PartialReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _maxBytesPerRead;
        private int _position;

        public PartialReadStream(byte[] data, int maxBytesPerRead)
        {
            _data = data;
            _maxBytesPerRead = maxBytesPerRead;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => _position = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesToRead = Math.Min(count, Math.Min(_maxBytesPerRead, _data.Length - _position));
            if (bytesToRead <= 0)
                return 0;

            Array.Copy(_data, _position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesToRead = Math.Min(buffer.Length, Math.Min(_maxBytesPerRead, _data.Length - _position));
            if (bytesToRead <= 0)
                return ValueTask.FromResult(0);

            _data.AsSpan(_position, bytesToRead).CopyTo(buffer.Span);
            _position += bytesToRead;
            return ValueTask.FromResult(bytesToRead);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _position = (int)offset;
                    break;
                case SeekOrigin.Current:
                    _position += (int)offset;
                    break;
                case SeekOrigin.End:
                    _position = _data.Length + (int)offset;
                    break;
            }
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
