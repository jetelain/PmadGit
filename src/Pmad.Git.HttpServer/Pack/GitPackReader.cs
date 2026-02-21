using System.Security.Cryptography;
using Pmad.Git.LocalRepositories.Utilities;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Pack;

namespace Pmad.Git.HttpServer.Pack;

internal sealed class GitPackReader
{
    private const int HeaderLength = 12;

    public async Task<IReadOnlyList<GitHash>> ReadAsync(IGitRepository repository, Stream source, CancellationToken cancellationToken)
    {
        if (repository is null)
        {
            throw new ArgumentNullException(nameof(repository));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (source is FileStream fileStream)
        {
            return await ReadAsync(repository, fileStream, cancellationToken).ConfigureAwait(false);
        }

        using var tempStream = new FileStream(
                            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
                            FileMode.CreateNew,
                            FileAccess.ReadWrite,
                            FileShare.Read,
                            bufferSize: 4096,
                            FileOptions.DeleteOnClose);
        await source.CopyToAsync(tempStream, cancellationToken).ConfigureAwait(false);
        tempStream.Seek(0, SeekOrigin.Begin);
        return await ReadAsync(repository, tempStream, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<GitHash>> ReadAsync(IGitRepository repository, FileStream fileStream, CancellationToken cancellationToken)
    {
        var objectCount = await ValidatePackFile(repository, fileStream, cancellationToken).ConfigureAwait(false);

        fileStream.Position = HeaderLength;
       
        var created = new List<GitHash>(checked((int)objectCount));
        var offsetCache = new Dictionary<long, GitObjectData>();
        var hashCache = new Dictionary<string, GitObjectData>(StringComparer.Ordinal);

        for (var i = 0u; i < objectCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var objectOffset = fileStream.Position;

            // Use shared pack object reader
            var materialized = await GitPackObjectReader.ReadObjectAsync(
                fileStream,
                objectOffset,
                repository.HashLengthBytes,
                async (hash, ct) =>
                {
                    // Try cache first, then repository
                    if (hashCache.TryGetValue(hash.Value, out var cached))
                    {
                        return cached;
                    }
                    return await repository.ObjectStore.ReadObjectAsync(hash, ct).ConfigureAwait(false);
                },
                async (offset, ct) =>
                {
                    // Resolve by offset from cache
                    if (!offsetCache.TryGetValue(offset, out var obj))
                    {
                        throw new InvalidDataException("ofs-delta references unknown base object");
                    }
                    return obj;
                },
                cancellationToken).ConfigureAwait(false);

            var storedHash = await repository.ObjectStore.WriteObjectAsync(materialized.Type, materialized.Content, cancellationToken).ConfigureAwait(false);
            created.Add(storedHash);
            offsetCache[objectOffset] = materialized;
            hashCache[storedHash.Value] = materialized;
        }

        repository.InvalidateCaches();
        return created;
    }

    private static async Task<uint> ValidatePackFile(IGitRepository repository, FileStream fileStream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];

        var algorithm = GitHashHelper.GetAlgorithmName(repository.HashLengthBytes);

        using var hashingStream = new HashingReadStream(fileStream, algorithm, leaveOpen: true);
        
        await hashingStream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        ValidateHeader(header);

        var toRead = fileStream.Length - repository.HashLengthBytes - HeaderLength;
        var buffer = new byte[4096];
        while (toRead > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await hashingStream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading pack data");
            }
            toRead -= read;
        }

        var computedHash = hashingStream.CompleteHash();
        var trailer = new byte[repository.HashLengthBytes];
        await hashingStream.ReadExactlyAsync(trailer, cancellationToken).ConfigureAwait(false);
        if (!computedHash.AsSpan().SequenceEqual(trailer))
        {
            throw new InvalidDataException("Pack checksum mismatch");
        }
        
        return ReadUInt32(header.AsSpan(8, 4));
    }

    private static void ValidateHeader(ReadOnlySpan<byte> header)
    {
        if (header[0] != 'P' || header[1] != 'A' || header[2] != 'C' || header[3] != 'K')
        {
            throw new InvalidDataException("Invalid pack signature");
        }

        var version = ReadUInt32(header.Slice(4, 4));
        if (version != 2)
        {
            throw new NotSupportedException($"Unsupported pack version {version}");
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data)
        => (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
}
