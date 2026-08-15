using System.Threading;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli;

/// <summary>
/// Synchronizes a local <see cref="IGitRepository"/> with a remote repository using the Git CLI.
/// </summary>
/// <remarks>
/// <para>
/// Local-to-remote synchronization is debounced and detected automatically: when created from an
/// <see cref="IGitRepository"/>, the synchronizer subscribes to its <see cref="IGitRepositoryCacheInvalidator.Changed"/>
/// event, which is raised whenever the repository is modified by any component (a local commit, a
/// Smart HTTP push handled by <c>Pmad.Git.HttpServer</c>, this synchronizer's own pulls, ...).
/// Each notification (re)schedules a push after <see cref="GitSyncOptions.PushDebounceDelay"/> of
/// inactivity. Call <see cref="NotifyLocalChange"/> to schedule a push manually (e.g. when the
/// synchronizer was created directly from a <see cref="GitCliRepository"/> with no repository
/// instance to subscribe to), or <see cref="FlushPendingPushAsync"/> to push immediately without
/// waiting for the debounce delay.
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
/// <para>Use <see cref="GitRepositorySynchronizerExtensions.CreateSynchronizer"/> to create an instance
/// directly from an <see cref="IGitRepository"/>.</para>
/// </remarks>
public sealed class GitRepositorySynchronizer : IAsyncDisposable
{
    private readonly GitCliRepository _cli;
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
    /// exception (e.g. a <see cref="GitCliException"/> while pulling), or when a push triggered by
    /// <see cref="FlushPendingPushAsync"/> (including debounced pushes) fails. The synchronizer
    /// keeps running after reporting the error: pulls retry at the next
    /// <see cref="GitSyncOptions.PullInterval"/>, and a failed push leaves the local change pending
    /// so it is retried on the next debounced or manual push attempt.
    /// </summary>
    public event EventHandler<Exception>? SyncError;

    /// <summary>
    /// Creates a synchronizer wrapping a <see cref="GitCliRepository"/> built from the given
    /// <paramref name="repository"/>, so that CLI writes are synchronized with the repository's
    /// shared lock manager and cache invalidation, and automatically subscribes to
    /// <paramref name="repository"/>'s <see cref="IGitRepositoryCacheInvalidator.Changed"/> event to
    /// detect local changes without requiring manual <see cref="NotifyLocalChange"/> calls.
    /// </summary>
    public GitRepositorySynchronizer(IGitRepository repository, GitSyncOptions? options = null)
        : this(new GitCliRepository(repository, (options ??= new GitSyncOptions()).GitRunner), options)
    {
        _changeSource = repository;
        _changeSource.Changed += OnRepositoryChanged;
    }

    /// <summary>
    /// Creates a synchronizer wrapping an existing <see cref="GitCliRepository"/>. Since no
    /// <see cref="IGitRepository"/> instance is available in this case, local changes must be
    /// reported manually via <see cref="NotifyLocalChange"/>.
    /// </summary>
    public GitRepositorySynchronizer(GitCliRepository cli, GitSyncOptions? options = null)
    {
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
        _options = options ?? new GitSyncOptions();
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
                    // Do not let a transient failure (e.g. network error, GitCliException on pull
                    // failure) fault the periodic loop; report it and retry at the next tick.
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
        _ = FlushPendingPushAsync(CancellationToken.None);
    }

    /// <summary>
    /// Immediately pushes local changes to the remote, cancelling any pending debounce delay.
    /// Does nothing when there is no pending change, a synchronization is already running, or a
    /// conflict is pending resolution. If the push fails (e.g. <see cref="GitCliException"/>), the
    /// failure is reported via <see cref="SyncError"/> instead of throwing, and the change remains
    /// pending so it is retried on the next debounced or manual push.
    /// </summary>
    public async Task FlushPendingPushAsync(CancellationToken cancellationToken = default)
    {
        if (!_pushPending || _state == GitSyncState.Conflict)
        {
            return;
        }
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            if (!_pushPending || _state == GitSyncState.Conflict)
            {
                return;
            }
            _pushPending = false;
            _pushDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _state = GitSyncState.Syncing;
            BeginSelfOperation();
            try
            {
                await _cli.PushAsync(_options.Remote, _options.Branch, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Restore the pending flag so the change is retried on the next debounced/manual
                // push instead of being silently lost, and report the failure instead of letting it
                // fault an unobserved fire-and-forget task (see OnPushDebounceElapsed).
                _pushPending = true;
                SyncError?.Invoke(this, ex);
                return;
            }
            finally
            {
                EndSelfOperation();
            }
            _lastSuccessfulSyncAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            if (_state != GitSyncState.Conflict)
            {
                _state = GitSyncState.Idle;
            }
            _gate.Release();
        }
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
                result = await _cli.PullAsync(_options.Remote, _options.Branch, cancellationToken: cancellationToken).ConfigureAwait(false);
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
    /// <param name="cancellationToken"></param>
    public async Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default)
    {
        EnsureConflictPending();
        BeginSelfOperation();
        try
        {
            await _cli.ResolveConflictAsync(relativeFilePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            EndSelfOperation();
        }
    }

    /// <summary>
    /// Completes the pending conflict resolution once every conflicted file has been resolved via
    /// <see cref="ResolveConflictAsync"/>, finalizing the merge commit and resuming normal
    /// synchronization (including pushing the merge commit to the remote).
    /// </summary>
    /// <param name="commitMessage">Optional commit message to use for the merge commit.</param>
    /// <param name="cancellationToken"></param>
    public async Task CompleteConflictResolutionAsync(string? commitMessage = null, CancellationToken cancellationToken = default)
    {
        EnsureConflictPending();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BeginSelfOperation();
            try
            {
                await _cli.ContinueMergeAsync(commitMessage, cancellationToken).ConfigureAwait(false);
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
    public async Task AbortConflictResolutionAsync(CancellationToken cancellationToken = default)
    {
        EnsureConflictPending();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BeginSelfOperation();
            try
            {
                await _cli.AbortMergeAsync(cancellationToken).ConfigureAwait(false);
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
            // Timer.Dispose(WaitHandle) blocks until any callback currently executing (i.e. an
            // in-progress OnPushDebounceElapsed, which fire-and-forgets FlushPendingPushAsync)
            // has finished running before signaling the wait handle, so by the time this
            // completes the fire-and-forget push (if any) has already been started and is being
            // tracked by the gate below.
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
        // Quiesce any operation still in flight (a debounced push started just before the timer
        // was disposed, or a manually invoked FlushPendingPushAsync/TriggerRemoteSyncAsync call)
        // by acquiring the gate: since every such operation holds the gate for its whole
        // duration, waiting for it here guarantees none of them touches _gate or _cli after this
        // method returns and disposes the synchronization primitives.
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _lifetimeCts.Dispose();
        _gate.Dispose();
    }
}
