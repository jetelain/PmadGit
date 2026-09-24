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
    public override long Length => CanSeek ? _inner.Length : throw new NotSupportedException();
    public override long Position
    {
        get => CanSeek ? _inner.Position : throw new NotSupportedException();
        set
        {
            if (!CanSeek)
            {
                throw new NotSupportedException();
            }
            _inner.Position = value;
        }
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => CanSeek ? _inner.Seek(offset, origin) : throw new NotSupportedException();
    public override void SetLength(long value)
    {
        if (!CanSeek)
        {
            throw new NotSupportedException();
        }
        _inner.SetLength(value);
    }
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        CancellationToken effectiveToken;
        CancellationTokenSource? readLinkedCts = null;
        if (cancellationToken.CanBeCanceled)
        {
            readLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token);
            effectiveToken = readLinkedCts.Token;
        }
        else
        {
            effectiveToken = _timeoutCts.Token;
        }

        try
        {
            return await _inner.ReadAsync(buffer, effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Git HTTP upload-pack stream timed out after {_timeout}.");
        }
        finally
        {
            readLinkedCts?.Dispose();
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
