using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pmad.Git.Protocol;

/// <summary>
/// Represents a parsed Git packet-line (pkt-line) message.
/// </summary>
/// <param name="Payload">The binary payload of the packet.</param>
/// <param name="IsFlush">Indicates whether this is a flush packet (0000).</param>
/// <param name="IsDelimiter">Indicates whether this is a delimiter packet (0001).</param>
/// <param name="IsResponseEnd">Indicates whether this is a response end packet (0002).</param>
public readonly record struct PktLine(ReadOnlyMemory<byte> Payload, bool IsFlush, bool IsDelimiter, bool IsResponseEnd = false)
{
    /// <summary>
    /// Gets a value indicating whether the payload is empty.
    /// </summary>
    public bool IsEmpty => Payload.IsEmpty;

    /// <summary>
    /// Returns the payload decoded as a UTF-8 string.
    /// </summary>
    public string AsString() => Encoding.UTF8.GetString(Payload.Span);
}

/// <summary>
/// Reads Git packet-line (pkt-line) formatted streams.
/// </summary>
public sealed class PktLineReader
{
    private readonly Stream _stream;
    private readonly byte[] _headerBuffer = new byte[4];

    /// <summary>
    /// Initializes a new instance of the <see cref="PktLineReader"/> class.
    /// </summary>
    /// <param name="stream">The source stream to read packet lines from.</param>
    public PktLineReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Reads the next packet line from the stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed <see cref="PktLine"/>, or <see langword="null"/> if the end of the stream was reached.</returns>
    public async Task<PktLine?> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await ReadExactAsync(_headerBuffer, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < _headerBuffer.Length)
        {
            throw new InvalidDataException("Unexpected end of packet header");
        }

        var length = ParseLength(_headerBuffer);
        if (length == 0)
        {
            return new PktLine(ReadOnlyMemory<byte>.Empty, IsFlush: true, IsDelimiter: false, IsResponseEnd: false);
        }

        if (length == 1)
        {
            return new PktLine(ReadOnlyMemory<byte>.Empty, IsFlush: false, IsDelimiter: true, IsResponseEnd: false);
        }

        if (length == 2)
        {
            return new PktLine(ReadOnlyMemory<byte>.Empty, IsFlush: false, IsDelimiter: false, IsResponseEnd: true);
        }

        if (length < 4)
        {
            throw new InvalidDataException($"pkt-line length {length} is invalid; control packets are 0, 1, or 2, and data packets must be at least 4 bytes");
        }

        if (length > 65520)
        {
            throw new InvalidDataException($"pkt-line length {length} exceeds maximum allowed packet length of 65520 bytes");
        }

        var payloadLength = length - 4;
        var payload = new byte[payloadLength];
        await ReadFullyAsync(payload, payloadLength, cancellationToken).ConfigureAwait(false);
        return new PktLine(payload, IsFlush: false, IsDelimiter: false, IsResponseEnd: false);
    }

    private async Task<int> ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private async Task ReadFullyAsync(byte[] buffer, int length, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < length)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of pkt-line payload");
            }

            offset += read;
        }
    }

    private static int ParseLength(ReadOnlySpan<byte> header)
    {
        var value = 0;
        for (var i = 0; i < header.Length; i++)
        {
            var c = header[i];
            value <<= 4;
            value |= c switch
            {
                >= (byte)'0' and <= (byte)'9' => c - '0',
                >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
                _ => throw new InvalidDataException("pkt-line header contains non-hex digit")
            };
        }

        return value;
    }
}

