using System.Buffers;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace Pmad.Git.LocalRepositories.Pack;

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
        if (!stream.CanSeek)
        {
            throw new ArgumentException("Stream must support seeking for ofs-delta objects", nameof(stream));
        }

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
        await stream.ReadExactlyAsync(baseHashBytes, cancellationToken).ConfigureAwait(false);
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
        var buffer = ArrayPool<byte>.Shared.Rent(10);
        try
        {
            var read = await ReadVariableLengthBytesAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            
            var b = buffer[0];
            long offset = (uint)(b & 0x7F);
            
            for (int i = 1; i < read; i++)
            {
                b = buffer[i];
                offset = ((offset + 1) << 7) | (uint)(b & 0x7F);
            }

            return offset;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads and decompresses zlib-compressed data from a stream.
    /// </summary>
    /// <param name="sourceStream">The source stream to read from. Must be seekable to rewind after decompression.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The decompressed data</returns>
    /// <remarks>
    /// The stream must support seeking because this method rewinds the stream to the position
    /// immediately after the compressed data, compensating for any extra bytes read by the inflater.
    /// </remarks>
    internal static async Task<byte[]> ReadZLibAsync(Stream sourceStream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        var inflater = new Inflater(noHeader: false);

        var inputBuffer = ArrayPool<byte>.Shared.Rent(4096);
        var outputBuffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (true)
            {
                if (inflater.IsNeedingInput)
                {
                    int readBytes = await sourceStream.ReadAsync(inputBuffer, 0, inputBuffer.Length, cancellationToken).ConfigureAwait(false);
                    if (readBytes == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    inflater.SetInput(inputBuffer, 0, readBytes);
                }

                int outputBytes = inflater.Inflate(outputBuffer);

                buffer.Write(outputBuffer, 0, outputBytes);

                if (inflater.IsFinished)
                {
                    // Move the source stream back to the position right after the compressed data
                    sourceStream.Seek(-inflater.RemainingInput, SeekOrigin.Current);

                    return buffer.ToArray();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inputBuffer);
            ArrayPool<byte>.Shared.Return(outputBuffer);
        }
    }

    public static async ValueTask<(int kind, long size)> ReadTypeAndSizeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(10);
        try
        {
            var read = await ReadVariableLengthBytesAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            
            var first = buffer[0];
            var kind = (first >> 4) & 0x7;
            long size = first & 0x0F;
            var shift = 4;
            
            for (int i = 1; i < read; i++)
            {
                first = buffer[i];
                size |= (long)(first & 0x7F) << shift;
                shift += 7;
            }

            return (kind, size);
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

    /// <summary>
    /// Reads variable-length encoded bytes from a stream into a buffer.
    /// </summary>
    /// <param name="stream">The source stream to read from. Must be seekable to rewind any over-read bytes.</param>
    /// <param name="buffer">The buffer to read into. Must be at least 10 bytes to handle maximum variable-length encoding.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of bytes consumed from the variable-length encoding</returns>
    /// <remarks>
    /// The stream must support seeking because this method may read ahead to find the end of the
    /// variable-length encoding (marked by a byte with the high bit clear), and then rewinds the
    /// stream to the position immediately after the last consumed byte.
    /// </remarks>
    internal static async ValueTask<int> ReadVariableLengthBytesAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var maxBytes = Math.Min(10, buffer.Length);
        var totalBytesRead = 0;
        
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, maxBytes), cancellationToken).ConfigureAwait(false);
        if (bytesRead == 0)
        {
            throw new EndOfStreamException();
        }
        
        totalBytesRead = bytesRead;
        
        for (int i = 0; i < totalBytesRead; i++)
        {
            if ((buffer[i] & 0x80) == 0)
            {
                var bytesConsumed = i + 1;
                if (bytesConsumed < totalBytesRead)
                {
                    stream.Seek(bytesConsumed - totalBytesRead, SeekOrigin.Current);
                }
                return bytesConsumed;
            }
        }
        
        while (totalBytesRead < maxBytes)
        {
            bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead, maxBytes - totalBytesRead), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException();
            }
            
            var endIndex = totalBytesRead + bytesRead;
            for (int i = totalBytesRead; i < endIndex; i++)
            {
                if ((buffer[i] & 0x80) == 0)
                {
                    var bytesConsumed = i + 1;
                    if (endIndex > bytesConsumed)
                    {
                        stream.Seek(bytesConsumed - endIndex, SeekOrigin.Current);
                    }
                    return bytesConsumed;
                }
            }
            
            totalBytesRead = endIndex;
        }
        
        throw new InvalidDataException("Variable length encoding exceeds maximum size");
    }
}
