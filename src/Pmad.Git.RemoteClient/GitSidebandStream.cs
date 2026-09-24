using System.Text;
using Pmad.Git.Protocol;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// A readable stream that demultiplexes Git sideband-64k packet lines.
/// Channel 1 bytes are returned as the stream payload (packfile).
/// Channel 2 messages are routed to the progress callback.
/// Channel 3 messages cause a <see cref="GitRemoteException"/> to be thrown.
/// A flush packet (0000) or end of stream indicates the end of payload.
/// </summary>
public sealed class GitSidebandStream : Stream
{
    private readonly Stream _stream;
    private readonly PktLineReader _reader;
    private readonly Action<string>? _onProgress;
    private readonly bool _leaveOpen;
    private ReadOnlyMemory<byte> _currentPayload;
    private int _currentOffset;
    private bool _endOfStream;
    private long _bytesRead;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitSidebandStream"/> class.
    /// </summary>
    /// <param name="stream">The underlying stream carrying sideband packet lines.</param>
    /// <param name="onProgress">Optional callback for progress messages received on channel 2.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the underlying stream open when disposing; otherwise, <see langword="false"/>.</param>
    public GitSidebandStream(Stream stream, Action<string>? onProgress = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _reader = new PktLineReader(stream);
        _onProgress = onProgress;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        while (true)
        {
            if (_currentOffset < _currentPayload.Length)
            {
                var available = _currentPayload.Length - _currentOffset;
                var toCopy = Math.Min(buffer.Length, available);
                _currentPayload.Slice(_currentOffset, toCopy).CopyTo(buffer);
                _currentOffset += toCopy;
                _bytesRead += toCopy;
                return toCopy;
            }

            if (_endOfStream)
            {
                return 0;
            }

            var packet = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (packet is null || packet.Value.IsFlush)
            {
                _endOfStream = true;
                return 0;
            }

            if (packet.Value.IsEmpty)
            {
                continue;
            }

            var payload = packet.Value.Payload;
            var channel = payload.Span[0];

            switch (channel)
            {
                case 1: // Band 1: Packfile data
                    _currentPayload = payload.Slice(1);
                    _currentOffset = 0;
                    break;

                case 2: // Band 2: Progress message
                    if (payload.Length > 1)
                    {
                        var message = Encoding.UTF8.GetString(payload.Span[1..]);
                        _onProgress?.Invoke(message);
                    }
                    break;

                case 3: // Band 3: Error message
                    var errorMessage = payload.Length > 1
                        ? Encoding.UTF8.GetString(payload.Span[1..]).TrimEnd('\r', '\n')
                        : "Remote sideband error";
                    throw new GitRemoteException(errorMessage);

                default:
                    throw new InvalidDataException($"Unknown sideband channel: {channel}");
            }
        }
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _stream.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

