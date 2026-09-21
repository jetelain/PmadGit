using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Reads and writes the Git index file (.git/index) using the standard DIRC v2 format.
/// </summary>
public sealed class GitIndex
{
    private static readonly byte[] Magic = [(byte)'D', (byte)'I', (byte)'R', (byte)'C'];

    /// <summary>
    /// The default and supported Git index format version (version 2).
    /// </summary>
    public const int SupportedVersion = 2;

    /// <summary>
    /// Gets or sets the index format version.
    /// </summary>
    public int Version { get; set; } = SupportedVersion;

    /// <summary>
    /// Gets the list of entries contained in this index.
    /// </summary>
    public List<GitIndexEntry> Entries { get; } = new();

    /// <summary>
    /// Finds an index entry by relative path and stage.
    /// </summary>
    /// <param name="path">Repository-relative path using '/' separators.</param>
    /// <param name="stage">Stage number (0 for normal).</param>
    /// <returns>The matching <see cref="GitIndexEntry"/>, or null if not found.</returns>
    public GitIndexEntry? FindEntry(string path, int stage = 0)
    {
        var normalized = path.Replace('\\', '/');
        return Entries.FirstOrDefault(e => e.Path.Equals(normalized, StringComparison.Ordinal) && e.Stage == stage);
    }

    /// <summary>
    /// Adds or updates an index entry for the same path and stage.
    /// </summary>
    /// <param name="entry">The entry to add or update.</param>
    public void AddOrUpdate(GitIndexEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Path.Equals(entry.Path, StringComparison.Ordinal) && Entries[i].Stage == entry.Stage)
            {
                Entries[i] = entry;
                return;
            }
        }
        Entries.Add(entry);
    }

    /// <summary>
    /// Removes an entry by path and stage.
    /// </summary>
    /// <param name="path">Repository-relative path using '/' separators.</param>
    /// <param name="stage">Stage number (0 for normal).</param>
    /// <returns>True if one or more entries were removed, false otherwise.</returns>
    public bool Remove(string path, int stage = 0)
    {
        var normalized = path.Replace('\\', '/');
        return Entries.RemoveAll(e => e.Path.Equals(normalized, StringComparison.Ordinal) && e.Stage == stage) > 0;
    }

    /// <summary>
    /// Parses a Git index from raw byte data.
    /// </summary>
    /// <param name="data">The raw bytes of the index file.</param>
    /// <param name="hashLengthBytes">Hash length in bytes (20 for SHA-1, 32 for SHA-256).</param>
    /// <returns>The parsed <see cref="GitIndex"/>.</returns>
    public static GitIndex FromBytes(byte[] data, int hashLengthBytes = GitHash.Sha1ByteLength)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        return FromSpan(data.AsSpan(), hashLengthBytes);
    }

    /// <summary>
    /// Parses a Git index from a read-only span of bytes.
    /// </summary>
    /// <param name="data">The raw span of the index file.</param>
    /// <param name="hashLengthBytes">Hash length in bytes (20 for SHA-1, 32 for SHA-256).</param>
    /// <returns>The parsed <see cref="GitIndex"/>.</returns>
    public static GitIndex FromSpan(ReadOnlySpan<byte> data, int hashLengthBytes = GitHash.Sha1ByteLength)
    {
        if (data.Length < 12 + hashLengthBytes)
        {
            throw new InvalidDataException($"Index data is too short ({data.Length} bytes).");
        }

        // Validate checksum
        var algorithmName = GitHashHelper.GetAlgorithmName(hashLengthBytes);
        using (var hashAlgo = IncrementalHash.CreateHash(algorithmName))
        {
            hashAlgo.AppendData(data.Slice(0, data.Length - hashLengthBytes));
            Span<byte> computedChecksum = stackalloc byte[hashLengthBytes];
            if (!hashAlgo.TryGetHashAndReset(computedChecksum, out _))
            {
                throw new InvalidOperationException("Failed to compute index checksum.");
            }
            var storedChecksum = data.Slice(data.Length - hashLengthBytes, hashLengthBytes);
            if (!storedChecksum.SequenceEqual(computedChecksum))
            {
                throw new InvalidDataException("Index file checksum mismatch.");
            }
        }

        // Header
        if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2] || data[3] != Magic[3])
        {
            throw new InvalidDataException("Invalid index file signature (expected DIRC).");
        }

        var version = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
        if (version != 2 && version != 3)
        {
            throw new NotSupportedException($"Index version {version} is not supported. Only versions 2 and 3 are supported.");
        }

        var entryCount = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4));
        var index = new GitIndex { Version = version };

        var offset = 12;
        var fixedHeaderLength = 40 + hashLengthBytes + 2;

        for (var i = 0; i < entryCount; i++)
        {
            if (offset + fixedHeaderLength > data.Length - hashLengthBytes)
            {
                throw new InvalidDataException($"Unexpected end of file while reading entry {i}.");
            }

            var ctimeSec = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            var ctimeNano = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4, 4));
            var mtimeSec = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 8, 4));
            var mtimeNano = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 12, 4));
            var dev = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 16, 4));
            var ino = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 20, 4));
            var fileMode = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 24, 4));
            var uid = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 28, 4));
            var gid = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 32, 4));
            var fileSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 36, 4));
            var hash = GitHash.FromBytes(data.Slice(offset + 40, hashLengthBytes));
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 40 + hashLengthBytes, 2));

            ushort extendedFlags = 0;
            var currentHeaderLength = fixedHeaderLength;
            if (version >= 3 && (flags & 0x4000) != 0)
            {
                if (offset + fixedHeaderLength + 2 > data.Length - hashLengthBytes)
                {
                    throw new InvalidDataException($"Unexpected end of file while reading extended flags for entry {i}.");
                }
                extendedFlags = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + fixedHeaderLength, 2));
                currentHeaderLength += 2;
            }

            // Path starts immediately after header
            var pathStart = offset + currentHeaderLength;
            var pathEnd = pathStart;
            while (pathEnd < data.Length - hashLengthBytes && data[pathEnd] != 0)
            {
                pathEnd++;
            }

            if (pathEnd >= data.Length - hashLengthBytes)
            {
                throw new InvalidDataException($"Unterminated path string in index entry {i}.");
            }

            var pathLength = pathEnd - pathStart;
            var path = Encoding.UTF8.GetString(data.Slice(pathStart, pathLength));

            var entrySize = (currentHeaderLength + pathLength + 8) & ~7;
            offset += entrySize;

            index.Entries.Add(new GitIndexEntry(
                path,
                hash,
                fileMode,
                fileSize,
                mtimeSec,
                mtimeNano,
                ctimeSec,
                ctimeNano,
                dev,
                ino,
                uid,
                gid,
                flags,
                extendedFlags));
        }

        return index;
    }

    /// <summary>
    /// Serializes the index to a byte array using standard DIRC v2 format with trailing checksum.
    /// </summary>
    /// <param name="hashLengthBytes">Hash length in bytes (20 for SHA-1, 32 for SHA-256).</param>
    /// <returns>Byte array representing the binary index file.</returns>
    public byte[] ToByteArray(int hashLengthBytes = GitHash.Sha1ByteLength)
    {
        Entries.Sort((a, b) =>
        {
            var cmp = string.Compare(a.Path, b.Path, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return a.Stage.CompareTo(b.Stage);
        });

        var algorithmName = GitHashHelper.GetAlgorithmName(hashLengthBytes);
        using var ms = new MemoryStream();
        byte[] checksum;
        using (var hashingStream = new HashingWriteStream(ms, algorithmName, leaveOpen: true))
        {
            // Check if any entry requires extended flags (version 3)
            var hasExtendedEntries = Entries.Any(e => (e.Flags & 0x4000) != 0 || e.ExtendedFlags != 0);
            var effectiveVersion = (Version >= 3 || hasExtendedEntries) ? Math.Max(Version, 3) : Version;

            // Write 12-byte header
            Span<byte> header = stackalloc byte[12];
            Magic.CopyTo(header);
            BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)effectiveVersion);
            BinaryPrimitives.WriteUInt32BigEndian(header.Slice(8, 4), (uint)Entries.Count);
            hashingStream.Write(header);

            // Write entries
            var fixedHeaderLength = 40 + hashLengthBytes + 2;
            var entryHeader = new byte[fixedHeaderLength + 2];

            foreach (var entry in Entries)
            {
                var hashBytes = entry.Hash.ToByteArray();
                if (hashBytes.Length != hashLengthBytes)
                {
                    throw new InvalidOperationException(
                        $"Entry '{entry.Path}' has a {hashBytes.Length}-byte hash but the index was opened with hashLengthBytes={hashLengthBytes}. " +
                        "All entries must use the same hash algorithm as the index.");
                }

                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(0, 4), entry.CtimeSeconds);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(4, 4), entry.CtimeNanoseconds);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(8, 4), entry.MtimeSeconds);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(12, 4), entry.MtimeNanoseconds);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(16, 4), entry.Dev);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(20, 4), entry.Ino);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(24, 4), (uint)entry.FileMode);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(28, 4), entry.Uid);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(32, 4), entry.Gid);
                BinaryPrimitives.WriteUInt32BigEndian(entryHeader.AsSpan(36, 4), entry.FileSize);

                hashBytes.CopyTo(entryHeader, 40);

                var pathBytes = Encoding.UTF8.GetBytes(entry.Path);
                var pathLen = (ushort)Math.Min(pathBytes.Length, 0xFFF);
                var isExtended = effectiveVersion >= 3 && ((entry.Flags & 0x4000) != 0 || entry.ExtendedFlags != 0);

                var flags = (ushort)((entry.Flags & 0x8000) | ((entry.Stage & 0x3) << 12) | pathLen);
                if (isExtended)
                {
                    flags |= 0x4000;
                }

                BinaryPrimitives.WriteUInt16BigEndian(entryHeader.AsSpan(40 + hashLengthBytes, 2), flags);

                var currentHeaderLength = fixedHeaderLength;
                if (isExtended)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(entryHeader.AsSpan(fixedHeaderLength, 2), entry.ExtendedFlags);
                    currentHeaderLength += 2;
                }

                hashingStream.Write(entryHeader, 0, currentHeaderLength);
                hashingStream.Write(pathBytes, 0, pathBytes.Length);

                // Padding to 8-byte boundary relative to entry start
                var totalLen = (currentHeaderLength + pathBytes.Length + 8) & ~7;
                var padLen = totalLen - (currentHeaderLength + pathBytes.Length);
                if (padLen > 0)
                {
                    var pad = new byte[padLen]; // All zeros
                    hashingStream.Write(pad, 0, padLen);
                }
            }

            checksum = hashingStream.CompleteHash();
        }

        ms.Write(checksum, 0, checksum.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads and parses a Git index file (.git/index) from disk.
    /// </summary>
    /// <param name="indexPath">Absolute path to the .git/index file.</param>
    /// <param name="hashLengthBytes">Hash length in bytes (20 for SHA-1, 32 for SHA-256).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The parsed <see cref="GitIndex"/>.</returns>
    public static async Task<GitIndex> ReadAsync(string indexPath, int hashLengthBytes = GitHash.Sha1ByteLength, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(indexPath))
        {
            return new GitIndex();
        }

        var data = await File.ReadAllBytesAsync(indexPath, cancellationToken).ConfigureAwait(false);
        return FromBytes(data, hashLengthBytes);
    }

    /// <summary>
    /// Writes the index to disk using the standard DIRC v2 format with trailing checksum.
    /// Atomically replaces the file if it already exists.
    /// </summary>
    /// <param name="indexPath">Absolute path to the destination index file.</param>
    /// <param name="hashLengthBytes">Hash length in bytes (20 for SHA-1, 32 for SHA-256).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    public async Task WriteAsync(string indexPath, int hashLengthBytes = GitHash.Sha1ByteLength, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? ".", $"{Path.GetFileName(indexPath)}.{Guid.NewGuid():N}.tmp");
        var bytes = ToByteArray(hashLengthBytes);

        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, indexPath, overwrite: true);
    }
}
