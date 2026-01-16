using System;
using System.Collections.Generic;
using System.Text;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a parsed git commit object with tree, parent and metadata information.
/// </summary>
/// <param name="Id">The hash that uniquely identifies the commit.</param>
/// <param name="Tree">The hash of the root tree for the commit.</param>
/// <param name="Parents">The list of parent commits recorded in the object.</param>
/// <param name="Headers">All header fields preserved as raw key/value pairs.</param>
/// <param name="Message">The commit message body.</param>
public sealed record GitCommit(
    GitHash Id,
    GitHash Tree,
    IReadOnlyList<GitHash> Parents,
    IReadOnlyDictionary<string, string> Headers,
    string Message)
{
    /// <summary>
    /// Gets the raw author header, if present.
    /// </summary>
    public string? Author => Headers.TryGetValue("author", out var value) ? value : null;

    /// <summary>
    /// Gets the raw committer header, if present.
    /// </summary>
    public string? Committer => Headers.TryGetValue("committer", out var value) ? value : null;

    /// <summary>
    /// Parses raw commit object content into a <see cref="GitCommit"/> instance.
    /// </summary>
    /// <param name="id">The hash associated with the commit object.</param>
    /// <param name="content">Raw decompressed commit payload.</param>
    /// <returns>The parsed <see cref="GitCommit"/>.</returns>
    public static GitCommit Parse(GitHash id, byte[] content)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var parents = new List<GitHash>();
        GitHash? tree = null;

        var span = content.AsSpan();
        var separator = FindDoubleNewLine(span);
        if (separator == -1)
        {
            separator = span.Length;
        }

        var headerSpan = span[..separator];
        var messageStart = Math.Min(separator + 2, span.Length);
        var messageSpan = messageStart < span.Length ? span[messageStart..] : ReadOnlySpan<byte>.Empty;

        var index = 0;
        while (index < headerSpan.Length)
        {
            var endOfLine = headerSpan[index..].IndexOf((byte)'\n');
            if (endOfLine == -1)
            {
                endOfLine = headerSpan.Length - index;
            }

            var line = headerSpan.Slice(index, endOfLine);
            var separatorIndex = line.IndexOf((byte)' ');
            if (separatorIndex > 0)
            {
                var key = Encoding.UTF8.GetString(line[..separatorIndex]);
                var value = Encoding.UTF8.GetString(line[(separatorIndex + 1)..]);
                if (key.Equals("tree", StringComparison.Ordinal))
                {
                    tree = new GitHash(value);
                }
                else if (key.Equals("parent", StringComparison.Ordinal))
                {
                    parents.Add(new GitHash(value));
                }
                else
                {
                    headers[key] = value;
                }
            }

            index += endOfLine + 1;
        }

        if (tree is null)
        {
            throw new InvalidOperationException($"Commit {id} does not contain a tree reference");
        }

        var message = messageSpan.Length == 0 ? string.Empty : Encoding.UTF8.GetString(messageSpan);
        return new GitCommit(id, tree.Value, parents, headers, message);
    }

    private static int FindDoubleNewLine(ReadOnlySpan<byte> span)
    {
        for (var i = 0; i < span.Length - 1; i++)
        {
            if (span[i] == '\n' && span[i + 1] == '\n')
            {
                return i;
            }
        }

        return -1;
    }
}
