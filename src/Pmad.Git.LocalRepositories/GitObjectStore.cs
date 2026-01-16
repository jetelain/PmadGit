using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Pmad.Git.LocalRepositories;

internal sealed class GitObjectStore
{
	private readonly string _gitDirectory;
	private readonly Dictionary<GitHash, GitObjectData> _cache = new();
	private readonly object _cacheLock = new();
	private readonly Task<List<PackEntry>> _packsTask;
	private readonly int _hashLengthBytes;

	public GitObjectStore(string gitDirectory)
	{
		_gitDirectory = gitDirectory;
		_hashLengthBytes = DetectHashLength(gitDirectory);
		_packsTask = LoadPackEntriesAsync();
	}

	/// <summary>
	/// Gets the number of bytes used to represent object hashes in this repository.
	/// </summary>
	public int HashLengthBytes => _hashLengthBytes;

	public async Task<GitObjectData> ReadObjectAsync(GitHash hash, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		lock (_cacheLock)
		{
			if (_cache.TryGetValue(hash, out var cached))
			{
				return cached;
			}
		}

		var result = await ReadObjectNoCacheAsync(hash, cancellationToken).ConfigureAwait(false);

		lock (_cacheLock)
		{
			_cache[hash] = result;
		}

		return result;
	}

    public async Task<GitObjectData> ReadObjectNoCacheAsync(GitHash hash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var loose = await TryReadLooseObjectAsync(hash, cancellationToken).ConfigureAwait(false);
        if (loose is not null)
        {
            return loose;
        }

        var resolver = new Func<GitHash, CancellationToken, Task<GitObjectData>>(ReadObjectNoCacheAsync);
        var packs = await _packsTask.ConfigureAwait(false);

        foreach (var pack in packs)
        {
            var packed = await pack.TryReadObject(hash, resolver, cancellationToken).ConfigureAwait(false);
            if (packed is not null)
            {
                return packed;
            }
        }

        throw new FileNotFoundException($"Git object {hash} could not be found");
    }


    private async Task<GitObjectData?> TryReadLooseObjectAsync(GitHash hash, CancellationToken cancellationToken)
	{
		var path = Path.Combine(_gitDirectory, "objects", hash.Value[..2], hash.Value[2..]);
		if (!File.Exists(path))
		{
			return null;
		}

		var options = new FileStreamOptions
		{
			Mode = FileMode.Open,
			Access = FileAccess.Read,
			Share = FileShare.Read,
			Options = FileOptions.Asynchronous | FileOptions.SequentialScan
		};

		await using var stream = new FileStream(path, options);
		using var zlib = new ZLibStream(stream, CompressionMode.Decompress);
		using var buffer = new MemoryStream();
		await zlib.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

		var content = buffer.ToArray();
		var separator = Array.IndexOf(content, (byte)0);
		if (separator < 0)
		{
			throw new InvalidDataException("Invalid loose object: missing header");
		}

		var header = System.Text.Encoding.ASCII.GetString(content, 0, separator);
		var spaceIndex = header.IndexOf(' ');
		if (spaceIndex < 0)
		{
			throw new InvalidDataException("Invalid loose object header");
		}

		var typeString = header[..spaceIndex];
		var payload = content[(separator + 1)..];
		return new GitObjectData(ParseType(typeString), payload);
	}

	private async Task<List<PackEntry>> LoadPackEntriesAsync()
	{
		var packDir = Path.Combine(_gitDirectory, "objects", "pack");
		if (!Directory.Exists(packDir))
		{
			return new List<PackEntry>();
		}

		var creationTasks = new List<Task<PackEntry>>();
		foreach (var idxPath in Directory.GetFiles(packDir, "*.idx"))
		{
			var packPath = Path.ChangeExtension(idxPath, ".pack");
			if (File.Exists(packPath))
			{
				creationTasks.Add(PackEntry.CreateAsync(idxPath, packPath, _hashLengthBytes));
			}
		}

		if (creationTasks.Count == 0)
		{
			return new List<PackEntry>();
		}

		var entries = await Task.WhenAll(creationTasks).ConfigureAwait(false);
		return new List<PackEntry>(entries);
	}

	private static int DetectHashLength(string gitDirectory)
	{
		var configPath = Path.Combine(gitDirectory, "config");
		if (File.Exists(configPath))
		{
			var format = ReadObjectFormat(configPath);
			if (string.Equals(format, "sha256", StringComparison.OrdinalIgnoreCase))
			{
				return GitHash.Sha256ByteLength;
			}
		}

		return GitHash.Sha1ByteLength;
	}

	private static string? ReadObjectFormat(string configPath)
	{
		string? currentSection = null;
		foreach (var rawLine in File.ReadLines(configPath))
		{
			var line = rawLine.Trim();
			if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
			{
				continue;
			}

			if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
			{
				currentSection = line[1..^1].Trim();
				continue;
			}

			var separator = line.IndexOf('=');
			if (separator < 0)
			{
				continue;
			}

			var key = line[..separator].Trim();
			var value = line[(separator + 1)..].Trim();
			if (string.Equals(currentSection, "extensions", StringComparison.OrdinalIgnoreCase) &&
			    string.Equals(key, "objectformat", StringComparison.OrdinalIgnoreCase))
			{
				return value;
			}
		}

		return null;
	}

    private static GitObjectType ParseType(string type) => type switch
    {
        "commit" => GitObjectType.Commit,
        "tree" => GitObjectType.Tree,
        "blob" => GitObjectType.Blob,
        "tag" => GitObjectType.Tag,
        _ => throw new NotSupportedException($"Unsupported git object type '{type}'")
    };

	private sealed class PackEntry
	{
		private readonly PackIndex _index;
		private readonly string _packPath;
		private readonly int _hashLengthBytes;

		private PackEntry(string packPath, int hashLengthBytes, PackIndex index)
		{
			_packPath = packPath;
			_hashLengthBytes = hashLengthBytes;
			_index = index;
		}

		public static async Task<PackEntry> CreateAsync(
			string idxPath,
			string packPath,
			int hashLengthBytes,
			CancellationToken cancellationToken = default)
		{
			var index = await PackIndex.LoadAsync(idxPath, hashLengthBytes, cancellationToken).ConfigureAwait(false);
			var entry = new PackEntry(packPath, hashLengthBytes, index);
			entry.ValidatePackFile();
			return entry;
		}

		public async Task<GitObjectData?> TryReadObject(
			GitHash hash,
			Func<GitHash, CancellationToken, Task<GitObjectData>> resolve,
			CancellationToken cancellationToken)
		{
			if (!_index.TryGetOffset(hash, out var offset))
			{
				return null;
			}

			return await ReadAtOffset(offset, resolve, cancellationToken).ConfigureAwait(false);
		}

		private async Task<GitObjectData> ReadAtOffset(
			long offset,
			Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
			CancellationToken cancellationToken)
		{
			var options = new FileStreamOptions
			{
				Mode = FileMode.Open,
				Access = FileAccess.Read,
				Share = FileShare.Read,
				Options = FileOptions.Asynchronous | FileOptions.SequentialScan
			};

			await using var stream = new FileStream(_packPath, options);
			return await ReadObject(stream, offset, resolveByHash, cancellationToken).ConfigureAwait(false);
		}

		private async Task<GitObjectData> ReadObject(
			FileStream stream,
			long offset,
			Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
			CancellationToken cancellationToken)
		{
			stream.Position = offset;
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
				6 => await ReadOfsDelta(stream, offset, resolveByHash, cancellationToken).ConfigureAwait(false),
				7 => await ReadRefDelta(stream, resolveByHash, cancellationToken).ConfigureAwait(false),
				_ => throw new NotSupportedException($"Unsupported pack object kind {kind}")
			};
		}

		private async Task<GitObjectData> ReadOfsDelta(
			FileStream stream,
			long currentOffset,
			Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
			CancellationToken cancellationToken)
		{
			var baseDistance = await ReadOffsetAsync(stream, cancellationToken).ConfigureAwait(false);
			var baseOffset = currentOffset - baseDistance;
			var dataOffset = stream.Position;
			var baseObject = await ReadObject(stream, baseOffset, resolveByHash, cancellationToken).ConfigureAwait(false);
			stream.Position = dataOffset;
			var deltaPayload = await ReadZLibAsync(stream, cancellationToken).ConfigureAwait(false);
			return ApplyDelta(baseObject, deltaPayload);
		}

		private async Task<GitObjectData> ReadRefDelta(
			FileStream stream,
			Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
			CancellationToken cancellationToken)
		{
			var baseHash = new byte[_hashLengthBytes];
			await stream.ReadExactlyAsync(baseHash, 0, baseHash.Length, cancellationToken).ConfigureAwait(false);
			var baseObject = await resolveByHash(GitHash.FromBytes(baseHash), cancellationToken).ConfigureAwait(false);
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

		private static long ReadVariableLength(ReadOnlySpan<byte> data, ref int cursor)
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

		private static async Task<long> ReadOffsetAsync(Stream stream, CancellationToken cancellationToken)
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

		private static async Task<byte[]> ReadZLibAsync(Stream stream, CancellationToken cancellationToken)
		{
			using var zlib = new ZLibStream(stream, CompressionMode.Decompress, leaveOpen: true);
			using var buffer = new MemoryStream();
			await zlib.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
			return buffer.ToArray();
		}

		private static async ValueTask<(int kind, long size)> ReadTypeAndSizeAsync(Stream stream, CancellationToken cancellationToken)
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

		private static async ValueTask<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
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

        private void ValidatePackFile()
        {
            using var stream = new FileStream(_packPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);
            if (header[0] != 'P' || header[1] != 'A' || header[2] != 'C' || header[3] != 'K')
            {
                throw new InvalidDataException($"Pack file '{_packPath}' does not start with PACK signature");
            }
        }
    }

	private sealed class PackIndex
	{
		private readonly Dictionary<GitHash, long> _offsets;

		private PackIndex(Dictionary<GitHash, long> offsets)
		{
			_offsets = offsets;
		}

		public bool TryGetOffset(GitHash hash, out long offset) => _offsets.TryGetValue(hash, out offset);

		public static async Task<PackIndex> LoadAsync(
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

		private static async Task<PackIndex> LoadVersion2Async(
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

			return new PackIndex(map);
		}

		private static async Task<PackIndex> LoadVersion1Async(
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

			return new PackIndex(map);
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

}
