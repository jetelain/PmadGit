using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Pack;

internal sealed class GitPackReader
{
    public async Task<IReadOnlyList<GitHash>> ReadAsync(GitRepository repository, Stream source, CancellationToken cancellationToken)
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

        using var hashingStream = new HashingReadStream(source, algorithm);
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

    private sealed class HashingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly IncrementalHash _hash;
        private bool _hashCompleted;

        public HashingReadStream(Stream inner, HashAlgorithmName algorithm)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _hash = IncrementalHash.CreateHash(algorithm);
        }

        public long BytesRead { get; private set; }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            if (read > 0 && !_hashCompleted)
            {
                _hash.AppendData(buffer, offset, read);
                BytesRead += read;
            }

            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            if (read > 0 && !_hashCompleted)
            {
                _hash.AppendData(buffer, offset, read);
                BytesRead += read;
            }

            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ReadAsync(buffer, cancellationToken, appendHash: !_hashCompleted);

        private async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken, bool appendHash)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0 && appendHash)
            {
                _hash.AppendData(buffer.Span[..read]);
                BytesRead += read;
            }

            return read;
        }

        public async Task ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            await _inner.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (!_hashCompleted)
            {
                _hash.AppendData(buffer);
                BytesRead += buffer.Length;
            }
        }

        public async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await _inner.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (!_hashCompleted)
            {
                _hash.AppendData(buffer.Span);
                BytesRead += buffer.Length;
            }
        }

        public async Task<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1);
            try
            {
                var read = await _inner.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return -1;
                }

                if (!_hashCompleted)
                {
                    _hash.AppendData(buffer, 0, 1);
                    BytesRead += 1;
                }

                return buffer[0];
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public byte[] CompleteHash()
        {
            _hashCompleted = true;
            return _hash.GetHashAndReset();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
