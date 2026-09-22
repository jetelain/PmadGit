namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitRepositorySynchronizerTests
{
    private sealed class FakeRemoteRepository : IGitRemoteRepository, IGitRepositoryCacheInvalidator
    {
        public string RootPath { get; set; } = "C:\\fake\\repo";

        public List<(string? remote, string? branch, bool prune)> FetchCalls { get; } = new();
        public List<(string? remote, string? branch, bool rebase)> PullCalls { get; } = new();
        public List<(string? remote, string? branch, bool force, bool setUpstream)> PushCalls { get; } = new();
        public List<string> MergeCalls { get; } = new();
        public List<string> ResolveConflictCalls { get; } = new();
        public List<string?> ContinueMergeCalls { get; } = new();
        public int AbortMergeCalls { get; private set; }

        public GitMergeResult PullResult { get; set; } = new GitMergeResult(true, Array.Empty<string>());
        public GitMergeResult MergeResult { get; set; } = new GitMergeResult(true, Array.Empty<string>());
        public bool IsMergeInProgress { get; set; }
        public IReadOnlyList<string> ConflictedFiles { get; set; } = Array.Empty<string>();
        public GitTrackingStatus TrackingStatus { get; set; } = new GitTrackingStatus("main", "origin/main", 0, 0);
        public bool IsCommitPushed { get; set; } = true;

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public void InvalidateCaches(bool clearAllData = false, bool raiseChanged = true)
        {
            if (raiseChanged)
            {
                RaiseChanged();
            }
        }

        public Task FetchAsync(string? remote = null, string? branch = null, bool prune = false, CancellationToken cancellationToken = default)
        {
            FetchCalls.Add((remote, branch, prune));
            return Task.CompletedTask;
        }

        public Task<GitMergeResult> PullAsync(string? remote = null, string? branch = null, bool rebase = false, CancellationToken cancellationToken = default)
        {
            PullCalls.Add((remote, branch, rebase));
            return Task.FromResult(PullResult);
        }

        public Task PushAsync(string? remote = null, string? branch = null, bool force = false, bool setUpstream = false, CancellationToken cancellationToken = default)
        {
            PushCalls.Add((remote, branch, force, setUpstream));
            return Task.CompletedTask;
        }

        public Task<GitMergeResult> MergeAsync(string branch, CancellationToken cancellationToken = default)
        {
            MergeCalls.Add(branch);
            return Task.FromResult(MergeResult);
        }

        public Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsMergeInProgress);
        }

        public Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ConflictedFiles);
        }

        public Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default)
        {
            ResolveConflictCalls.Add(relativeFilePath);
            return Task.CompletedTask;
        }

        public Task ContinueMergeAsync(string? commitMessage = null, CancellationToken cancellationToken = default)
        {
            ContinueMergeCalls.Add(commitMessage);
            return Task.CompletedTask;
        }

        public Task AbortMergeAsync(CancellationToken cancellationToken = default)
        {
            AbortMergeCalls++;
            return Task.CompletedTask;
        }

        public Task<GitTrackingStatus> GetTrackingStatusAsync(string? branch = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TrackingStatus);
        }

        public Task<bool> IsCommitPushedAsync(string commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsCommitPushed);
        }

        public Task<bool> IsCommitPushedAsync(GitHash commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsCommitPushed);
        }
    }

    [Fact]
    public async Task Synchronizer_Targets_IGitRemoteRepository_For_Push()
    {
        var fake = new FakeRemoteRepository();
        var options = new GitSyncOptions
        {
            Remote = "origin",
            Branch = "feature",
            PushDebounceDelay = TimeSpan.FromMilliseconds(20)
        };

        await using var synchronizer = new GitRepositorySynchronizer(fake, options);

        synchronizer.NotifyLocalChange();
        await synchronizer.FlushPendingPushAsync();

        Assert.Single(fake.PushCalls);
        Assert.Equal("origin", fake.PushCalls[0].remote);
        Assert.Equal("feature", fake.PushCalls[0].branch);
        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.NotNull(synchronizer.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task Synchronizer_Targets_IGitRemoteRepository_For_Pull()
    {
        var fake = new FakeRemoteRepository();
        var options = new GitSyncOptions
        {
            Remote = "upstream",
            Branch = "main"
        };

        await using var synchronizer = new GitRepositorySynchronizer(fake, options);

        await synchronizer.TriggerRemoteSyncAsync();

        Assert.Single(fake.PullCalls);
        Assert.Equal("upstream", fake.PullCalls[0].remote);
        Assert.Equal("main", fake.PullCalls[0].branch);
        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.NotNull(synchronizer.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task Synchronizer_Handles_Conflict_And_Resolution_Via_IGitRemoteRepository()
    {
        var fake = new FakeRemoteRepository
        {
            PullResult = new GitMergeResult(false, new[] { "file1.txt", "file2.txt" })
        };
        var options = new GitSyncOptions();

        await using var synchronizer = new GitRepositorySynchronizer(fake, options);

        await synchronizer.TriggerRemoteSyncAsync();

        Assert.Equal(GitSyncState.Conflict, synchronizer.State);
        Assert.NotNull(synchronizer.Conflict);
        Assert.Equal(new[] { "file1.txt", "file2.txt" }, synchronizer.Conflict!.ConflictedFiles);

        await synchronizer.ResolveConflictAsync("file1.txt");
        await synchronizer.ResolveConflictAsync("file2.txt");

        Assert.Equal(new[] { "file1.txt", "file2.txt" }, fake.ResolveConflictCalls);

        await synchronizer.CompleteConflictResolutionAsync("Resolved merge");

        Assert.Single(fake.ContinueMergeCalls);
        Assert.Equal("Resolved merge", fake.ContinueMergeCalls[0]);
        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.Null(synchronizer.Conflict);
        Assert.Single(fake.PushCalls); // CompleteConflictResolutionAsync triggers a push
    }

    [Fact]
    public async Task Synchronizer_Handles_AbortConflict_Via_IGitRemoteRepository()
    {
        var fake = new FakeRemoteRepository
        {
            PullResult = new GitMergeResult(false, new[] { "conflict.txt" })
        };
        var options = new GitSyncOptions();

        await using var synchronizer = new GitRepositorySynchronizer(fake, options);

        await synchronizer.TriggerRemoteSyncAsync();
        Assert.Equal(GitSyncState.Conflict, synchronizer.State);

        await synchronizer.AbortConflictResolutionAsync();

        Assert.Equal(1, fake.AbortMergeCalls);
        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.Null(synchronizer.Conflict);
    }

    [Fact]
    public async Task Synchronizer_AutoSubscribes_To_ChangeSource_And_Debounces_Push()
    {
        var fake = new FakeRemoteRepository();
        var options = new GitSyncOptions
        {
            PushDebounceDelay = TimeSpan.FromMilliseconds(20)
        };

        // When fake implements IGitRepositoryCacheInvalidator, it automatically subscribes
        await using var synchronizer = new GitRepositorySynchronizer(fake, options);

        fake.RaiseChanged();

        var start = DateTime.UtcNow;
        while (fake.PushCalls.Count == 0 && (DateTime.UtcNow - start).TotalMilliseconds < 2000)
        {
            await Task.Delay(10);
        }

        Assert.Single(fake.PushCalls);
    }

    [Fact]
    public async Task CreateSynchronizer_ExtensionMethods_Work_With_IGitRemoteRepository()
    {
        var fake = new FakeRemoteRepository();
        var options = new GitSyncOptions();

        await using var synchronizer1 = fake.CreateSynchronizer(options, start: false);
        Assert.Equal(GitSyncState.Idle, synchronizer1.State);

        await using var synchronizer2 = ((IGitRemoteRepository)fake).CreateSynchronizer(options, start: false);
        Assert.Equal(GitSyncState.Idle, synchronizer2.State);
    }
}
