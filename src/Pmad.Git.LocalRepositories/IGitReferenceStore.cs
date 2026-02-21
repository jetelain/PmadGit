namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Provides read and write access to git references (branches, tags, etc.).
/// </summary>
public interface IGitReferenceStore
{
    /// <summary>
    /// Returns a snapshot of all references stored in the repository.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A dictionary keyed by fully qualified reference names.</returns>
    Task<IReadOnlyDictionary<string, GitHash>> GetReferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to resolve a fully qualified reference path to a hash.
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (e.g. refs/heads/main).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash if the reference exists, otherwise null.</returns>
    Task<GitHash?> TryResolveReferenceAsync(string referencePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves HEAD to the target commit hash.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash pointed to by HEAD.</returns>
    Task<GitHash> ResolveHeadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes or overwrites the value of a reference file with validation.
    /// This method is thread-safe and acquires the necessary lock internally.
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (for example refs/heads/main).</param>
    /// <param name="expectedOldValue">Expected current hash of the reference, or null if reference should not exist.</param>
    /// <param name="newValue">New hash to persist, or null to delete the reference.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the expected old value doesn't match the current value.</exception>
    Task WriteReferenceWithValidationAsync(
        string referencePath,
        GitHash? expectedOldValue,
        GitHash? newValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires locks for multiple references in a consistent order to prevent deadlocks.
    /// This is used for batch reference updates like git push.
    /// </summary>
    /// <param name="referencePaths">Fully qualified reference paths to lock.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    Task<IGitMultipleReferenceLocks> AcquireMultipleReferenceLocksAsync(
        IEnumerable<string> referencePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the cached reference index so that subsequent reads reflect the current state.
    /// </summary>
    void InvalidateCaches();
}
