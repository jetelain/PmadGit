using System.Collections.Concurrent;
using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Manages locks for git repository operations to prevent race conditions and data loss.
/// </summary>
/// <remarks>
/// Instances can be shared between a <see cref="GitRepository"/> and other components (e.g. a CLI-based
/// wrapper) operating on the same repository directory within the same process, in order to synchronize
/// their access to references/objects. See <see cref="IGitRepositoryLockManager"/>.
/// </remarks>
public sealed class GitRepositoryLockManager : IGitRepositoryLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _referenceLocks = new(StringComparer.Ordinal);

    // These fields implement a simple async reader-writer lock:
    // - Reference-level locks ("readers") can be held concurrently for different references.
    // - LockAllAsync ("writer") waits for all in-flight reference-level locks to complete and
    //   blocks new ones from starting until it is released.
    private readonly SemaphoreSlim _globalLock = new(1, 1);
    private readonly SemaphoreSlim _readerCountLock = new(1, 1);
    private int _activeReaders;

    /// <summary>
    /// Acquires a lock for a specific reference (branch).
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (e.g., refs/heads/main).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after the operation completes.</returns>
    public async Task<IDisposable> AcquireReferenceLockAsync(string referencePath, CancellationToken cancellationToken = default)
    {
        if (referencePath is null)
        {
            throw new ArgumentNullException(nameof(referencePath));
        }

        await EnterReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var semaphore = GetSemaphore(referencePath);
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new LockHandle(semaphore, this);
        }
        catch
        {
            ExitRead();
            throw;
        }
    }

    /// <summary>
    /// Acquires locks for all references in the repository.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    /// <remarks>
    /// This waits for all currently held (and in-flight) reference locks to be released, and prevents
    /// new reference locks from being acquired until the returned handle is disposed.
    /// </remarks>
    public async Task<IDisposable> LockAllAsync(CancellationToken cancellationToken = default)
    {
        await _globalLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new GlobalLockHandle(_globalLock);
    }

    /// <summary>
    /// Marks the beginning of a reference-level lock operation ("read" access), blocking while a
    /// <see cref="LockAllAsync"/> operation ("write" access) is in progress.
    /// </summary>
    private async Task EnterReadAsync(CancellationToken cancellationToken)
    {
        await _readerCountLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (++_activeReaders == 1)
            {
                await _globalLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _readerCountLock.Release();
        }
    }

    /// <summary>
    /// Marks the end of a reference-level lock operation, releasing the global lock once no
    /// reference-level operations remain in-flight.
    /// </summary>
    private void ExitRead()
    {
        _readerCountLock.Wait();
        try
        {
            if (--_activeReaders == 0)
            {
                _globalLock.Release();
            }
        }
        finally
        {
            _readerCountLock.Release();
        }
    }

    /// <summary>
    /// Gets or creates a semaphore for the specified reference path.
    /// </summary>
    /// <param name="referencePath">The fully qualified reference path.</param>
    /// <returns>A semaphore used to control access to the reference.</returns>
    /// <remarks>
    /// This method is thread-safe and ensures only one semaphore exists per reference path.
    /// </remarks>
    private SemaphoreSlim GetSemaphore(string referencePath)
    {
        return _referenceLocks.GetOrAddSingleton(referencePath, static _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Acquires locks for multiple references in a consistent order to prevent deadlocks.
    /// </summary>
    /// <param name="referencePaths">Fully qualified reference paths to lock.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>A disposable lock that must be released after all operations complete.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="referencePaths"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via <paramref name="cancellationToken"/>.</exception>
    /// <remarks>
    /// This method ensures deadlock-free acquisition by:
    /// - Deduplicating reference paths to avoid attempting to acquire the same lock multiple times
    /// - Sorting paths alphabetically to ensure all callers acquire locks in the same order
    /// - Releasing all acquired locks if an error occurs during acquisition
    /// 
    /// The returned disposable handle releases all locks when disposed.
    /// </remarks>
    public async Task<IDisposable> AcquireMultipleReferenceLocksAsync(IEnumerable<string> referencePaths, CancellationToken cancellationToken = default)
    {
        if (referencePaths is null)
        {
            throw new ArgumentNullException(nameof(referencePaths));
        }

        // Deduplicate and sort paths to prevent deadlocks
        var orderedPaths = referencePaths.Distinct(StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal).ToList();
        var semaphores = new List<SemaphoreSlim>(orderedPaths.Count);

        await EnterReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in orderedPaths)
            {
                var semaphore = GetSemaphore(path);
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                semaphores.Add(semaphore);
            }

            return new MultipleLockHandle(semaphores, this);
        }
        catch
        {
            foreach (var semaphore in semaphores)
            {
                semaphore.Release();
            }
            ExitRead();
            throw;
        }
    }

    /// <summary>
    /// Represents a disposable lock handle for a single reference.
    /// </summary>
    private sealed class LockHandle : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly GitRepositoryLockManager _owner;
        private bool _disposed;

        public LockHandle(SemaphoreSlim semaphore, GitRepositoryLockManager owner)
        {
            _semaphore = semaphore;
            _owner = owner;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semaphore.Release();
                _owner.ExitRead();
            }
        }
    }

    /// <summary>
    /// Represents a disposable lock handle for multiple references.
    /// Releases all acquired locks in a single operation when disposed.
    /// </summary>
    private sealed class MultipleLockHandle : IDisposable
    {
        private readonly List<SemaphoreSlim> _semaphores;
        private readonly GitRepositoryLockManager _owner;
        private bool _disposed;

        public MultipleLockHandle(List<SemaphoreSlim> semaphores, GitRepositoryLockManager owner)
        {
            _semaphores = semaphores;
            _owner = owner;
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
                _owner.ExitRead();
            }
        }
    }

    /// <summary>
    /// Represents a disposable lock handle for the global ("lock all") lock.
    /// </summary>
    private sealed class GlobalLockHandle : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public GlobalLockHandle(SemaphoreSlim semaphore)
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
}
