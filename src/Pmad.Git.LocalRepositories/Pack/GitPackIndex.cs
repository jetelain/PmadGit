using System.Buffers;
using System.Buffers.Binary;

namespace Pmad.Git.LocalRepositories.Pack;

internal sealed class GitPackIndex
{
    private readonly Dictionary<GitHash, long> _offsets;

    private GitPackIndex(Dictionary<GitHash, long> offsets)
    {
        _offsets = offsets;
    }

    public bool TryGetOffset(GitHash hash, out long offset) => _offsets.TryGetValue(hash, out offset);

    public static async Task<GitPackIndex> LoadAsync(
        string path,
        int hashLengthBytes,
        CancellationToken cancellationToken = default)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        await using var stream = new FileStream(path, options);
        var signatureBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            await stream.ReadExactlyAsync(signatureBuffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            if (signatureBuffer[0] == 0xFF && signatureBuffer[1] == 't' && signatureBuffer[2] == 'O' && signatureBuffer[3] == 'c')
            {
                return await LoadVersion2Async(stream, hashLengthBytes, cancellationToken).ConfigureAwait(false);
            }

            return await LoadVersion1Async(stream, hashLengthBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(signatureBuffer);
        }
    }

    private static async Task<GitPackIndex> LoadVersion2Async(
        Stream stream,
        int hashLengthBytes,
        CancellationToken cancellationToken)
    {
        var headerBuffer = ArrayPool<byte>.Shared.Rent(8);
        try
        {
            await stream.ReadExactlyAsync(headerBuffer.AsMemory(0, 8), cancellationToken).ConfigureAwait(false);
            if (headerBuffer[0] != 0xFF || headerBuffer[1] != (byte)'t' || headerBuffer[2] != (byte)'O' || headerBuffer[3] != (byte)'c')
            {
                throw new InvalidDataException("Pack index signature mismatch");
            }

            var version = BinaryPrimitives.ReadInt32BigEndian(headerBuffer.AsSpan(4, 4));
            if (version != 2)
            {
                throw new NotSupportedException($"Unsupported pack index version {version}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }

        var fanout = await ReadFanoutAsync(stream, cancellationToken).ConfigureAwait(false);
        var entries = checked((int)fanout[255]);
        var hashes = await ReadHashesAsync(stream, entries, hashLengthBytes, cancellationToken).ConfigureAwait(false);
        stream.Position += (long)entries * 4;
        var (offsets, largeOffsets) = await ReadOffsetsAsync(stream, entries, cancellationToken).ConfigureAwait(false);
        var map = new Dictionary<GitHash, long>(hashes.Length);
        for (var i = 0; i < hashes.Length; i++)
        {
            var offset = offsets[i];
            if (offset < 0)
            {
                offset = largeOffsets[unchecked((int)(-offset - 1))];
            }

            map[hashes[i]] = offset;
        }

        return new GitPackIndex(map);
    }

    private static async Task<GitPackIndex> LoadVersion1Async(
        Stream stream,
        int hashLengthBytes,
        CancellationToken cancellationToken)
    {
        var fanout = await ReadFanoutAsync(stream, cancellationToken).ConfigureAwait(false);
        var entries = checked((int)fanout[255]);
        var hashes = await ReadHashesAsync(stream, entries, hashLengthBytes, cancellationToken).ConfigureAwait(false);
        var offsets = await ReadOffsets32Async(stream, entries, cancellationToken).ConfigureAwait(false);
        var map = new Dictionary<GitHash, long>(hashes.Length);
        for (var i = 0; i < hashes.Length; i++)
        {
            map[hashes[i]] = offsets[i];
        }

        return new GitPackIndex(map);
    }

    private static async Task<uint[]> ReadFanoutAsync(Stream stream, CancellationToken cancellationToken)
    {
        var fanout = new uint[256];
        var buffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            for (var i = 0; i < 256; i++)
            {
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                fanout[i] = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return fanout;
    }

    private static async Task<GitHash[]> ReadHashesAsync(
        Stream stream,
        int entries,
        int hashLengthBytes,
        CancellationToken cancellationToken)
    {
        var hashes = new GitHash[entries];
        var buffer = ArrayPool<byte>.Shared.Rent(hashLengthBytes);
        try
        {
            for (var i = 0; i < entries; i++)
            {
                await stream.ReadExactlyAsync(buffer.AsMemory(0, hashLengthBytes), cancellationToken).ConfigureAwait(false);
                hashes[i] = GitHash.FromBytes(buffer.AsSpan(0, hashLengthBytes));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hashes;
    }

    private static async Task<long[]> ReadOffsets32Async(Stream stream, int entries, CancellationToken cancellationToken)
    {
        var offsets = new long[entries];
        var buffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            for (var i = 0; i < entries; i++)
            {
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return offsets;
    }

    private static async Task<(long[] offsets, List<long> largeOffsets)> ReadOffsetsAsync(
        Stream stream,
        int entries,
        CancellationToken cancellationToken)
    {
        var offsets = new long[entries];
        var largeOffsets = new List<long>();
        var buffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            for (var i = 0; i < entries; i++)
            {
                await stream.ReadExactlyAsync(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
                var raw = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
                if ((raw & 0x8000_0000) == 0)
                {
                    offsets[i] = raw;
                }
                else
                {
                    offsets[i] = -(largeOffsets.Count + 1);
                    largeOffsets.Add(0);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var largeBuffer = ArrayPool<byte>.Shared.Rent(8);
        try
        {
            for (var i = 0; i < largeOffsets.Count; i++)
            {
                await stream.ReadExactlyAsync(largeBuffer.AsMemory(0, 8), cancellationToken).ConfigureAwait(false);
                var high = BinaryPrimitives.ReadUInt32BigEndian(largeBuffer.AsSpan(0, 4));
                var low = BinaryPrimitives.ReadUInt32BigEndian(largeBuffer.AsSpan(4, 4));
                largeOffsets[i] = ((long)high << 32) | low;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(largeBuffer);
        }

        return (offsets, largeOffsets);
    }
}