namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Options controlling the behavior of move/rename operations in <see cref="IGitWorkspaceRepository"/>.
/// </summary>
public sealed class GitMoveOptions
{
    /// <summary>
    /// When <see langword="true"/>, forces renaming or moving even if the destination exists.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// When <see langword="true"/>, skips move or rename actions that would lead to an error condition.
    /// </summary>
    public bool SkipErrors { get; set; }

    /// <summary>
    /// When <see langword="true"/>, only plans and returns what would be moved without modifying the disk or index.
    /// </summary>
    public bool DryRun { get; set; }
}

/// <summary>
/// Represents a single file or path move item.
/// </summary>
/// <param name="SourcePath">The repository-relative source path.</param>
/// <param name="DestinationPath">The repository-relative destination path.</param>
public sealed record GitMoveItem(string SourcePath, string DestinationPath);

/// <summary>
/// Represents the result of a move or rename operation.
/// </summary>
public sealed class GitMoveResult
{
    /// <summary>
    /// Gets the list of items that were moved (or would be moved in dry-run mode).
    /// </summary>
    public IReadOnlyList<GitMoveItem> MovedItems { get; init; } = [];
}
