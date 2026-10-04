using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test.Fakes;

public class FakeGitRepositoryWithRemote : IGitRepositoryWithRemote
{
    public string RootPath { get; set; } = string.Empty;

    public List<(string? Remote, string? Branch, bool Prune)> FetchCalls { get; } = new();
    public List<(string? Remote, string? Branch, bool Rebase)> PullCalls { get; } = new();
    public List<(string? Remote, string? Branch, bool Force, bool SetUpstream)> PushCalls { get; } = new();

    public GitMergeResult PullResult { get; set; } = new(true, Array.Empty<string>(), null, GitMergeStatus.Merged);
    public Func<string? , string? , bool, CancellationToken, Task>? OnFetchAsync { get; set; }
    public Func<string? , string? , bool, CancellationToken, Task<GitMergeResult>>? OnPullAsync { get; set; }
    public Func<string, CancellationToken, Task<string?>>? OnGetRemoteUrlAsync { get; set; }

    public Task FetchAsync(string? remote = null, string? branch = null, bool prune = false, CancellationToken cancellationToken = default)
    {
        FetchCalls.Add((remote, branch, prune));
        if (OnFetchAsync != null)
        {
            return OnFetchAsync(remote, branch, prune, cancellationToken);
        }
        return Task.CompletedTask;
    }

    public Task<GitMergeResult> PullAsync(string? remote = null, string? branch = null, bool rebase = false, CancellationToken cancellationToken = default)
    {
        PullCalls.Add((remote, branch, rebase));
        if (OnPullAsync != null)
        {
            return OnPullAsync(remote, branch, rebase, cancellationToken);
        }
        return Task.FromResult(PullResult);
    }

    public Task PushAsync(string? remote = null, string? branch = null, bool force = false, bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        PushCalls.Add((remote, branch, force, setUpstream));
        return Task.CompletedTask;
    }

    public Task<GitMergeResult> MergeAsync(string branch, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GitMergeResult(true, Array.Empty<string>()));
    }

    public Task<bool> IsMergeInProgressAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task ResolveConflictAsync(string relativeFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ContinueMergeAsync(string? commitMessage = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AbortMergeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<GitTrackingStatus> GetTrackingStatusAsync(string? branch = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GitTrackingStatus(branch ?? "master", null, 0, 0));

    public Task<bool> IsCommitPushedAsync(string commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> IsCommitPushedAsync(GitHash commitHash, string? remoteBranch = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<string?> GetRemoteUrlAsync(string remoteName, CancellationToken cancellationToken = default)
    {
        if (OnGetRemoteUrlAsync != null)
        {
            return OnGetRemoteUrlAsync(remoteName, cancellationToken);
        }
        return Task.FromResult<string?>(null);
    }
}
