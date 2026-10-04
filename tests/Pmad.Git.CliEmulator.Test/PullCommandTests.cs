using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class PullCommandTests
{
    [Fact]
    public async Task Pull_WithoutRemote_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
    }

    [Fact]
    public async Task Pull_DefaultRemoteAndBranch_Succeeds()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            PullResult = new GitMergeResult(true, Array.Empty<string>(), null, GitMergeStatus.FastForward)
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Pull complete.", response.StdOut);
        Assert.Single(fakeRemote.PullCalls);
        Assert.Equal("origin", fakeRemote.PullCalls[0].Remote);
        Assert.Null(fakeRemote.PullCalls[0].Branch);
        Assert.False(fakeRemote.PullCalls[0].Rebase);

        Assert.Single(approval.ReadRemoteCalls);
        Assert.Equal("pull", approval.ReadRemoteCalls[0].Operation);
        Assert.Equal("origin", approval.ReadRemoteCalls[0].RemoteName);
    }

    [Fact]
    public async Task Pull_CustomRemoteAndBranch_PassesParameters()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            PullResult = new GitMergeResult(true, Array.Empty<string>(), null, GitMergeStatus.Merged)
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull", "upstream", "feature-x"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Pull complete.", response.StdOut);
        Assert.Single(fakeRemote.PullCalls);
        Assert.Equal("upstream", fakeRemote.PullCalls[0].Remote);
        Assert.Equal("feature-x", fakeRemote.PullCalls[0].Branch);

        Assert.Single(approval.ReadRemoteCalls);
        Assert.Equal("upstream", approval.ReadRemoteCalls[0].RemoteName);
        Assert.Equal("feature-x", approval.ReadRemoteCalls[0].Branch);
    }

    [Fact]
    public async Task Pull_AlreadyUpToDate_OutputsAlreadyUpToDate()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            PullResult = new GitMergeResult(true, Array.Empty<string>(), null, GitMergeStatus.AlreadyUpToDate)
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Already up to date.", response.StdOut);
    }

    [Fact]
    public async Task Pull_Conflicted_OutputsConflictsAndReturns1()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            PullResult = new GitMergeResult(false, new[] { "file1.txt", "sub/file2.txt" }, null, GitMergeStatus.Conflicted)
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(1, response.ExitCode);
        Assert.Contains("Automatic merge failed; fix conflicts and then commit the result.", response.StdOut);
        Assert.Contains("CONFLICT: file1.txt", response.StdErr);
        Assert.Contains("CONFLICT: sub/file2.txt", response.StdErr);
    }

    [Fact]
    public async Task Pull_WhenApprovalDenied_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote();
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval
        {
            ReadRemoteResult = ApprovalResult.Denied
        };

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
        Assert.Empty(fakeRemote.PullCalls);
    }

    [Fact]
    public async Task Pull_WhenApprovalCancelled_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote();
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval
        {
            ReadRemoteResult = ApprovalResult.Cancelled
        };

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fakeRemote.PullCalls);
    }

    [Fact]
    public async Task Pull_ThrowsException_Returns128WithFatalError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            OnPullAsync = (_, _, _, _) => throw new InvalidOperationException("Network connection aborted.")
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal: Network connection aborted.", response.StdErr);
    }
}
