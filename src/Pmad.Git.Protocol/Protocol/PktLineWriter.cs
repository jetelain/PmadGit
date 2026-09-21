using System;
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
    /// Writes a binary payload as a packet line.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="payload">The binary payload to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var header = FormatLength(payload.Length + 4);
        return WriteInternalAsync(stream, header, payload, cancellationToken);
    }

    /// <summary>
    /// Writes a UTF-8 string as a packet line.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="value">The string value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(value);
        return WriteAsync(stream, payload, cancellationToken);
    }

    /// <summary>
    /// Writes a flush packet (0000) to the stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteFlushAsync(Stream stream, CancellationToken cancellationToken)
        => stream.WriteAsync("0000"u8.ToArray(), cancellationToken).AsTask();

    /// <summary>
    /// Writes a delimiter packet (0001) to the stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task WriteDelimiterAsync(Stream stream, CancellationToken cancellationToken)
        => stream.WriteAsync("0001"u8.ToArray(), cancellationToken).AsTask();

    private static async Task WriteInternalAsync(Stream stream, byte[] header, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private static byte[] FormatLength(int value)
    {
        var buffer = new byte[4];
        for (var i = 3; i >= 0; i--)
        {
            buffer[3 - i] = ToHex((value >> (i * 4)) & 0xF);
        }

        return buffer;
    }

    private static byte ToHex(int value) => (byte)(value switch
    {
        < 10 => '0' + value,
        _ => 'a' + (value - 10)
    });
}

