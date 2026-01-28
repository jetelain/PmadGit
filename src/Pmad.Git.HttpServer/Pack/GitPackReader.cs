using System.Security.Cryptography;
using Pmad.Git.HttpServer.Utilities;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Pack;

namespace Pmad.Git.HttpServer.Pack;

internal sealed class GitPackReader
{
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

        var algorithm = repository.HashLengthBytes switch
        {
            GitHash.Sha1ByteLength => HashAlgorithmName.SHA1,
            GitHash.Sha256ByteLength => HashAlgorithmName.SHA256,
            _ => throw new NotSupportedException("Unsupported git hash length")
        };

        using var hashingStream = new HashingReadStream(source, algorithm, leaveOpen: true);
        var header = new byte[12];
        await hashingStream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        ValidateHeader(header);
        var objectCount = ReadUInt32(header.AsSpan(8, 4));

        var created = new List<GitHash>(checked((int)objectCount));
        var offsetCache = new Dictionary<long, GitObjectData>();
        var hashCache = new Dictionary<string, GitObjectData>(StringComparer.Ordinal);

        for (var i = 0u; i < objectCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var objectOffset = hashingStream.BytesRead;

            // Use shared pack object reader
            var materialized = await GitPackObjectReader.ReadObjectAsync(
                hashingStream,
                objectOffset,
                repository.HashLengthBytes,
                async (hash, ct) =>
                {
                    // Try cache first, then repository
                    if (hashCache.TryGetValue(hash.Value, out var cached))
                    {
                        return cached;
                    }
                    return await repository.ReadObjectAsync(hash, ct).ConfigureAwait(false);
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

            var storedHash = await repository.WriteObjectAsync(materialized.Type, materialized.Content, cancellationToken).ConfigureAwait(false);
            created.Add(storedHash);
            offsetCache[objectOffset] = materialized;
            hashCache[storedHash.Value] = materialized;
        }

        var computedHash = hashingStream.CompleteHash();
        var trailer = new byte[repository.HashLengthBytes];
        await hashingStream.ReadExactlyAsync(trailer, cancellationToken).ConfigureAwait(false);
        if (!computedHash.AsSpan().SequenceEqual(trailer))
        {
            throw new InvalidDataException("Pack checksum mismatch");
        }

        repository.InvalidateCaches();
        return created;
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
