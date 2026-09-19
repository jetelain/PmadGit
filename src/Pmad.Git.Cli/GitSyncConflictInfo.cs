namespace Pmad.Git.Cli;

/// <summary>
/// Represents the current synchronization state of a <see cref="GitRepositorySynchronizer"/>.
/// </summary>
public enum GitSyncState
{
    /// <summary>
    /// No synchronization operation is currently running and there is no pending conflict.
    /// </summary>
    Idle,

    /// <summary>
    /// A push or pull operation is currently in progress.
    /// </summary>
    Syncing,

    /// <summary>
    /// A pull (or merge) operation stopped because of one or more conflicts that must be
    /// resolved before synchronization can resume. See <see cref="GitRepositorySynchronizer.Conflict"/>.
    /// </summary>
    Conflict,
}

/// <summary>
/// Describes a pending merge conflict that must be resolved before synchronization can resume.
/// </summary>
public sealed class GitSyncConflictInfo
{
    internal GitSyncConflictInfo(IReadOnlyList<string> conflictedFiles, DateTimeOffset detectedAt)
    {
        ConflictedFiles = conflictedFiles;
        DetectedAt = detectedAt;
    }

    /// <summary>
    /// Relative paths of the files currently in conflict.
    /// </summary>
    public IReadOnlyList<string> ConflictedFiles { get; }

    /// <summary>
    /// Time at which the conflict was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; }
}
