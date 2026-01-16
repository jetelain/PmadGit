namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a tree entry paired with its path within a traversal.
/// </summary>
/// <param name="Path">Full path of the entry relative to the traversal root.</param>
/// <param name="Entry">The tree entry metadata.</param>
public sealed record GitTreeItem(string Path, GitTreeEntry Entry);
