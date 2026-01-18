using System.Buffers;
using System.IO.Compression;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Provides utilities for reading Git pack file objects.
/// </summary>
internal static class GitPackObjectReader
{
    /// <summary>
    /// Reads a Git object from a pack stream at the current position.
    /// </summary>
    /// <param name="stream">The stream to read from, positioned at the start of the object</param>
    /// <param name="currentOffset">The offset in the pack file where this object starts (used for ofs-delta calculation)</param>
    /// <param name="hashLengthBytes">Length of hash in bytes (20 for SHA-1, 32 for SHA-256)</param>
    /// <param name="resolveByHash">Callback to resolve base objects by hash (for ref-delta)</param>
    /// <param name="resolveByOffset">Callback to resolve base objects by offset (for ofs-delta)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task<GitObjectData> ReadObjectAsync(
        Stream stream,
        long currentOffset,
        int hashLengthBytes,
        Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
        Func<long, CancellationToken, Task<GitObjectData>>? resolveByOffset,
        CancellationToken cancellationToken)
    {
        var (kind, _) = await ReadTypeAndSizeAsync(stream, cancellationToken).ConfigureAwait(false);

        return kind switch
        {
            1 => new GitObjectData(
                GitObjectType.Commit,
                await ReadZLibAsync(stream, cancellationToken).ConfigureAwait(false)),
            2 => new GitObjectData(
                GitObjectType.Tree,
                await ReadZLibAsync(stream, cancellationToken).ConfigureAwait(false)),
            3 => new GitObjectData(
                GitObjectType.Blob,
                await ReadZLibAsync(stream, cancellationToken).ConfigureAwait(false)),
            4 => new GitObjectData(
                GitObjectType.Tag,
                await ReadZLibAsync(stream, cancellationToken).ConfigureAwait(false)),
            6 => await ReadOfsDeltaAsync(stream, currentOffset, resolveByHash, resolveByOffset, cancellationToken).ConfigureAwait(false),
            7 => await ReadRefDeltaAsync(stream, hashLengthBytes, resolveByHash, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported pack object kind {kind}")
        };
    }

    private static async Task<GitObjectData> ReadOfsDeltaAsync(
        Stream stream,
        long currentOffset,
        Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
        Func<long, CancellationToken, Task<GitObjectData>>? resolveByOffset,
        CancellationToken cancellationToken)
    {
        var baseDistance = await ReadOfsDeltaOffsetAsync(stream, cancellationToken).ConfigureAwait(false);
        var baseOffset = currentOffset - baseDistance;
        
        GitObjectData baseObject;
        if (resolveByOffset != null)
        {
            baseObject = await resolveByOffset(baseOffset, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidDataException("ofs-delta requires offset resolution capability");
        }

        var deltaPayload = await ReadZLibAsync(stream, cancellationToken).ConfigureAwait(false);
        return ApplyDelta(baseObject, deltaPayload);
    }

    private static async Task<GitObjectData> ReadRefDeltaAsync(
        Stream stream,
        int hashLengthBytes,
        Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
        CancellationToken cancellationToken)
    {
        var baseHashBytes = new byte[hashLengthBytes];
        await ReadExactlyAsync(stream, baseHashBytes, cancellationToken).ConfigureAwait(false);
        var baseHash = GitHash.FromBytes(baseHashBytes);
        var baseObject = await resolveByHash(baseHash, cancellationToken).ConfigureAwait(false);
        var deltaPayload = await ReadZLibAsync(stream, cancellationToken).ConfigureAwait(false);
        return ApplyDelta(baseObject, deltaPayload);
    }

    private static GitObjectData ApplyDelta(GitObjectData baseObject, byte[] delta)
    {
        var patched = ApplyDeltaCore(baseObject.Content, delta);
        return new GitObjectData(baseObject.Type, patched);
    }

    private static byte[] ApplyDeltaCore(ReadOnlySpan<byte> source, ReadOnlySpan<byte> delta)
    {
        var cursor = 0;
        var baseSize = ReadVariableLength(delta, ref cursor);
        var resultSize = ReadVariableLength(delta, ref cursor);

        if (baseSize != source.Length)
        {
            throw new InvalidDataException("Delta base size mismatch");
        }

        if (resultSize > int.MaxValue)
        {
            throw new InvalidDataException("Delta result size is too large");
        }

        var result = new byte[(int)resultSize];
        var resultIndex = 0;

        while (cursor < delta.Length)
        {
            var opcode = delta[cursor++];
            if ((opcode & 0x80) != 0)
            {
                var copyOffset = 0;
                var copySize = 0;

                if ((opcode & 0x01) != 0) copyOffset |= delta[cursor++];
                if ((opcode & 0x02) != 0) copyOffset |= delta[cursor++] << 8;
                if ((opcode & 0x04) != 0) copyOffset |= delta[cursor++] << 16;
                if ((opcode & 0x08) != 0) copyOffset |= delta[cursor++] << 24;

                if ((opcode & 0x10) != 0) copySize |= delta[cursor++];
                if ((opcode & 0x20) != 0) copySize |= delta[cursor++] << 8;
                if ((opcode & 0x40) != 0) copySize |= delta[cursor++] << 16;
                if (copySize == 0) copySize = 0x10000;

                if (copyOffset < 0 || copyOffset + copySize > source.Length)
                {
                    throw new InvalidDataException("Delta copy instruction exceeds base size");
                }

                source.Slice(copyOffset, copySize).CopyTo(result.AsSpan(resultIndex));
                resultIndex += copySize;
            }
            else if (opcode != 0)
            {
                if (cursor + opcode > delta.Length)
                {
                    throw new InvalidDataException("Delta insert instruction exceeds payload");
                }

                delta.Slice(cursor, opcode).CopyTo(result.AsSpan(resultIndex));
                cursor += opcode;
                resultIndex += opcode;
            }
            else
            {
                throw new InvalidDataException("Invalid delta opcode");
            }
        }

        if (resultIndex != result.Length)
        {
            throw new InvalidDataException("Delta application produced incorrect length");
        }

        return result;
    }

    public static async Task<long> ReadOfsDeltaOffsetAsync(Stream stream, CancellationToken cancellationToken)
    {
        var b = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        if (b < 0)
        {
            throw new EndOfStreamException();
        }

        long offset = (uint)(b & 0x7F);
        while ((b & 0x80) != 0)
        {
            b = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
            if (b < 0)
            {
                throw new EndOfStreamException();
            }

            offset = ((offset + 1) << 7) | (uint)(b & 0x7F);
        }

        return offset;
    }

    public static async Task<byte[]> ReadZLibAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Wrap in a non-buffering stream to prevent ZLibStream from reading ahead
        // This is critical for pack files where multiple compressed objects are consecutive
        using var nonBufferingStream = new NonBufferingStreamWrapper(stream);
        using var zlib = new ZLibStream(nonBufferingStream, CompressionMode.Decompress, leaveOpen: true);
        using var buffer = new MemoryStream();
        var readBuffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = await zlib.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await buffer.WriteAsync(readBuffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Stream wrapper that prevents buffering by only reading one byte at a time.
    /// This is necessary to prevent ZLibStream from reading ahead past the end of a compressed object.
    /// </summary>
    private sealed class NonBufferingStreamWrapper : Stream
    {
        private readonly Stream _inner;

        public NonBufferingStreamWrapper(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Only read one byte at a time to prevent buffering
            if (count == 0)
            {
                return 0;
            }
            return _inner.Read(buffer, offset, 1);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (count == 0)
            {
                return 0;
            }
            return await _inner.ReadAsync(buffer.AsMemory(offset, 1), cancellationToken).ConfigureAwait(false);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0)
            {
                return 0;
            }
            return await _inner.ReadAsync(buffer.Slice(0, 1), cancellationToken).ConfigureAwait(false);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public static async ValueTask<(int kind, long size)> ReadTypeAndSizeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var first = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        if (first < 0)
        {
            throw new EndOfStreamException();
        }

        var kind = (first >> 4) & 0x7;
        long size = first & 0x0F;
        var shift = 4;
        while ((first & 0x80) != 0)
        {
            first = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
            if (first < 0)
            {
                throw new EndOfStreamException();
            }

            size |= (long)(first & 0x7F) << shift;
            shift += 7;
        }

        return (kind, size);
    }

    public static async ValueTask<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            return read == 0 ? -1 : buffer[0];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static long ReadVariableLength(ReadOnlySpan<byte> data, ref int cursor)
    {
        long result = 0;
        var shift = 0;
        while (cursor < data.Length)
        {
            var b = data[cursor++];
            result |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return result;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }
            offset += read;
        }
    }
}
