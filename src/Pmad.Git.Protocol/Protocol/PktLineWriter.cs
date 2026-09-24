using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pmad.Git.Protocol;

/// <summary>
/// Writes Git packet-line (pkt-line) formatted data to streams.
/// </summary>
public static class PktLineWriter
{
    /// <summary>
    /// The maximum payload length for a pkt-line packet (65520 - 4 bytes header).
    /// </summary>
    public const int MaxPayloadLength = 65516;

    private static readonly ReadOnlyMemory<byte> FlushBytes = new byte[] { (byte)'0', (byte)'0', (byte)'0', (byte)'0' };
    private static readonly ReadOnlyMemory<byte> DelimiterBytes = new byte[] { (byte)'0', (byte)'0', (byte)'0', (byte)'1' };
    private static readonly ReadOnlyMemory<byte> ResponseEndBytes = new byte[] { (byte)'0', (byte)'0', (byte)'0', (byte)'2' };

    /// <summary>
    /// Writes a binary payload as a packet line.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="payload">The binary payload to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Length, $"Payload length exceeds maximum allowed packet-line payload size of {MaxPayloadLength} bytes.");
        }

        var totalLength = payload.Length + 4;
        var buffer = ArrayPool<byte>.Shared.Rent(totalLength);
        try
        {
            FormatLength(totalLength, buffer.AsSpan(0, 4));
            payload.Span.CopyTo(buffer.AsSpan(4));
            await stream.WriteAsync(buffer.AsMemory(0, totalLength), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes a UTF-8 string as a packet line.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="value">The string value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        var payload = Encoding.UTF8.GetBytes(value);
        return WriteAsync(stream, payload, cancellationToken);
    }

    /// <summary>
    /// Writes a flush packet (0000) to the stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteFlushAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.WriteAsync(FlushBytes, cancellationToken).AsTask();
    }

    /// <summary>
    /// Writes a delimiter packet (0001) to the stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteDelimiterAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.WriteAsync(DelimiterBytes, cancellationToken).AsTask();
    }

    /// <summary>
    /// Writes a response end packet (0002) to the stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteResponseEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.WriteAsync(ResponseEndBytes, cancellationToken).AsTask();
    }

    private static void FormatLength(int value, Span<byte> destination)
    {
        for (var i = 3; i >= 0; i--)
        {
            destination[3 - i] = ToHex((value >> (i * 4)) & 0xF);
        }
    }

    private static byte ToHex(int value) => (byte)(value switch
    {
        < 10 => '0' + value,
        _ => 'a' + (value - 10)
    });
}

