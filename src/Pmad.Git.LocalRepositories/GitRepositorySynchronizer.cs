namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Synchronizes a local repository with a remote using an <see cref="IGitRepositoryWithRemote"/>.
/// </summary>
/// <remarks>
/// <para>
/// Local-to-remote synchronization is debounced and detected automatically: when created with an
/// <see cref="IGitRepositoryCacheInvalidator"/> (such as an <see cref="IGitRepository"/>), the synchronizer
/// subscribes to its <see cref="IGitRepositoryCacheInvalidator.Changed"/> event, which is raised whenever
/// the repository is modified by any component (a local commit, a push, this synchronizer's own pulls, ...).
/// Each notification (re)schedules a push after <see cref="GitSyncOptions.PushDebounceDelay"/> of
/// inactivity. Call <see cref="NotifyLocalChange"/> to schedule a push manually, or
/// <see cref="FlushPendingPushAsync"/> to push immediately without waiting for the debounce delay.
/// </para>
/// <para>
/// Remote-to-local synchronization runs periodically every <see cref="GitSyncOptions.PullInterval"/>,
/// and can also be triggered on demand (e.g. from a webhook endpoint) using
/// <see cref="TriggerRemoteSyncAsync"/>.
/// </para>
/// <para>
/// Since the synchronizer's own push/pull/merge operations also raise
/// <see cref="IGitRepositoryCacheInvalidator.Changed"/>, those self-triggered notifications are
/// ignored while an operation is in progress, so completing a push or pull does not endlessly
/// reschedule another synchronization.
/// </para>
/// <para>
/// When a pull results in a merge conflict, the synchronizer switches to <see cref="GitSyncState.Conflict"/>
/// and stops automatic synchronization until the conflict is resolved via <see cref="ResolveConflictAsync"/>
/// followed by <see cref="CompleteConflictResolutionAsync"/>, or discarded via
/// <see cref="AbortConflictResolutionAsync"/>.
/// </para>
/// </remarks>
public sealed class GitRepositorySynchronizer : IAsyncDisposable
{
    private readonly IGitRepositoryWithRemote _remoteRepo;
    private readonly GitSyncOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly IGitRepositoryCacheInvalidator? _changeSource;
    private Timer? _pushDebounceTimer;
    private Task? _periodicPullLoop;
    private volatile bool _pushPending;
    private volatile GitSyncState _state = GitSyncState.Idle;
    private GitSyncConflictInfo? _conflict;
    private DateTimeOffset? _lastSuccessfulSyncAt;
    private bool _started;
    private bool _disposed;
    private int _suppressChangeDepth;

    /// <summary>
    /// Raised when the periodic remote-to-local synchronization loop encounters an unexpected
    /// exception while pulling, or when a push triggered by <see cref="FlushPendingPushAsync"/>
    /// (including debounced pushes) fails. The synchronizer keeps running after reporting the error:
    /// pulls retry at the next <see cref="GitSyncOptions.PullInterval"/>, and a failed push leaves
    /// the local change pending so it is retried on the next debounced or manual push attempt.
    /// </summary>
    public event EventHandler<Exception>? SyncError;

    /// <summary>
    /// Creates a synchronizer wrapping an <see cref="IGitRepositoryWithRemote"/> with no automatic local-change detection.
    /// </summary>
    public GitRepositorySynchronizer(IGitRepositoryWithRemote remoteRepository, GitSyncOptions? options = null)
        : this(remoteRepository, null, options)
    {
    }

    /// <summary>
    /// Creates a synchronizer wrapping an <see cref="IGitRepositoryWithRemote"/> and an optional
    /// <see cref="IGitRepositoryCacheInvalidator"/> change source to automatically detect local changes.
    /// </summary>
    public GitRepositorySynchronizer(IGitRepositoryWithRemote remoteRepository, IGitRepositoryCacheInvalidator? changeSource, GitSyncOptions? options = null)
    {
        _remoteRepo = remoteRepository ?? throw new ArgumentNullException(nameof(remoteRepository));
        _options = options ?? new GitSyncOptions();
        if (changeSource != null)
        {
            _changeSource = changeSource;
            _changeSource.Changed += OnRepositoryChanged;
        }
    }

    private void OnRepositoryChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _suppressChangeDepth) > 0)
        {
            // This change was caused by one of our own operations (push, pull, merge, ...);
            // ignore it to avoid scheduling a new synchronization endlessly.
            return;
        }
        NotifyLocalChange();
    }

    /// <summary>
    /// Marks the start of a git operation performed by this synchronizer, so that the resulting
    /// <see cref="IGitRepositoryCacheInvalidator.Changed"/> notification is not mistaken for an
    /// external local change. Must be paired with <see cref="EndSelfOperation"/> in a finally block.
    /// </summary>
    private void BeginSelfOperation() => Interlocked.Increment(ref _suppressChangeDepth);

    private void EndSelfOperation() => Interlocked.Decrement(ref _suppressChangeDepth);

    /// <summary>
    /// Current synchronization state.
    /// </summary>
    public GitSyncState State => _state;

    /// <summary>
    /// Information about the current pending conflict, or <c>null</c> when <see cref="State"/> is
    /// not <see cref="GitSyncState.Conflict"/>.
    /// </summary>
    public GitSyncConflictInfo? Conflict => _conflict;

    /// <summary>
    /// Timestamp of the last successful synchronization with the remote repository, whether it was
    /// a push (<see cref="FlushPendingPushAsync"/>) or a pull (<see cref="TriggerRemoteSyncAsync"/>),
    /// or <c>null</c> if none succeeded yet. A pull that stops because of a merge conflict does not
    /// update this value.
    /// </summary>
    public DateTimeOffset? LastSuccessfulSyncAt => _lastSuccessfulSyncAt;

    /// <summary>
    /// Starts the periodic remote-to-local synchronization loop. Must be called once before relying
    /// on automatic pulls; local-change debouncing works regardless of whether <see cref="Start"/>
    /// was called.
    /// </summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _periodicPullLoop = RunPeriodicPullLoopAsync(_lifetimeCts.Token);
    }

    private async Task RunPeriodicPullLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.PullInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await TriggerRemoteSyncAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Do not let a transient failure fault the periodic loop; report it and retry at the next tick.
                    SyncError?.Invoke(this, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Notifies the synchronizer that the local repository was modified, scheduling a debounced
    /// push. If further changes are notified before the delay elapses, the delay is reset.
    /// </summary>
    public void NotifyLocalChange()
    {
        if (_disposed)
        {
            return;
        }
        _pushPending = true;
        _pushDebounceTimer ??= new Timer(OnPushDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _pushDebounceTimer.Change(_options.PushDebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnPushDebounceElapsed(object? state)
    {
        if (_disposed)
        {
            return;
        }
        _ = FlushPendingPushAsync(_lifetimeCts.Token);
    }

    /// <summary>
    /// Immediately pushes local changes to the remote, cancelling any pending debounce delay.
    /// Does nothing when there is no pending change, a synchronization is already running, or a
    /// conflict is pending resolution. If the push fails, the failure is reported via
    /// <see cref="SyncError"/> instead of throwing, and the change remains pending so it is retried
    /// on the next debounced or manual push.
    /// </summary>
    private const int MaxAutoReconcileAttempts = 3;

    /// <summary>
    /// Immediately pushes local changes to the remote, cancelling any pending debounce delay.
    /// Does nothing when there is no pending change, a synchronization is already running, or a
    /// conflict is pending resolution. If the push fails, the failure is reported via
    /// <see cref="SyncError"/> instead of throwing, and the change remains pending so it is retried
    /// on the next debounced or manual push.
    /// </summary>
    public Task FlushPendingPushAsync(CancellationToken cancellationToken = default)
        => FlushPendingPushCoreAsync(0, cancellationToken);

    private async Task FlushPendingPushCoreAsync(int reconcileAttempt, CancellationToken cancellationToken)
    {
        if (_disposed || !_pushPending || _state == GitSyncState.Conflict)
        {
            return;
        }
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // Another synchronization (pull, or another push) currently holds the gate. Since the
            // one-shot debounce timer has already fired (or this is a manual flush racing with it),
            // rearm it so the pending change is not forgotten until another notification or manual
            // flush occurs; it will fire again once the current synchronization releases the gate.
            if (!_disposed && _pushPending && _state != GitSyncState.Conflict)
            {
                try
                {
                    _pushDebounceTimer?.Change(_options.PushDebounceDelay, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }
            return;
        }
        var needsAutoReconcile = false;
        try
        {
            if (_disposed || !_pushPending || _state == GitSyncState.Conflict)
            {
                return;
            }
            _pushPending = false;
            if (!_disposed)
            {
                try
                {
                    _pushDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                }
            }
            _state = GitSyncState.Syncing;
            BeginSelfOperation();
            try
            {
                await _remoteRepo.PushAsync(_options.Remote, _options.Branch, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Restore the pending flag so a cancelled push is retried later, just like other
                // push failures, instead of permanently forgetting the local change.
                _pushPending = true;
                throw;
            }
            catch (Exception ex) when (IsNonFastForwardRejection(ex))
            {
                if (reconcileAttempt < MaxAutoReconcileAttempts)
                {
                    needsAutoReconcile = true;
                }
                else
                {
                    _pushPending = true;
                    SyncError?.Invoke(this, ex);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Restore the pending flag so the change is retried on the next debounced/manual
                // push instead of being silently lost, and report the failure instead of letting it
                // fault an unobserved fire-and-forget task.
                _pushPending = true;
                SyncError?.Invoke(this, ex);
                return;
            }
            finally
            {
                EndSelfOperation();
            }

            if (!needsAutoReconcile)
            {
                _lastSuccessfulSyncAt = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            if (_state != GitSyncState.Conflict)
            {
                _state = GitSyncState.Idle;
            }
            _gate.Release();
        }

        if (needsAutoReconcile && !_disposed && _state != GitSyncState.Conflict)
        {
            try
            {
                await TriggerRemoteSyncAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _pushPending = true;
                throw;
            }
            catch (Exception ex)
            {
                _pushPending = true;
                SyncError?.Invoke(this, ex);
                return;
            }

            if (_state == GitSyncState.Conflict)
            {
                _pushPending = true;
                return;
            }

            _pushPending = true;
            await FlushPendingPushCoreAsync(reconcileAttempt + 1, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsNonFastForwardRejection(Exception ex)
    {
        var text = ex.ToString();
        if (text.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("fetch first", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tip of your current branch is behind", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Contains("[rejected]", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("rejected", StringComparison.OrdinalIgnoreCase))
        {
            if (!text.Contains("hook declined", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("permission", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Immediately pulls changes from the remote repository, integrating them into the local
    /// repository. Can be called manually (e.g. from a webhook endpoint triggered by the remote)
    /// in addition to the automatic periodic pull. Does nothing when a conflict is already pending.
    /// </summary>
    public async Task TriggerRemoteSyncAsync(CancellationToken cancellationToken = default)
    {
        if (_state == GitSyncState.Conflict)
        {
            return;
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == GitSyncState.Conflict)
            {
                return;
            }
            _state = GitSyncState.Syncing;
            GitMergeResult result;
            BeginSelfOperation();
            try
            {
                result = await _remoteRepo.PullAsync(_options.Remote, _options.Branch, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndSelfOperation();
            }
            if (!result.IsSuccess && result.HasConflicts)
            {
                _conflict = new GitSyncConflictInfo(result.ConflictedFiles, DateTimeOffset.UtcNow);
                _state = GitSyncState.Conflict;
                return;
            }
            _lastSuccessfulSyncAt = DateTimeOffset.UtcNow;
            _state = GitSyncState.Idle;
        }
        finally
        {
            if (_state == GitSyncState.Syncing)
            {
                _state = GitSyncState.Idle;
            }
            _gate.Release();
        }
    }

    /// <summary>
    /// Marks a conflicted file as resolved, by staging its current content. Call
    /// <see cref="CompleteConflictResolutionAsync"/> once all conflicted files have been resolved.
    /// </summary>
    /// <param name="relativeFilePath">Path of the file, relative to the repository root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default)
    {
        EnsureConflictPending();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConflictPending(); // Re-check after acquiring the gate, in case the state changed while waiting.
            BeginSelfOperation();
            try
            {
                await _remoteRepo.ResolveConflictAsync(relativeFilePath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndSelfOperation();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Completes the pending conflict resolution once every conflicted file has been resolved via
    /// <see cref="ResolveConflictAsync"/>, finalizing the merge commit and resuming normal
    /// synchronization (including pushing the merge commit to the remote).
    /// </summary>
    /// <param name="commitMessage">Optional commit message to use for the merge commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CompleteConflictResolutionAsync(string? commitMessage = null, CancellationToken cancellationToken = default)
    {
        EnsureConflictPending();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConflictPending(); // Re-check after acquiring the gate, in case the state changed while waiting.
            BeginSelfOperation();
            try
            {
                await _remoteRepo.ContinueMergeAsync(commitMessage, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndSelfOperation();
            }
            _conflict = null;
            _state = GitSyncState.Idle;
        }
        finally
        {
            _gate.Release();
        }

        NotifyLocalChange();
        await FlushPendingPushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Discards the pending conflict resolution, restoring the repository to the state it had
    /// before the merge started, and resumes normal synchronization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AbortConflictResolutionAsync(CancellationToken cancellationToken = default)
    {
        EnsureConflictPending();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureConflictPending(); // Re-check after acquiring the gate, in case the state changed while waiting.

            BeginSelfOperation();
            try
            {
                await _remoteRepo.AbortMergeAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndSelfOperation();
            }
            _conflict = null;
            _state = GitSyncState.Idle;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureConflictPending()
    {
        if (_state != GitSyncState.Conflict)
        {
            throw new InvalidOperationException("No conflict is currently pending resolution.");
        }
    }

    /// <summary>
    /// Stops the periodic pull loop and pending debounce timer, and waits for any in-flight
    /// synchronization (a debounced or manually triggered push/pull, or a conflict resolution
    /// call) to complete before releasing the synchronization primitives.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_changeSource != null)
        {
            _changeSource.Changed -= OnRepositoryChanged;
        }
        _lifetimeCts.Cancel();
        if (_pushDebounceTimer != null)
        {
            using var waitHandle = new ManualResetEvent(false);
            _pushDebounceTimer.Dispose(waitHandle);
            await Task.Run(() => waitHandle.WaitOne()).ConfigureAwait(false);
        }
        if (_periodicPullLoop != null)
        {
            try
            {
                await _periodicPullLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _lifetimeCts.Dispose();
        _gate.Dispose();
        if (_remoteRepo is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (_remoteRepo is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

