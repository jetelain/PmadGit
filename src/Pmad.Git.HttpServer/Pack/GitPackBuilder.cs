using System.IO.Compression;
using System.Security.Cryptography;
using Pmad.Git.LocalRepositories.Utilities;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Pack;

internal sealed class GitPackBuilder
{
    public async Task WriteAsync(IGitRepository repository, IReadOnlyList<GitHash> objects, Stream destination, CancellationToken cancellationToken)
    {
        if (repository is null)
        {
            throw new ArgumentNullException(nameof(repository));
        }

        if (objects is null)
        {
            throw new ArgumentNullException(nameof(objects));
        }

        var algorithm = repository.HashLengthBytes switch
        {
            GitHash.Sha1ByteLength => HashAlgorithmName.SHA1,
            GitHash.Sha256ByteLength => HashAlgorithmName.SHA256,
            _ => throw new NotSupportedException("Unsupported git hash length")
        };

        await using var hashingStream = new HashingWriteStream(destination, algorithm, leaveOpen: true);
        await WriteHeaderAsync(hashingStream, objects.Count, cancellationToken).ConfigureAwait(false);

        foreach (var hash in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = await repository.ReadObjectAsync(hash, cancellationToken).ConfigureAwait(false);
            await WriteObjectAsync(hashingStream, data, cancellationToken).ConfigureAwait(false);
        }

        await hashingStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        var digest = hashingStream.CompleteHash();
        await destination.WriteAsync(digest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHeaderAsync(Stream stream, int objectCount, CancellationToken cancellationToken)
    {
        await stream.WriteAsync("PACK"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await WriteUInt32Async(stream, 2u, cancellationToken).ConfigureAwait(false);
        await WriteUInt32Async(stream, (uint)objectCount, cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteUInt32Async(Stream stream, uint value, CancellationToken cancellationToken)
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer[0] = (byte)((value >> 24) & 0xFF);
        buffer[1] = (byte)((value >> 16) & 0xFF);
        buffer[2] = (byte)((value >> 8) & 0xFF);
        buffer[3] = (byte)(value & 0xFF);
        return stream.WriteAsync(buffer.ToArray(), cancellationToken).AsTask();
    }

    private static async Task WriteObjectAsync(Stream stream, GitObjectData data, CancellationToken cancellationToken)
    {
        var header = EncodeObjectHeader(data.Type, data.Content.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        await using var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        await zlib.WriteAsync(data.Content, cancellationToken).ConfigureAwait(false);
        await zlib.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> EncodeObjectHeader(GitObjectType type, int size)
    {
        Span<byte> buffer = stackalloc byte[16];
        var typeCode = type switch
        {
            GitObjectType.Commit => 1,
            GitObjectType.Tree => 2,
            GitObjectType.Blob => 3,
            GitObjectType.Tag => 4,
            _ => throw new NotSupportedException($"Unsupported git object type '{type}'")
        };

        var index = 0;
        var first = (byte)((typeCode << 4) | (size & 0x0F));
        size >>= 4;
        if (size != 0)
        {
            first |= 0x80;
        }

        buffer[index++] = first;
        while (size != 0)
        {
            var next = (byte)(size & 0x7F);
            size >>= 7;
            if (size != 0)
            {
                next |= 0x80;
            }

            buffer[index++] = next;
        }

        return buffer[..index].ToArray();
    }
}
