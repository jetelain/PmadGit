namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Defines the possible object kinds referenced by a git tree entry.
/// </summary>
public enum GitTreeEntryKind
{
    Blob,
    Tree,
    Symlink,
    Submodule
}
