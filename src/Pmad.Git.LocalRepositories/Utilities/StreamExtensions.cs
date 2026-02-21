using System.Buffers;

namespace Pmad.Git.LocalRepositories.Utilities;

internal static class StreamExtensions
{
    private const int DefaultBufferSize = 81920; // Same default buffer size used by Stream.CopyToAsync

    public static async Task CopyToExactlyAsync(this Stream source, Stream destination, long length, CancellationToken cancellationToken)
    {
        // Rent a buffer from the pool, ensuring it's not larger than the remaining length to copy to avoid unnecessary memory usage for small copies.
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(DefaultBufferSize, length));
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (remaining > 0)
                    {
                        throw new EndOfStreamException($"Expected {length} bytes but stream ended after {length - remaining} bytes.");
                    }
                    break;
                }
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
