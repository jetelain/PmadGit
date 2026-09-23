using System.IO;

namespace Pmad.Git.RemoteClient;

internal sealed class TimeoutStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationTokenSource _timeoutCts;
    private readonly CancellationTokenSource _linkedCts;
    private readonly TimeSpan _timeout;

    public TimeoutStream(Stream inner, CancellationTokenSource timeoutCts, CancellationTokenSource linkedCts, TimeSpan timeout)
    {
        _inner = inner;
        _timeoutCts = timeoutCts;
        _linkedCts = linkedCts;
        _timeout = timeout;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var readLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token);
        try
        {
            return await _inner.ReadAsync(buffer, readLinkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Git HTTP upload-pack stream timed out after {_timeout}.");
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _linkedCts.Dispose();
            _timeoutCts.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        _linkedCts.Dispose();
        _timeoutCts.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
