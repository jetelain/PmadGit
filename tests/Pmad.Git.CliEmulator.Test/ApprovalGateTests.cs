using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class ApprovalGateTests
{
    [Fact]
    public async Task Restore_WhenDenied_ReturnsExitCode130_AndDoesNotOverwrite()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            DiscardLocalChangesResult = ApprovalResult.Denied
        };

        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "modified locally");

        var response = await emulator.InvokeAsync(["restore", "README.md"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
        Assert.Equal("modified locally", File.ReadAllText(readmePath));
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("restore", approval.DiscardLocalChangesCalls[0].Operation);
    }

    [Fact]
    public async Task Restore_WhenCancelled_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            DiscardLocalChangesResult = ApprovalResult.Cancelled
        };

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "modified locally");

        var response = await emulator.InvokeAsync(["restore", "README.md"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetHard_WhenDenied_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            DiscardLocalChangesResult = ApprovalResult.Denied
        };

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "changed");

        var response = await emulator.InvokeAsync(["reset", "--hard", "HEAD"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
    }

    [Fact]
    public async Task BranchDelete_Force_CallsApproveUnpushedCommitLoss()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            UnpushedCommitLossResult = ApprovalResult.Approved
        };

        await emulator.InvokeAsync(["branch", "to-delete"], approval);
        var response = await emulator.InvokeAsync(["branch", "-D", "to-delete"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.UnpushedCommitLossCalls);
        Assert.Equal("branch -D", approval.UnpushedCommitLossCalls[0].Operation);
        Assert.Equal("to-delete", approval.UnpushedCommitLossCalls[0].BranchName);
    }

    [Fact]
    public async Task BranchDelete_Force_WhenDenied_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            UnpushedCommitLossResult = ApprovalResult.Denied
        };

        await emulator.InvokeAsync(["branch", "to-delete-denied"], approval);
        var response = await emulator.InvokeAsync(["branch", "-D", "to-delete-denied"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);

        var listRes = await emulator.InvokeAsync(["branch"], approval);
        Assert.Contains("to-delete-denied", listRes.StdOut);
    }

    [Fact]
    public async Task Fetch_WhenRemoteNotConfigured_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
        Assert.Empty(approval.ReadRemoteCalls);
    }

    [Fact]
    public async Task Pull_WhenRemoteNotConfigured_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
        Assert.Empty(approval.ReadRemoteCalls);
    }

    [Fact]
    public async Task Push_WhenRemoteNotConfigured_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["push"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
        Assert.Empty(approval.WriteRemoteCalls);
    }

    [Fact]
    public async Task Cancellation_ViaCancellationToken_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await emulator.InvokeAsync(["status"], cancellationToken: cts.Token);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fetch_WithDefaultRemoteUrl_PassesEffectiveUrlToApproval()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = repo.CreateCliEmulator(defaultRemoteUrl: "https://example.com/test-remote.git");
        var approval = new TestUserApproval
        {
            ReadRemoteResult = ApprovalResult.Denied
        };

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Single(approval.ReadRemoteCalls);
        Assert.Equal("https://example.com/test-remote.git", approval.ReadRemoteCalls[0].RemoteUrl);
    }

    [Fact]
    public async Task Push_ForceNonCurrentBranch_PassesTargetBranchCommitToApproval()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head.ToString();
        testRepo.Commit("Second commit", ("file2.txt", "content2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = repo.CreateCliEmulator(defaultRemoteUrl: "https://example.com/test-remote.git");
        var approval = new TestUserApproval
        {
            HistoryRewriteResult = ApprovalResult.Denied
        };

        await emulator.InvokeAsync(["branch", "old-branch", initialHash], approval);

        var response = await emulator.InvokeAsync(["push", "--force", "origin", "old-branch"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Single(approval.HistoryRewriteCalls);
        Assert.Equal("old-branch", approval.HistoryRewriteCalls[0].BranchName);
        Assert.Equal(initialHash, approval.HistoryRewriteCalls[0].AffectedCommits[0].Hash);
    }

    [Fact]
    public async Task CatFile_Cancelled_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await emulator.InvokeAsync(["cat-file", "-p", "HEAD"], cancellationToken: cts.Token);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Branch_Verbose_Cancelled_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await emulator.InvokeAsync(["branch", "-v"], cancellationToken: cts.Token);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCliEmulator_DisposesCreatedRemoteClientRepository()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = repo.CreateCliEmulator(defaultRemoteUrl: "https://example.com/test.git", disposeRepositories: false);

        var remote = emulator.Remote as IDisposable;
        Assert.NotNull(remote);

        emulator.Dispose();

        Assert.NotNull(repo.IndexManager);
    }
}
