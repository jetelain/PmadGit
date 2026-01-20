using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pmad.Git.HttpServer.Protocol;

internal static class PktLineWriter
{
    public static Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var header = FormatLength(payload.Length + 4);
        return WriteInternalAsync(stream, header, payload, cancellationToken);
    }

    public static Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(value);
        return WriteAsync(stream, payload, cancellationToken);
    }

    public static Task WriteFlushAsync(Stream stream, CancellationToken cancellationToken)
        => stream.WriteAsync("0000"u8.ToArray(), cancellationToken).AsTask();

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
