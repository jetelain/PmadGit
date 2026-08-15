namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Manages locks for git repository operations to prevent race conditions and data loss.
/// </summary>
/// <remarks>
/// A single instance can be shared between a <see cref="GitRepository"/> and other components
/// (e.g. a CLI-based wrapper) that operate on the same repository directory within the same
/// process, so that reference/object writes are properly synchronized between them.
/// </remarks>
public interface IGitRepositoryLockManager
{
    /// <summary>
    /// Acquires a lock for a specific reference (branch).
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (e.g., refs/heads/main).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after the operation completes.</returns>
    Task<IDisposable> AcquireReferenceLockAsync(string referencePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires locks for multiple references in a consistent order to prevent deadlocks.
    /// </summary>
    /// <param name="referencePaths">Fully qualified reference paths to lock.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    Task<IDisposable> AcquireMultipleReferenceLocksAsync(IEnumerable<string> referencePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires locks for all references in the repository.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    /// <remarks>
    /// This waits for all currently held (and in-flight) reference locks to be released, and prevents
    /// new reference locks from being acquired until the returned handle is disposed.
    /// </remarks>
    Task<IDisposable> LockAllAsync(CancellationToken cancellationToken = default);
}
