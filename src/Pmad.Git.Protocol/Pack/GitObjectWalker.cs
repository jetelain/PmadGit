using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Protocol.Pack;

/// <summary>
/// Traverses Git object graphs starting from root references to collect reachable objects for packfiles.
/// </summary>
public sealed class GitObjectWalker
{
    private readonly IGitRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitObjectWalker"/> class.
    /// </summary>
    /// <param name="repository">The Git repository to walk objects from.</param>
    public GitObjectWalker(IGitRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Traverses and collects all reachable objects starting from the specified root hashes.
    /// </summary>
    /// <param name="roots">The root object hashes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all visited unique object hashes in topological order.</returns>
    public Task<IReadOnlyList<GitHash>> CollectAsync(IEnumerable<GitHash> roots, CancellationToken cancellationToken)
        => CollectAsync(roots, null, cancellationToken);

    /// <summary>
    /// Traverses and collects all reachable objects starting from the specified root hashes,
    /// excluding objects reachable from the specified exclude roots.
    /// </summary>
    /// <param name="roots">The root object hashes.</param>
    /// <param name="excludes">Optional object hashes to exclude along with their reachable descendants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all visited unique object hashes in topological order.</returns>
    public async Task<IReadOnlyList<GitHash>> CollectAsync(IEnumerable<GitHash> roots, IEnumerable<GitHash>? excludes, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (excludes != null)
        {
            var excludeStack = new Stack<GitHash>(excludes);
            while (excludeStack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = excludeStack.Pop();
                if (!visited.Add(current.Value))
                {
                    continue;
                }

                GitObjectData data;
                try
                {
                    data = await _repository.ObjectStore.ReadObjectAsync(current, cancellationToken).ConfigureAwait(false);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }

                switch (data.Type)
                {
                    case GitObjectType.Commit:
                        var commit = GitCommit.Parse(current, data.Content);
                        excludeStack.Push(commit.Tree);
                        for (var i = commit.Parents.Count - 1; i >= 0; i--)
                        {
                            excludeStack.Push(commit.Parents[i]);
                        }
                        break;
                    case GitObjectType.Tree:
                        var tree = GitTree.Parse(current, data.Content, _repository.HashLengthBytes);
                        for (var i = tree.Entries.Count - 1; i >= 0; i--)
                        {
                            excludeStack.Push(tree.Entries[i].Hash);
                        }
                        break;
                    case GitObjectType.Tag:
                        var target = ParseTagTarget(data.Content);
                        if (target.HasValue)
                        {
                            excludeStack.Push(target.Value);
                        }
                        break;
                }
            }
        }

        var ordered = new List<GitHash>();
        var stack = new Stack<GitHash>(roots ?? throw new ArgumentNullException(nameof(roots)));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (!visited.Add(current.Value))
            {
                continue;
            }

            ordered.Add(current);
            var data = await _repository.ObjectStore.ReadObjectAsync(current, cancellationToken).ConfigureAwait(false);
            switch (data.Type)
            {
                case GitObjectType.Commit:
                    var commit = GitCommit.Parse(current, data.Content);
                    stack.Push(commit.Tree);
                    for (var i = commit.Parents.Count - 1; i >= 0; i--)
                    {
                        stack.Push(commit.Parents[i]);
                    }
                    break;
                case GitObjectType.Tree:
                    var tree = GitTree.Parse(current, data.Content, _repository.HashLengthBytes);
                    for (var i = tree.Entries.Count - 1; i >= 0; i--)
                    {
                        stack.Push(tree.Entries[i].Hash);
                    }
                    break;
                case GitObjectType.Tag:
                    var target = ParseTagTarget(data.Content);
                    if (target.HasValue)
                    {
                        stack.Push(target.Value);
                    }
                    break;
            }
        }

        return ordered;
    }

    private static GitHash? ParseTagTarget(ReadOnlySpan<byte> payload)
    {
        var span = payload;
        var newline = span.IndexOf((byte)'\n');
        while (newline >= 0)
        {
            var line = span.Slice(0, newline);
            var spaceIndex = line.IndexOf((byte)' ');
            if (spaceIndex > 0)
            {
                var key = Encoding.ASCII.GetString(line[..spaceIndex]);
                if (key.Equals("object", StringComparison.Ordinal))
                {
                    var value = Encoding.ASCII.GetString(line[(spaceIndex + 1)..]);
                    return GitHash.TryParse(value, out var hash) ? hash : null;
                }
            }

            span = span[(newline + 1)..];
            newline = span.IndexOf((byte)'\n');
        }

        return null;
    }
}

