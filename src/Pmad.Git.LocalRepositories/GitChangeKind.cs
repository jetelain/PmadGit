namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Specifies the kind of change between two git trees.
/// </summary>
public enum GitChangeKind
{
    /// <summary>
    /// The file was added in the new tree.
    /// </summary>
    Added,

    /// <summary>
    /// The file was modified between the old and new trees.
    /// </summary>
    Modified,

    /// <summary>
    /// The file was deleted in the new tree.
    /// </summary>
    Deleted
}
