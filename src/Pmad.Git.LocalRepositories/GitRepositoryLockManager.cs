using System.Collections.Concurrent;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Manages locks for git repository operations to prevent race conditions and data loss.
/// </summary>
internal sealed class GitRepositoryLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _referenceLocks = new(StringComparer.Ordinal);
    private readonly object _lockCreationLock = new();

    /// <summary>
    /// Acquires a lock for a specific reference (branch).
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (e.g., refs/heads/main).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after the operation completes.</returns>
    public async Task<IDisposable> AcquireReferenceLockAsync(string referencePath, CancellationToken cancellationToken = default)
    {
        var semaphore = GetSemaphore(referencePath);
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LockHandle(semaphore);
    }

    private SemaphoreSlim GetSemaphore(string referencePath)
    {
        if (!_referenceLocks.TryGetValue(referencePath, out var semaphore))
        {
            lock (_lockCreationLock)
            {
                semaphore = _referenceLocks.GetOrAdd(referencePath, static _ => new SemaphoreSlim(1, 1));
            }
        }
        return semaphore;
    }

    /// <summary>
    /// Acquires locks for multiple references in a consistent order to prevent deadlocks.
    /// </summary>
    /// <param name="referencePaths">Fully qualified reference paths to lock.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    public async Task<IDisposable> AcquireMultipleReferenceLocksAsync(IEnumerable<string> referencePaths, CancellationToken cancellationToken = default)
    {
        if (referencePaths is null)
        {
            throw new ArgumentNullException(nameof(referencePaths));
        }

        // Deduplicate and sort paths to prevent deadlocks
        var orderedPaths = referencePaths.Distinct(StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal).ToList();
        var semaphores = new List<SemaphoreSlim>(orderedPaths.Count);
        
        try
        {
            foreach (var path in orderedPaths)
            {
                var semaphore = GetSemaphore(path);
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                semaphores.Add(semaphore);
            }
            
            return new MultipleLockHandle(semaphores);
        }
        catch
        {
            foreach (var semaphore in semaphores)
            {
                semaphore.Release();
            }
            throw;
        }
    }

    private sealed class LockHandle : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public LockHandle(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semaphore.Release();
            }
        }
    }

    private sealed class MultipleLockHandle : IDisposable
    {
        private readonly List<SemaphoreSlim> _semaphores;
        private bool _disposed;

        public MultipleLockHandle(List<SemaphoreSlim> semaphores)
        {
            _semaphores = semaphores;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                foreach (var semaphore in _semaphores)
                {
                    semaphore.Release();
                }
            }
        }
    }
}
