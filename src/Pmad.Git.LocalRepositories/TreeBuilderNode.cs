namespace Pmad.Git.LocalRepositories;

internal sealed class TreeBuilderNode
{
    public Dictionary<string, TreeBuilderNode> Directories { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, TreeLeaf> Leaves { get; } = new(StringComparer.Ordinal);
}
