namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Specifies the behavior when resetting the current branch to a specific commit.
/// </summary>
public enum GitResetMode
{
    /// <summary>
    /// Updates the branch reference to point to the target commit.
    /// Leaves the staging area (.git/index) and the working tree completely untouched.
    /// </summary>
    Soft,

    /// <summary>
    /// Updates the branch reference and resets the staging area (.git/index) to match
    /// the target commit. Leaves the working tree untouched.
    /// </summary>
    Mixed,

    /// <summary>
    /// Updates the branch reference, resets the staging area (.git/index), and resets
    /// the working tree by discarding all modifications and restoring files from the target commit.
    /// </summary>
    Hard
}
