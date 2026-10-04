using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Commands;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class FetchCommandTests
{
    [Fact]
    public async Task Fetch_WithoutRemote_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
    }

    [Fact]
    public async Task Fetch_DefaultRemote_CallsFetchWithOriginAndNullBranch()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote();
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Fetch complete.", response.StdOut);
        Assert.Single(fakeRemote.FetchCalls);
        Assert.Equal("origin", fakeRemote.FetchCalls[0].Remote);
        Assert.Null(fakeRemote.FetchCalls[0].Branch);
        Assert.False(fakeRemote.FetchCalls[0].Prune);

        Assert.Single(approval.ReadRemoteCalls);
        Assert.Equal("fetch", approval.ReadRemoteCalls[0].Operation);
        Assert.Equal("origin", approval.ReadRemoteCalls[0].RemoteName);
    }

    [Fact]
    public async Task Fetch_WithRemoteAndBranchAndPrune_PassesParameters()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote();
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["fetch", "--prune", "upstream", "main"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Fetch complete.", response.StdOut);
        Assert.Single(fakeRemote.FetchCalls);
        Assert.Equal("upstream", fakeRemote.FetchCalls[0].Remote);
        Assert.Equal("main", fakeRemote.FetchCalls[0].Branch);
        Assert.True(fakeRemote.FetchCalls[0].Prune);
    }

    [Fact]
    public async Task Fetch_WithShortPruneOption_PassesPruneTrue()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote();
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["fetch", "-p", "origin"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(fakeRemote.FetchCalls);
        Assert.True(fakeRemote.FetchCalls[0].Prune);
    }

    [Fact]
    public async Task Fetch_WhenApprovalDenied_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote();
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval
        {
            ReadRemoteResult = ApprovalResult.Denied
        };

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
        Assert.Empty(fakeRemote.FetchCalls);
    }

    [Fact]
    public async Task Fetch_WhenApprovalCancelled_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote();
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval
        {
            ReadRemoteResult = ApprovalResult.Cancelled
        };

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fakeRemote.FetchCalls);
    }

    [Fact]
    public async Task Fetch_WhenExceptionThrown_Returns128WithFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            OnFetchAsync = (_, _, _, _) => throw new InvalidOperationException("Fetch connection failed.")
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal: Fetch connection failed.", response.StdErr);
    }

    [Fact]
    public async Task ResolveRemoteUrlAsync_RemoteReturnsUrl_UsesRemoteUrl()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            OnGetRemoteUrlAsync = (remoteName, _) => Task.FromResult<string?>($"https://example.com/{remoteName}.git")
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["fetch", "origin"], approval);

        Assert.Single(approval.ReadRemoteCalls);
        Assert.Equal("https://example.com/origin.git", approval.ReadRemoteCalls[0].RemoteUrl);
    }

    [Fact]
    public async Task ResolveRemoteUrlAsync_RemoteReturnsNull_ReadsGitConfig()
    {
        using var testRepo = GitTestRepository.Create();
        var configPath = Path.Combine(testRepo.WorkingDirectory, ".git", "config");
        File.AppendAllText(configPath, "\n[remote \"myremote\"]\n\turl = https://config.example.com/repo.git\n");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            OnGetRemoteUrlAsync = (_, _) => Task.FromResult<string?>(null)
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["fetch", "myremote"], approval);

        Assert.Single(approval.ReadRemoteCalls);
        Assert.Equal("https://config.example.com/repo.git", approval.ReadRemoteCalls[0].RemoteUrl);
    }

    [Fact]
    public async Task ResolveRemoteUrlAsync_FallbackToRemoteName()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var fakeRemote = new FakeGitRepositoryWithRemote
        {
            OnGetRemoteUrlAsync = (_, _) => Task.FromResult<string?>(null)
        };
        var emulator = new GitCliEmulator(repo, fakeRemote);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["fetch", "unknownremote"], approval);

        Assert.Single(approval.ReadRemoteCalls);
        Assert.Equal("unknownremote", approval.ReadRemoteCalls[0].RemoteUrl);
    }
}
