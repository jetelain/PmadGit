using System.Text;
using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories.Test.Utilities;

public sealed class SliceReadStreamTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidStream_ShouldInitialize()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

        // Act
        using var stream = new SliceReadStream(innerStream, 3);

        // Assert
        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
        Assert.Equal(3, stream.Length);
    }

    [Fact]
    public void Constructor_WithNullStream_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SliceReadStream(null!, 10));
    }

    [Fact]
    public void Constructor_WithNonReadableStream_ShouldThrowArgumentException()
    {
        // Arrange
        using var innerStream = new NonReadableStream();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new SliceReadStream(innerStream, 10));
        Assert.Contains("readable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithNegativeLength_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliceReadStream(innerStream, -1));
    }

    [Fact]
    public void Constructor_WithLengthExceedingInnerStream_ShouldThrowArgumentException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new SliceReadStream(innerStream, 10));
        Assert.Contains("exceed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithZeroLength_ShouldSucceed()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        using var stream = new SliceReadStream(innerStream, 0);

        // Assert
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Constructor_WithLeaveOpenTrue_ShouldNotDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new SliceReadStream(innerStream, 2, leaveOpen: true);

        // Act
        stream.Dispose();

        // Assert - Should still be able to read from inner stream
        Assert.Equal(0, innerStream.Position);
        var result = innerStream.ReadByte();
        Assert.Equal(1, result);
    }

    [Fact]
    public void Constructor_WithLeaveOpenFalse_ShouldDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new SliceReadStream(innerStream, 2, leaveOpen: false);

        // Act
        stream.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    [Fact]
    public void Constructor_WithInnerStreamAtNonZeroPosition_ShouldUseCurrentPosition()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        innerStream.Position = 2; // Start at byte 3

        // Act
        using var stream = new SliceReadStream(innerStream, 2);

        // Assert
        var buffer = new byte[2];
        var bytesRead = stream.Read(buffer, 0, 2);
        Assert.Equal(2, bytesRead);
        Assert.Equal(new byte[] { 3, 4 }, buffer);
    }

    #endregion

    #region Stream Properties Tests

    [Fact]
    public void CanRead_ShouldReturnTrue()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void CanWrite_ShouldReturnFalse()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.False(stream.CanWrite);
    }

    [Fact]
    public void CanSeek_ShouldReturnTrueForSeekableStream()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.True(stream.CanSeek);
    }

    [Fact]
    public void CanSeek_ShouldReturnFalseForNonSeekableStream()
    {
        // Arrange
        using var innerStream = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void Length_ShouldReturnSliceLength()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new SliceReadStream(innerStream, 3);

        // Act & Assert
        Assert.Equal(3, stream.Length);
    }

    [Fact]
    public void Position_ShouldReturnCorrectPositionInitially()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new SliceReadStream(innerStream, 3);

        // Act & Assert
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Position_ShouldUpdateAfterRead()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[2];

        // Act
        stream.Read(buffer, 0, 2);

        // Assert
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void Position_Set_ShouldUpdatePosition()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new SliceReadStream(innerStream, 3);

        // Act
        stream.Position = 2;

        // Assert
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void Position_Set_WithNonSeekableStream_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.Position = 1);
    }

    [Fact]
    public void Position_Set_WithNegativeValue_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new SliceReadStream(innerStream, 3);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
    }

    [Fact]
    public void Position_Set_BeyondLength_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new SliceReadStream(innerStream, 3);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = 4);
    }

    [Fact]
    public void Position_Set_AtLength_ShouldSucceed()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new SliceReadStream(innerStream, 3);

        // Act
        stream.Position = 3;

        // Assert
        Assert.Equal(3, stream.Position);
    }

    #endregion

    #region Read Tests

    [Fact]
    public void Read_WithValidBuffer_ShouldReadData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[3];

        // Act
        var bytesRead = stream.Read(buffer, 0, 3);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
    }

    [Fact]
    public void Read_RequestingMoreThanSliceLength_ShouldReadOnlySliceData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[10];

        // Act
        var bytesRead = stream.Read(buffer, 0, 10);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0, 0, 0, 0, 0 }, buffer);
    }

    [Fact]
    public void Read_AtEndOfSlice_ShouldReturnZero()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[3];
        stream.Read(buffer, 0, 3);

        // Act
        var bytesRead = stream.Read(buffer, 0, 3);

        // Assert
        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public void Read_MultipleReads_ShouldReadSequentially()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);
        var buffer1 = new byte[2];
        var buffer2 = new byte[2];

        // Act
        var bytesRead1 = stream.Read(buffer1, 0, 2);
        var bytesRead2 = stream.Read(buffer2, 0, 2);

        // Assert
        Assert.Equal(2, bytesRead1);
        Assert.Equal(2, bytesRead2);
        Assert.Equal(new byte[] { 1, 2 }, buffer1);
        Assert.Equal(new byte[] { 3, 4 }, buffer2);
    }

    [Fact]
    public void Read_WithZeroCount_ShouldBeNoOp()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[5];

        // Act
        var bytesRead = stream.Read(buffer, 0, 0);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(0, stream.Position);
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Read_WithInnerStreamAtOffset_ShouldReadFromOffset()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        using var innerStream = new MemoryStream(data);
        innerStream.Position = 2; // Start at byte 3
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[3];

        // Act
        var bytesRead = stream.Read(buffer, 0, 3);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 3, 4, 5 }, buffer);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WithByteArray_ShouldReadData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[3];

        // Act
        var bytesRead = await stream.ReadAsync(buffer, 0, 3, CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
    }

    [Fact]
    public async Task ReadAsync_WithMemory_ShouldReadData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[3];

        // Act
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
    }

    [Fact]
    public async Task ReadAsync_RequestingMoreThanSliceLength_ShouldReadOnlySliceData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[10];

        // Act
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0, 0, 0, 0, 0 }, buffer);
    }

    [Fact]
    public async Task ReadAsync_AtEndOfSlice_ShouldReturnZero()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);
        var buffer = new byte[10];
        await stream.ReadAsync(buffer.AsMemory(0, 3), CancellationToken.None);

        // Act
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public async Task ReadAsync_WithZeroLengthBuffer_ShouldBeNoOp()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 3);

        // Act
        var bytesRead = await stream.ReadAsync(Memory<byte>.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task ReadAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var innerStream = new SlowStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);
        var buffer = new byte[3];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await stream.ReadAsync(buffer.AsMemory(), cts.Token));
    }

    #endregion

    #region Seek Tests

    [Fact]
    public void Seek_WithBeginOrigin_ShouldSetPosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);

        // Act
        var newPosition = stream.Seek(2, SeekOrigin.Begin);

        // Assert
        Assert.Equal(2, newPosition);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void Seek_WithCurrentOrigin_ShouldSetRelativePosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);
        stream.Position = 1;

        // Act
        var newPosition = stream.Seek(2, SeekOrigin.Current);

        // Assert
        Assert.Equal(3, newPosition);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Seek_WithEndOrigin_ShouldSetPositionFromEnd()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);

        // Act
        var newPosition = stream.Seek(-1, SeekOrigin.End);

        // Assert
        Assert.Equal(3, newPosition);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Seek_WithNonSeekableStream_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.Seek(1, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_WithInvalidOrigin_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(1, (SeekOrigin)999));
    }

    [Fact]
    public void Seek_ThenRead_ShouldReadFromNewPosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);
        var buffer = new byte[2];

        // Act
        stream.Seek(2, SeekOrigin.Begin);
        var bytesRead = stream.Read(buffer, 0, 2);

        // Assert
        Assert.Equal(2, bytesRead);
        Assert.Equal(new byte[] { 3, 4 }, buffer);
    }

    [Fact]
    public void Seek_BeyondSliceLength_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(10, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_NegativeResultingPosition_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);
        stream.Position = 2;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-5, SeekOrigin.Current));
    }

    [Fact]
    public void Seek_ToEnd_ShouldPositionAtLength()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 4);

        // Act
        var position = stream.Seek(0, SeekOrigin.End);

        // Assert
        Assert.Equal(4, position);
        Assert.Equal(4, stream.Position);
    }

    #endregion

    #region Unsupported Operations Tests

    [Fact]
    public void Flush_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.Flush());
    }

    [Fact]
    public void SetLength_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
    }

    [Fact]
    public void Write_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new SliceReadStream(innerStream, 2);
        var buffer = new byte[] { 4, 5, 6 };

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, buffer.Length));
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_WithLeaveOpenFalse_ShouldDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new SliceReadStream(innerStream, 2, leaveOpen: false);

        // Act
        stream.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    [Fact]
    public void Dispose_WithLeaveOpenTrue_ShouldNotDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new SliceReadStream(innerStream, 2, leaveOpen: true);

        // Act
        stream.Dispose();

        // Assert
        var result = innerStream.ReadByte();
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task DisposeAsync_WithLeaveOpenFalse_ShouldDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new SliceReadStream(innerStream, 2, leaveOpen: false);

        // Act
        await stream.DisposeAsync();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    [Fact]
    public async Task DisposeAsync_WithLeaveOpenTrue_ShouldNotDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new SliceReadStream(innerStream, 2, leaveOpen: true);

        // Act
        await stream.DisposeAsync();

        // Assert
        var result = innerStream.ReadByte();
        Assert.Equal(1, result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Integration_ReadSeekRead_ShouldWorkCorrectly()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 5);

        // Act
        var buffer1 = new byte[2];
        stream.Read(buffer1, 0, 2); // Read 1, 2

        stream.Seek(0, SeekOrigin.Begin); // Reset to start

        var buffer2 = new byte[3];
        stream.Read(buffer2, 0, 3); // Read 1, 2, 3

        // Assert
        Assert.Equal(new byte[] { 1, 2 }, buffer1);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer2);
    }

    [Fact]
    public async Task Integration_MixedSyncAsyncReads_ShouldWorkCorrectly()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new MemoryStream(data);
        using var stream = new SliceReadStream(innerStream, 6);

        // Act
        var buffer1 = new byte[2];
        stream.Read(buffer1, 0, 2); // Read 1, 2

        var buffer2 = new byte[2];
        await stream.ReadAsync(buffer2.AsMemory(), CancellationToken.None); // Read 3, 4

        var buffer3 = new byte[2];
        stream.Read(buffer3, 0, 2); // Read 5, 6

        // Assert
        Assert.Equal(new byte[] { 1, 2 }, buffer1);
        Assert.Equal(new byte[] { 3, 4 }, buffer2);
        Assert.Equal(new byte[] { 5, 6 }, buffer3);
        Assert.Equal(6, stream.Position);
    }

    [Fact]
    public void Integration_SliceOfSlice_ShouldWorkCorrectly()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new MemoryStream(data);
        using var outerSlice = new SliceReadStream(innerStream, 6, leaveOpen: true);
        outerSlice.Position = 2; // Position at byte 3
        using var innerSlice = new SliceReadStream(outerSlice, 3, leaveOpen: true);

        // Act
        var buffer = new byte[3];
        var bytesRead = innerSlice.Read(buffer, 0, 3);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 3, 4, 5 }, buffer);
    }

    [Fact]
    public void Integration_ReadBeyondInnerStreamEnd_ShouldReturnPartialData()
    {
        // Arrange: Inner stream will return fewer bytes than requested
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new PartialReadStream(data);
        using var stream = new SliceReadStream(innerStream, 5);

        // Act
        var buffer = new byte[5];
        var bytesRead = stream.Read(buffer, 0, 5);

        // Assert - PartialReadStream returns only 1 byte at a time
        Assert.Equal(1, bytesRead);
        Assert.Equal(1, buffer[0]);
    }

    [Fact]
    public void Integration_PositionTracking_WithPartialReads_ShouldBeAccurate()
    {
        // Arrange: Inner stream that returns partial reads
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new PartialReadStream(data);
        using var stream = new SliceReadStream(innerStream, 6);

        // Act - Multiple reads to accumulate data
        var buffer = new byte[6];
        var totalRead = 0;
        while (totalRead < 3)
        {
            var bytesRead = stream.Read(buffer, totalRead, 6 - totalRead);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
        }

        // Assert
        Assert.Equal(3, totalRead);
        Assert.Equal(3, stream.Position);
        Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0 }, buffer);
    }

    [Fact]
    public void Integration_InnerStreamAtOffset_WithSeek_ShouldMaintainCorrectBoundaries()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new MemoryStream(data);
        innerStream.Position = 2; // Start at byte 3
        using var stream = new SliceReadStream(innerStream, 4); // Slice: bytes 3-6

        // Act
        stream.Seek(0, SeekOrigin.End); // Go to end of slice
        stream.Seek(-2, SeekOrigin.Current); // Go back 2 bytes
        var buffer = new byte[2];
        var bytesRead = stream.Read(buffer, 0, 2);

        // Assert
        Assert.Equal(2, bytesRead);
        Assert.Equal(new byte[] { 5, 6 }, buffer); // Should read bytes 5 and 6
        Assert.Equal(4, stream.Position); // Should be at end of slice
    }

    #endregion

    #region Helper Classes

    private sealed class NonReadableStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class SlowStream : Stream
    {
        private readonly MemoryStream _inner;

        public SlowStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(100, cancellationToken);
            return await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(100, cancellationToken);
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class PartialReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public PartialReadStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Only read 1 byte at a time to simulate partial reads
            return _inner.Read(buffer, offset, Math.Min(1, count));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Only read 1 byte at a time to simulate partial reads
            return await _inner.ReadAsync(buffer.Slice(0, Math.Min(1, buffer.Length)), cancellationToken);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    #endregion
}
