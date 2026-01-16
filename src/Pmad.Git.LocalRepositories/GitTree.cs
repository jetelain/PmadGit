using System;
using System.Collections.Generic;
using System.Text;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents the contents of a git tree object.
/// </summary>
public sealed class GitTree
{
    /// <summary>
    /// Gets the hash that identifies this tree object.
    /// </summary>
    public GitHash Id { get; }

    /// <summary>
    /// Gets all entries contained within the tree.
    /// </summary>
    public IReadOnlyList<GitTreeEntry> Entries { get; }

    private GitTree(GitHash id, IReadOnlyList<GitTreeEntry> entries)
    {
        Id = id;
        Entries = entries;
    }

    /// <summary>
    /// Parses raw tree object content into a <see cref="GitTree"/> instance.
    /// </summary>
    /// <param name="id">Hash of the tree object being parsed.</param>
    /// <param name="content">Raw decompressed tree payload.</param>
    /// <param name="hashLengthBytes">Byte length of object ids contained in the tree.</param>
    /// <returns>The parsed <see cref="GitTree"/>.</returns>
    public static GitTree Parse(GitHash id, byte[] content, int hashLengthBytes = GitHash.Sha1ByteLength)
    {
        if (!GitHash.IsSupportedByteLength(hashLengthBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(hashLengthBytes), "Unsupported hash length for git tree entries");
        }

        var entries = new List<GitTreeEntry>();
        var span = content.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            var modeEnd = span[index..].IndexOf((byte)' ');
            if (modeEnd == -1)
            {
                throw new InvalidOperationException("Malformed tree entry: missing mode");
            }

            var modeSpan = span.Slice(index, modeEnd);
            index += modeEnd + 1;

            var nameEnd = span[index..].IndexOf((byte)0);
            if (nameEnd == -1)
            {
                throw new InvalidOperationException("Malformed tree entry: missing name terminator");
            }

            var nameSpan = span.Slice(index, nameEnd);
            index += nameEnd + 1;

            if (index + hashLengthBytes > span.Length)
            {
                throw new InvalidOperationException("Malformed tree entry: missing object id");
            }

            var objectSpan = span.Slice(index, hashLengthBytes);
            index += hashLengthBytes;

            var mode = ParseOctal(modeSpan);
            var name = Encoding.UTF8.GetString(nameSpan);
            var hash = GitHash.FromBytes(objectSpan);
            entries.Add(new GitTreeEntry(name, ResolveKind(mode), hash, mode));
        }

        return new GitTree(id, entries);
    }

    private static int ParseOctal(ReadOnlySpan<byte> span)
    {
        var value = 0;
        foreach (var b in span)
        {
            if (b < '0' || b > '7')
            {
                throw new InvalidOperationException("Tree entry mode must be octal");
            }

            value = (value << 3) + (b - '0');
        }

        return value;
    }

    private const int TreeMode = 16384;   // 040000 in octal
    private const int SubmoduleMode = 57344; // 160000 in octal
    private const int SymlinkMode = 40960;   // 120000 in octal

    private static GitTreeEntryKind ResolveKind(int mode) => mode switch
    {
        TreeMode => GitTreeEntryKind.Tree,
        SubmoduleMode => GitTreeEntryKind.Submodule,
        SymlinkMode => GitTreeEntryKind.Symlink,
        _ => GitTreeEntryKind.Blob
    };
}
