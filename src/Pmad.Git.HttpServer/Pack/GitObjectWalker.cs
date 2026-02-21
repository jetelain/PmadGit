using System.Text;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Pack;

internal sealed class GitObjectWalker
{
    private readonly IGitRepository _repository;

    public GitObjectWalker(IGitRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<GitHash>> CollectAsync(IEnumerable<GitHash> roots, CancellationToken cancellationToken)
    {
        var ordered = new List<GitHash>();
        var stack = new Stack<GitHash>(roots ?? throw new ArgumentNullException(nameof(roots)));
        var visited = new HashSet<string>(StringComparer.Ordinal);

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
