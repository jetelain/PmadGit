using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public class TimeoutStreamTest
{
    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly bool _delayOrThrowOnToken;

        public NonSeekableReadStream(byte[] data, bool delayOrThrowOnToken = false)
        {
            _inner = new MemoryStream(data);
            _delayOrThrowOnToken = delayOrThrowOnToken;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_delayOrThrowOnToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return await _inner.ReadAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public void SeekableStream_PropertiesAndOperations_DelegateToInner()
    {
        var mem = new MemoryStream();
        mem.Write(new byte[] { 1, 2, 3, 4, 5 });
        mem.Position = 0;
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = new CancellationTokenSource();
        using var stream = new TimeoutStream(mem, timeoutCts, linkedCts, TimeSpan.FromSeconds(5));

        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.True(stream.CanWrite);
        Assert.Equal(5, stream.Length);
        Assert.Equal(0, stream.Position);

        stream.Position = 2;
        Assert.Equal(2, stream.Position);

        stream.Seek(1, SeekOrigin.Begin);
        Assert.Equal(1, stream.Position);

        stream.SetLength(10);
        Assert.Equal(10, stream.Length);

        stream.Write(new byte[] { 99 }, 0, 1);
        stream.Flush();

        var readBuffer = new byte[2];
        var readCount = stream.Read(readBuffer, 0, 2);
        Assert.Equal(2, readCount);
    }

    [Fact]
    public void NonSeekableStream_PropertiesAndOperations_ThrowNotSupportedException()
    {
        var nonSeekable = new NonSeekableReadStream(new byte[] { 1, 2, 3 });
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = new CancellationTokenSource();
        using var stream = new TimeoutStream(nonSeekable, timeoutCts, linkedCts, TimeSpan.FromSeconds(5));

        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);

        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
    }

    [Fact]
    public async Task ReadAsync_SuccessfulRead_ReturnsData()
    {
        var mem = new MemoryStream(new byte[] { 10, 20, 30 });
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = new CancellationTokenSource();
        using var stream = new TimeoutStream(mem, timeoutCts, linkedCts, TimeSpan.FromSeconds(5));

        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer, 0, 3, CancellationToken.None);

        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 10, 20, 30 }, buffer);

        await stream.FlushAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ReadAsync_WithUserCancellationToken_ReadsSuccessfully()
    {
        var mem = new MemoryStream(new byte[] { 10, 20, 30 });
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = new CancellationTokenSource();
        using var stream = new TimeoutStream(mem, timeoutCts, linkedCts, TimeSpan.FromSeconds(5));
        using var userCts = new CancellationTokenSource();

        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer.AsMemory(), userCts.Token);

        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 10, 20, 30 }, buffer);
    }

    [Fact]
    public async Task ReadAsync_WhenTimeoutCtsTriggered_ThrowsTimeoutException()
    {
        var nonSeekable = new NonSeekableReadStream(new byte[] { 1 }, delayOrThrowOnToken: true);
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = new CancellationTokenSource();
        using var stream = new TimeoutStream(nonSeekable, timeoutCts, linkedCts, TimeSpan.FromMilliseconds(100));

        timeoutCts.Cancel();

        var buffer = new byte[1];
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => stream.ReadAsync(buffer.AsMemory()).AsTask());

        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_WhenUserCtsTriggered_ThrowsOperationCanceledException()
    {
        var nonSeekable = new NonSeekableReadStream(new byte[] { 1 }, delayOrThrowOnToken: true);
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = new CancellationTokenSource();
        using var stream = new TimeoutStream(nonSeekable, timeoutCts, linkedCts, TimeSpan.FromSeconds(30));
        using var userCts = new CancellationTokenSource();

        userCts.Cancel();

        var buffer = new byte[1];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(buffer.AsMemory(), userCts.Token).AsTask());
    }

    [Fact]
    public void Dispose_DisposesInnerAndTokens()
    {
        var mem = new MemoryStream(new byte[] { 1 });
        var timeoutCts = new CancellationTokenSource();
        var linkedCts = new CancellationTokenSource();
        var stream = new TimeoutStream(mem, timeoutCts, linkedCts, TimeSpan.FromSeconds(5));

        stream.Dispose();

        Assert.False(mem.CanRead);
        Assert.Throws<ObjectDisposedException>(() => timeoutCts.Token);
        Assert.Throws<ObjectDisposedException>(() => linkedCts.Token);
    }

    [Fact]
    public async Task DisposeAsync_DisposesInnerAndTokens()
    {
        var mem = new MemoryStream(new byte[] { 1 });
        var timeoutCts = new CancellationTokenSource();
        var linkedCts = new CancellationTokenSource();
        var stream = new TimeoutStream(mem, timeoutCts, linkedCts, TimeSpan.FromSeconds(5));

        await stream.DisposeAsync();

        Assert.False(mem.CanRead);
        Assert.Throws<ObjectDisposedException>(() => timeoutCts.Token);
        Assert.Throws<ObjectDisposedException>(() => linkedCts.Token);
    }
}
