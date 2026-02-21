namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Provides a mechanism to perform multiple atomic reference updates while holding locks
/// on all affected references. This prevents concurrent modifications and ensures consistency
/// during batch operations such as git push.
/// </summary>
/// <remarks>
/// This class ensures that:
/// - All reference locks are held for the duration of the operations
/// - Only references that were locked can be modified
/// - Cache invalidation happens correctly after each update
/// - Proper cleanup occurs even if operations fail
/// </remarks>
internal sealed class GitMultipleReferenceLocks : IGitMultipleReferenceLocks
{
    private readonly GitReferenceStore _referenceStore;
    private readonly HashSet<string> _normalizedPaths;
    private readonly IDisposable _lockDisposable;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitMultipleReferenceLocks"/> class.
    /// </summary>
    /// <param name="referenceStore">The reference store on which operations will be performed.</param>
    /// <param name="normalizedPaths">List of normalized reference paths that have been locked.</param>
    /// <param name="lockDisposable">The underlying lock handle that will be released on disposal.</param>
    public GitMultipleReferenceLocks(GitReferenceStore referenceStore, List<string> normalizedPaths, IDisposable lockDisposable)
    {
        _referenceStore = referenceStore;
        _normalizedPaths = normalizedPaths.ToHashSet(StringComparer.Ordinal);
        _lockDisposable = lockDisposable;
    }

    /// <summary>
    /// Releases all acquired locks for the references.
    /// </summary>
    /// <remarks>
    /// This method is idempotent and can be called multiple times safely.
    /// After disposal, no further write operations can be performed using this instance.
    /// </remarks>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _lockDisposable.Dispose();
        }
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the reference was not locked by this instance.</exception>
    public Task WriteReferenceWithValidationAsync(string referencePath, GitHash? expectedOldValue, GitHash? newValue, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GitMultipleReferenceLocks));
        }

        var normalized = GitReferenceStore.NormalizeAbsoluteReferencePath(referencePath);
        if (!_normalizedPaths.Contains(normalized))
        {
            throw new InvalidOperationException($"The reference '{referencePath}' is not locked by this lock instance.");
        }

        return _referenceStore.WriteReferenceWithValidationInternalAsync(normalized, expectedOldValue, newValue, cancellationToken);
    }
}