namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a file change detected between two git trees.
/// </summary>
/// <param name="Path">Repository-relative path using '/' separators.</param>
/// <param name="Kind">The kind of change (<see cref="GitChangeKind"/>).</param>
/// <param name="OldHash">The blob hash in the old tree, or <see langword="null"/> if the file was added.</param>
/// <param name="NewHash">The blob hash in the new tree, or <see langword="null"/> if the file was deleted.</param>
public sealed record GitTreeChange(
    string Path,
    GitChangeKind Kind,
    GitHash? OldHash,
    GitHash? NewHash);
