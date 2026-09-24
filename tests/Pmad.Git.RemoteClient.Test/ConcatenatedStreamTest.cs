using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public sealed class ConcatenatedStreamTest
{
    [Fact]
    public void Properties_WithSeekableStreams_ReflectCombinedLengthAndPosition()
    {
        using var s1 = new MemoryStream(new byte[] { 1, 2, 3 });
        using var s2 = new MemoryStream(new byte[] { 4, 5, 6, 7 });
        using var concat = new ConcatenatedStream(s1, s2);

        Assert.True(concat.CanRead);
        Assert.True(concat.CanSeek);
        Assert.False(concat.CanWrite);
        Assert.Equal(7, concat.Length);
        Assert.Equal(0, concat.Position);
    }

    [Fact]
    public async Task ReadAsync_AcrossStreamBoundary_ReadsBothStreamsSequentially()
    {
        using var s1 = new MemoryStream(new byte[] { 10, 20, 30 });
        using var s2 = new MemoryStream(new byte[] { 40, 50 });
        using var concat = new ConcatenatedStream(s1, s2);

        var destination = new MemoryStream();
        await concat.CopyToAsync(destination);

        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, destination.ToArray());
    }

    [Fact]
    public void Read_WithPartialBuffers_ReadsCorrectBytesAcrossBoundary()
    {
        using var s1 = new MemoryStream(new byte[] { 1, 2, 3 });
        using var s2 = new MemoryStream(new byte[] { 4, 5, 6 });
        using var concat = new ConcatenatedStream(s1, s2);

        var buffer = new byte[2];

        // Read 1-2 from s1
        var read = concat.Read(buffer, 0, 2);
        Assert.Equal(2, read);
        Assert.Equal(new byte[] { 1, 2 }, buffer);

        // Read 3 from s1
        read = concat.Read(buffer, 0, 2);
        Assert.Equal(1, read);
        Assert.Equal(3, buffer[0]);

        // Next read moves to s2
        read = concat.Read(buffer, 0, 2);
        Assert.Equal(2, read);
        Assert.Equal(new byte[] { 4, 5 }, buffer);

        // Read 6 from s2
        read = concat.Read(buffer, 0, 2);
        Assert.Equal(1, read);
        Assert.Equal(6, buffer[0]);

        // End of stream
        read = concat.Read(buffer, 0, 2);
        Assert.Equal(0, read);
    }

    [Fact]
    public void Seek_CanSeekToFirstOrSecondStream()
    {
        using var s1 = new MemoryStream(new byte[] { 10, 20, 30 });
        using var s2 = new MemoryStream(new byte[] { 40, 50, 60 });
        using var concat = new ConcatenatedStream(s1, s2);

        // Seek into second stream
        concat.Seek(4, SeekOrigin.Begin);
        Assert.Equal(4, concat.Position);
        Assert.Equal(50, concat.ReadByte());

        // Seek back into first stream
        concat.Seek(1, SeekOrigin.Begin);
        Assert.Equal(1, concat.Position);
        Assert.Equal(20, concat.ReadByte());

        // Seek from end
        concat.Seek(-1, SeekOrigin.End);
        Assert.Equal(5, concat.Position);
        Assert.Equal(60, concat.ReadByte());
    }

    [Fact]
    public void Dispose_WhenLeaveSecondOpenTrue_LeavesSecondStreamOpen()
    {
        var s1 = new MemoryStream(new byte[] { 1, 2 });
        var s2 = new MemoryStream(new byte[] { 3, 4 });
        var concat = new ConcatenatedStream(s1, s2, leaveSecondOpen: true);

        concat.Dispose();

        Assert.False(s1.CanRead); // Disposed
        Assert.True(s2.CanRead);  // Left open
        s2.Dispose();
    }
}

