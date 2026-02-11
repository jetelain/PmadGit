using System.Text;
using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories.Test.Utilities;

public sealed class EfficientAsyncReadStreamTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidStream_ShouldInitialize()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Assert
        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
    }

    #endregion

    #region Stream Properties Tests

    [Fact]
    public void CanRead_ShouldReturnTrue()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void CanWrite_ShouldReturnFalse()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.False(stream.CanWrite);
    }

    [Fact]
    public void CanSeek_ShouldReturnTrueForSeekableStream()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.True(stream.CanSeek);
    }

    [Fact]
    public void CanSeek_ShouldReturnFalseForNonSeekableStream()
    {
        // Arrange
        using var innerStream = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void Length_ShouldReturnInnerStreamLength()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.Equal(data.Length, stream.Length);
    }

    [Fact]
    public void Position_ShouldReturnCorrectPositionInitially()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Position_ShouldUpdateAfterRead()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new EfficientAsyncReadStream(innerStream);
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
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        stream.Position = 3;

        // Assert
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Position_Set_ShouldClearBuffer()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[2];
        stream.Read(buffer, 0, 2); // Read to populate buffer

        // Act
        stream.Position = 0; // Reset position
        stream.Read(buffer, 0, 1);

        // Assert
        Assert.Equal(1, buffer[0]); // Should read from start again
    }

    #endregion

    #region Read Tests

    [Fact]
    public void Read_WithValidBuffer_ShouldReadData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[3];

        // Act
        var bytesRead = stream.Read(buffer, 0, 3);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
    }

    [Fact]
    public void Read_AtEndOfStream_ShouldReturnZero()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[10];
        stream.Read(buffer, 0, 3); // Read all data

        // Act
        var bytesRead = stream.Read(buffer, 0, 5);

        // Assert
        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public void Read_MultipleReads_ShouldReadSequentially()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
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
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[5];

        // Act
        var bytesRead = stream.Read(buffer, 0, 0);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(0, stream.Position); // Position should not change
        Assert.All(buffer, b => Assert.Equal(0, b)); // Buffer should remain unchanged
    }

    [Fact]
    public void Read_WithZeroCount_AfterReading_ShouldNotChangePosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[2];
        stream.Read(buffer, 0, 2); // Read 2 bytes first

        // Act
        var bytesRead = stream.Read(buffer, 0, 0);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(2, stream.Position); // Position should remain at 2
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WithByteArray_ShouldReadData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
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
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[3];

        // Act
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer);
    }

    [Fact]
    public async Task ReadAsync_AtEndOfStream_ShouldReturnZero()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[10];
        await stream.ReadAsync(buffer.AsMemory(0, 3), CancellationToken.None);

        // Act
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(0, bytesRead);
    }

    [Fact]
    public async Task ReadAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var innerStream = new SlowStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[3];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await stream.ReadAsync(buffer.AsMemory(), cts.Token));
    }

    [Fact]
    public async Task ReadAsync_WithZeroLengthBuffer_ShouldBeNoOp()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var bytesRead = await stream.ReadAsync(Memory<byte>.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(0, stream.Position); // Position should not change
    }

    [Fact]
    public async Task ReadAsync_WithZeroLengthBuffer_AfterReading_ShouldNotChangePosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[2];
        await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None); // Read 2 bytes first

        // Act
        var bytesRead = await stream.ReadAsync(Memory<byte>.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(2, stream.Position); // Position should remain at 2
    }

    [Fact]
    public async Task ReadAsync_WithZeroCount_ShouldBeNoOp()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[5];

        // Act
        var bytesRead = await stream.ReadAsync(buffer, 0, 0, CancellationToken.None);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(0, stream.Position); // Position should not change
        Assert.All(buffer, b => Assert.Equal(0, b)); // Buffer should remain unchanged
    }

    [Fact]
    public async Task ReadAsync_WithZeroCount_AfterReading_ShouldNotChangePosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[2];
        await stream.ReadAsync(buffer, 0, 2, CancellationToken.None); // Read 2 bytes first

        // Act
        var bytesRead = await stream.ReadAsync(buffer, 0, 0, CancellationToken.None);

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(2, stream.Position); // Position should remain at 2
    }

    #endregion

    #region PreLoadAsync Tests

    [Fact]
    public async Task PreLoadAsync_ShouldBufferDataForSubsequentReads()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        await stream.PreLoadAsync(4, CancellationToken.None);
        var buffer = new byte[4];
        var bytesRead = stream.Read(buffer, 0, 4); // Should read from buffer

        // Assert
        Assert.Equal(4, bytesRead);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);
    }

    [Fact]
    public async Task PreLoadAsync_WithLargeByteCount_ShouldBufferAllAvailableData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        await stream.PreLoadAsync(100, CancellationToken.None); // Request more than available
        var buffer = new byte[10];
        var bytesRead = stream.Read(buffer, 0, 10);

        // Assert
        Assert.Equal(3, bytesRead); // Only 3 bytes available
        Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0, 0, 0, 0, 0 }, buffer);
    }

    [Fact]
    public async Task PreLoadAsync_ConsecutiveCalls_ShouldWorkCorrectly()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act - First preload and complete read
        await stream.PreLoadAsync(2, CancellationToken.None);
        var buffer1 = new byte[2];
        stream.Read(buffer1, 0, 2); // Exhausts buffer
        
        // Second preload after buffer is exhausted
        await stream.PreLoadAsync(2, CancellationToken.None);
        var buffer2 = new byte[2];
        var bytesRead = stream.Read(buffer2, 0, 2);

        // Assert
        Assert.Equal(new byte[] { 1, 2 }, buffer1);
        Assert.Equal(2, bytesRead);
        Assert.Equal(new byte[] { 3, 4 }, buffer2);
    }

    [Fact]
    public async Task PreLoadAsync_WithZeroBytes_ShouldNotBufferAnything()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        await stream.PreLoadAsync(0, CancellationToken.None);
        var buffer = new byte[3];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(3, bytesRead); // Should still read from inner stream
    }

    [Fact]
    public async Task PreLoadAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var innerStream = new SlowStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await stream.PreLoadAsync(3, cts.Token));
    }

    #endregion

    #region ReadUntilAsync Tests

    [Fact]
    public async Task ReadUntilAsync_WithDelimiterPresent_ShouldReturnDataBeforeDelimiter()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello\nWorld");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var result = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);

        // Assert
        Assert.Equal(Encoding.UTF8.GetBytes("Hello"), result);
    }

    [Fact]
    public async Task ReadUntilAsync_ShouldAdvancePositionPastDelimiter()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello\nWorld");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        var buffer = new byte[5];
        var bytesRead = stream.Read(buffer, 0, 5);

        // Assert
        Assert.Equal(5, bytesRead);
        Assert.Equal(Encoding.UTF8.GetBytes("World"), buffer);
    }

    [Fact]
    public async Task ReadUntilAsync_WithDelimiterAtStart_ShouldReturnEmptyArray()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("\nHello");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var result = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadUntilAsync_WithMultipleDelimiters_ShouldStopAtFirst()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello\nWorld\n!");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var result = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);

        // Assert
        Assert.Equal(Encoding.UTF8.GetBytes("Hello"), result);
    }

    [Fact]
    public async Task ReadUntilAsync_WithDelimiterNotFound_ShouldThrowEndOfStreamException()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello World");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await stream.ReadUntilAsync((byte)'\n', CancellationToken.None));
        
        Assert.Contains("Delimiter not found", exception.Message);
    }

    [Fact]
    public async Task ReadUntilAsync_WithEmptyStream_ShouldThrowEndOfStreamException()
    {
        // Arrange
        using var innerStream = new MemoryStream(Array.Empty<byte>());
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await stream.ReadUntilAsync((byte)'\n', CancellationToken.None));
    }

    [Fact]
    public async Task ReadUntilAsync_ConsecutiveCalls_ShouldReadMultipleLines()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Line1\nLine2\nLine3");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var line1 = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        var line2 = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);

        // Assert
        Assert.Equal(Encoding.UTF8.GetBytes("Line1"), line1);
        Assert.Equal(Encoding.UTF8.GetBytes("Line2"), line2);
    }

    [Fact]
    public async Task ReadUntilAsync_WithLargeData_ShouldHandleMultipleBufferReads()
    {
        // Arrange: Create data larger than internal buffer size (128 bytes)
        var largeData = new byte[500];
        for (int i = 0; i < largeData.Length - 1; i++)
        {
            largeData[i] = (byte)'A';
        }
        largeData[^1] = (byte)'\n'; // Delimiter at end
        
        using var innerStream = new MemoryStream(largeData);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var result = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);

        // Assert
        Assert.Equal(499, result.Length);
        Assert.All(result, b => Assert.Equal((byte)'A', b));
    }

    [Fact]
    public async Task ReadUntilAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var innerStream = new SlowStream(Encoding.UTF8.GetBytes("Hello\nWorld"));
        using var stream = new EfficientAsyncReadStream(innerStream);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await stream.ReadUntilAsync((byte)'\n', cts.Token));
    }

    #endregion

    #region Seek Tests

    [Fact]
    public void Seek_WithBeginOrigin_ShouldSetPosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var newPosition = stream.Seek(3, SeekOrigin.Begin);

        // Assert
        Assert.Equal(3, newPosition);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Seek_WithCurrentOrigin_ShouldSetRelativePosition()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);
        stream.Position = 2;

        // Act
        var newPosition = stream.Seek(2, SeekOrigin.Current);

        // Assert
        Assert.Equal(4, newPosition);
        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public void Seek_WithEndOrigin_ShouldSetPositionFromEnd()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var newPosition = stream.Seek(-2, SeekOrigin.End);

        // Assert
        Assert.Equal(3, newPosition);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Seek_WithNonSeekableStream_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.Seek(1, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_WithInvalidOrigin_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(1, (SeekOrigin)999));
    }

    #endregion

    #region Unsupported Operations Tests

    [Fact]
    public void Flush_ShouldNotThrow()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert (should not throw)
        stream.Flush();
    }

    [Fact]
    public void SetLength_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
    }

    [Fact]
    public void Write_ShouldThrowNotSupportedException()
    {
        // Arrange
        using var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream = new EfficientAsyncReadStream(innerStream);
        var buffer = new byte[] { 4, 5, 6 };

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, buffer.Length));
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_ShouldDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        stream.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeInnerStream()
    {
        // Arrange
        var innerStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        await stream.DisposeAsync();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_PreLoadAndRead_ShouldWorkTogether()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello World!");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        await stream.PreLoadAsync(5, CancellationToken.None);
        var buffer1 = new byte[5];
        stream.Read(buffer1, 0, 5);
        
        var buffer2 = new byte[7];
        await stream.ReadAsync(buffer2.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(Encoding.UTF8.GetBytes("Hello"), buffer1);
        Assert.Equal(Encoding.UTF8.GetBytes(" World!"), buffer2);
    }

    [Fact]
    public async Task Integration_ReadUntilAndRead_ShouldWorkTogether()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Header\nBody Content");
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var header = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        var buffer = new byte[12];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(Encoding.UTF8.GetBytes("Header"), header);
        Assert.Equal(12, bytesRead);
        Assert.Equal(Encoding.UTF8.GetBytes("Body Content"), buffer);
    }

    [Fact]
    public async Task Integration_SeekAndRead_ShouldWorkTogether()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var buffer1 = new byte[2];
        await stream.ReadAsync(buffer1.AsMemory(), CancellationToken.None);
        
        stream.Seek(0, SeekOrigin.Begin); // Reset to start
        
        var buffer2 = new byte[2];
        await stream.ReadAsync(buffer2.AsMemory(), CancellationToken.None);

        // Assert
        Assert.Equal(new byte[] { 1, 2 }, buffer1);
        Assert.Equal(new byte[] { 1, 2 }, buffer2); // Should read same data after seek
    }

    [Fact]
    public async Task Integration_BufferedReadAndPosition_ShouldTrackCorrectly()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        Assert.Equal(0, stream.Position);

        await stream.PreLoadAsync(5, CancellationToken.None);
        Assert.Equal(0, stream.Position); // PreLoad doesn't advance position until data is read

        var buffer = new byte[3];
        stream.Read(buffer, 0, 3);
        Assert.Equal(3, stream.Position);

        stream.Read(buffer, 0, 2);
        Assert.Equal(5, stream.Position);

        await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
        Assert.Equal(8, stream.Position);
    }

    #endregion

    #region Large Payload Tests (> BufferSize)

    [Fact]
    public async Task PreLoadAsync_WithLargePayload_ShouldBufferCorrectly()
    {
        // Arrange: Create data larger than BufferSize (128 bytes)
        var data = new byte[300];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        await stream.PreLoadAsync(250, CancellationToken.None);
        var buffer = new byte[250];
        var bytesRead = stream.Read(buffer, 0, 250);

        // Assert
        Assert.Equal(250, bytesRead);
        for (int i = 0; i < 250; i++)
        {
            Assert.Equal((byte)(i % 256), buffer[i]);
        }
    }

    [Fact]
    public async Task PreLoadAsync_WithLargePayload_ThenPartialRead_ShouldMaintainCorrectPosition()
    {
        // Arrange: Create data larger than BufferSize (128 bytes)
        var data = new byte[300];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act - Preload 200 bytes, read 100, then read remaining 100
        await stream.PreLoadAsync(200, CancellationToken.None);
        Assert.Equal(0, stream.Position); // Position not advanced yet

        var buffer1 = new byte[100];
        var bytesRead1 = stream.Read(buffer1, 0, 100);
        Assert.Equal(100, stream.Position); // Position advanced after read

        var buffer2 = new byte[100];
        var bytesRead2 = stream.Read(buffer2, 0, 100);
        Assert.Equal(200, stream.Position); // Position continues to advance

        // Assert
        Assert.Equal(100, bytesRead1);
        Assert.Equal(100, bytesRead2);
        
        // Verify data integrity
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal((byte)(i % 256), buffer1[i]);
            Assert.Equal((byte)((i + 100) % 256), buffer2[i]);
        }
    }

    [Fact]
    public async Task PreLoadAsync_WithLargePayload_MultiplePreloads_ShouldWorkCorrectly()
    {
        // Arrange: Create data larger than BufferSize (128 bytes)
        var data = new byte[400];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act - Preload 150 bytes, read all, then preload another 150 bytes
        await stream.PreLoadAsync(150, CancellationToken.None);
        var buffer1 = new byte[150];
        var bytesRead1 = stream.Read(buffer1, 0, 150);

        await stream.PreLoadAsync(150, CancellationToken.None);
        var buffer2 = new byte[150];
        var bytesRead2 = stream.Read(buffer2, 0, 150);

        // Assert
        Assert.Equal(150, bytesRead1);
        Assert.Equal(150, bytesRead2);
        Assert.Equal(300, stream.Position);

        // Verify data integrity
        for (int i = 0; i < 150; i++)
        {
            Assert.Equal((byte)(i % 256), buffer1[i]);
            Assert.Equal((byte)((i + 150) % 256), buffer2[i]);
        }
    }

    [Fact]
    public async Task ReadUntilAsync_WithLargePayloadBeforeDelimiter_ShouldBufferRemainingDataCorrectly()
    {
        // Arrange: Create data with delimiter after 250 bytes (> BufferSize)
        // Delimiter at 250 means it's in chunk: 128*1=128, 128*2=256, so it's in the second chunk at position 250-128=122
        // Remaining bytes in that chunk after delimiter: 128-123=5 bytes will be buffered
        var data = new byte[300];
        for (int i = 0; i < 250; i++)
        {
            data[i] = (byte)'A';
        }
        data[250] = (byte)'\n'; // Delimiter
        for (int i = 251; i < 300; i++)
        {
            data[i] = (byte)'B';
        }
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var result = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        
        // Read remaining data from buffer (only 5 bytes buffered from the chunk containing delimiter)
        var buffer = new byte[5];
        var bytesRead = stream.Read(buffer, 0, 5);

        // Assert
        Assert.Equal(250, result.Length);
        Assert.All(result, b => Assert.Equal((byte)'A', b));
        Assert.Equal(5, bytesRead);
        Assert.All(buffer, b => Assert.Equal((byte)'B', b));
        Assert.Equal(256, stream.Position); // 251 + 5 buffered bytes
    }

    [Fact]
    public async Task ReadUntilAsync_WithLargePayload_ThenReadUntilAgain_ShouldWorkCorrectly()
    {
        // Arrange: Create data with multiple delimiters across buffer boundaries
        // First delimiter at 150 (in chunk 128*2=256, position 150-128=22, remaining: 128-23=105)
        // Second delimiter at 351 (in chunk 128*3=384, position 351-256=95, remaining: 128-96=32)
        var data = new byte[400];
        int pos = 0;
        
        // First line: 150 bytes + delimiter
        for (int i = 0; i < 150; i++)
        {
            data[pos++] = (byte)'A';
        }
        data[pos++] = (byte)'\n'; // Position 150
        
        // Second line: 200 bytes + delimiter
        for (int i = 0; i < 200; i++)
        {
            data[pos++] = (byte)'B';
        }
        data[pos++] = (byte)'\n'; // Position 351
        
        // Third line: remaining bytes
        for (int i = pos; i < 400; i++)
        {
            data[i] = (byte)'C';
        }

        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var line1 = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        var line2 = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        
        // After second ReadUntil, 32 bytes are buffered from the chunk
        var buffer = new byte[32];
        var bytesRead = stream.Read(buffer, 0, 32);

        // Assert
        Assert.Equal(150, line1.Length);
        Assert.All(line1, b => Assert.Equal((byte)'A', b));
        
        Assert.Equal(200, line2.Length);
        Assert.All(line2, b => Assert.Equal((byte)'B', b));
        
        Assert.Equal(32, bytesRead);
        Assert.All(buffer, b => Assert.Equal((byte)'C', b));
        
        Assert.Equal(384, stream.Position); // 352 + 32 buffered
    }

    [Fact]
    public async Task ReadUntilAsync_WithLargePayload_ThenPreLoadAndRead_ShouldWorkCorrectly()
    {
        // Arrange: Simple test to verify buffer accumulation works correctly
        var data = new byte[500];
        for (int i = 0; i < 250; i++)
        {
            data[i] = (byte)'A';
        }
        data[250] = (byte)'\n'; // Delimiter
        for (int i = 251; i < 500; i++)
        {
            data[i] = (byte)'B';
        }
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act
        var result = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);

        var buffer = new byte[249]; // Try to read all buffered data
        await stream.ReadExactlyAsync(buffer, 0, 249);

        Assert.Equal(-1, stream.ReadByte()); // Should be at end of stream

        // Assert
        Assert.Equal(250, result.Length);
        Assert.Equal(249, buffer.Length);

        Assert.All(result, b => Assert.Equal((byte)'A', b));
        Assert.All(buffer, b => Assert.Equal((byte)'B', b));
    }

    [Fact]
    public async Task PreLoadAsync_WithLargePayload_PartialRead_ThenReadUntil_ShouldWorkCorrectly()
    {
        // Arrange: Create data with delimiter in the middle
        var data = new byte[500];
        for (int i = 0; i < 200; i++)
        {
            data[i] = (byte)(i % 256);
        }
        data[200] = (byte)'\n'; // Delimiter
        for (int i = 201; i < 500; i++)
        {
            data[i] = (byte)((i + 100) % 256);
        }
        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act - Preload 300 bytes, read 50, then ReadUntil
        await stream.PreLoadAsync(300, CancellationToken.None);
        
        var buffer1 = new byte[50];
        stream.ReadExactly(buffer1, 0, 50);
        Assert.Equal(50, stream.Position);

        // Now ReadUntil should find delimiter in buffered data
        var result = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        Assert.Equal(201, stream.Position); // After delimiter

        // Read some more data
        var buffer2 = new byte[299];
        stream.ReadExactly(buffer2, 0, 299);

        // Assert
        Assert.Equal(150, result.Length); // 200 - 50 bytes before delimiter
        Assert.Equal(500, stream.Position);

        // Verify data integrity
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal((byte)(i % 256), buffer1[i]);
        }
        for (int i = 0; i < 150; i++)
        {
            Assert.Equal((byte)((i + 50) % 256), result[i]);
        }
        for (int i = 0; i < 99; i++)
        {
            Assert.Equal((byte)((i + 201 + 100) % 256), buffer2[i]);
        }
    }

    [Fact]
    public async Task MixedOperations_WithLargePayloads_ShouldMaintainDataIntegrity()
    {
        // Arrange: Focus on data correctness with simpler operations
        var data = new byte[500];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        data[199] = (byte)'\n';  // First delimiter

        using var innerStream = new MemoryStream(data);
        using var stream = new EfficientAsyncReadStream(innerStream);

        // Act & Assert
        
        // 1. PreLoad 200 bytes and read first 100
        await stream.PreLoadAsync(200, CancellationToken.None);
        var buf1 = new byte[100];
        var read1 = stream.Read(buf1, 0, 100);
        Assert.Equal(100, read1);
        Assert.Equal(0, buf1[0]);

        // 2. ReadUntil should find delimiter at 199 using remaining buffered data
        var line1 = await stream.ReadUntilAsync((byte)'\n', CancellationToken.None);
        Assert.Equal(99, line1.Length);
        
        // 3. Continue reading
        var buf2 = new byte[100];
        var read2 = await stream.ReadAsync(buf2.AsMemory(), CancellationToken.None);
        Assert.True(read2 > 0);
        Assert.Equal((byte)200, buf2[0]); // data[200]
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// A non-seekable stream wrapper for testing
    /// </summary>
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

    /// <summary>
    /// A slow stream that simulates network delays for testing cancellation
    /// </summary>
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

    #endregion
}
