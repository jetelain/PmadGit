namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Defines the possible object kinds referenced by a git tree entry.
/// </summary>
public enum GitTreeEntryKind
{
    /// <summary>
    /// A blob (file) entry.
    /// </summary>
    Blob,

    /// <summary>
    /// A tree (directory) entry.
    /// </summary>
    Tree,

    /// <summary>
    /// A symbolic link entry.
    /// </summary>
    Symlink,

    /// <summary>
    /// A submodule entry.
    /// </summary>
    Submodule
}
