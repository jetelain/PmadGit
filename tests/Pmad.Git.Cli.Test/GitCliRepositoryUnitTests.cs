using Pmad.Git.Cli.Test.Infrastructure;

namespace Pmad.Git.Cli.Test;

/// <summary>
/// Unit tests that exercise <see cref="GitCliRepository"/> in isolation using a <see cref="FakeGitRunner"/>,
/// verifying the exact git command-line arguments produced and how responses are interpreted.
/// </summary>
public class GitCliRepositoryUnitTests
{
    private static readonly string ExistingDirectory = Path.GetTempPath();

    [Fact]
    public async Task FetchAsync_Without_Options_Runs_Plain_Fetch()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.FetchAsync();

        Assert.Equal(new[] { "fetch" }, runner.Calls.Single());
    }

    [Fact]
    public async Task FetchAsync_With_All_Options_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.FetchAsync("origin", "main", prune: true);

        Assert.Equal(new[] { "fetch", "--prune", "origin", "main" }, runner.Calls.Single());
    }

    [Fact]
    public async Task FetchAsync_Throws_On_Failure()
    {
        var runner = new FakeGitRunner().Enqueue(1, stderr: "boom");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var exception = await Assert.ThrowsAsync<GitCliException>(() => repository.FetchAsync());
        Assert.Equal(1, exception.ExitCode);
        Assert.Equal("boom", exception.StdErr);
    }

    [Fact]
    public async Task PullAsync_Without_Options_Runs_Plain_Pull()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var result = await repository.PullAsync();

        Assert.Equal(new[] { "pull", "--no-rebase" }, runner.Calls.Single());
        Assert.True(result.IsSuccess);
        Assert.False(result.HasConflicts);
    }

    [Fact]
    public async Task PullAsync_With_Rebase_Remote_And_Branch_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.PullAsync("origin", "main", rebase: true);

        Assert.Equal(new[] { "pull", "--rebase", "origin", "main" }, runner.Calls.Single());
    }

    [Fact]
    public async Task PullAsync_Returns_Conflicted_Files_When_Merge_Fails_With_Conflicts()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "README.md\nsrc/File.cs\n");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var result = await repository.PullAsync();

        Assert.False(result.IsSuccess);
        Assert.True(result.HasConflicts);
        Assert.Equal(new[] { "README.md", "src/File.cs" }, result.ConflictedFiles);
        Assert.Equal(new[] { "pull", "--no-rebase" }, runner.Calls[0]);
        Assert.Equal(new[] { "diff", "--name-only", "--diff-filter=U" }, runner.Calls[1]);
    }

    [Fact]
    public async Task PullAsync_Throws_When_Failure_Is_Not_A_Conflict()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "network error")
            .Enqueue(0, stdout: "");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var exception = await Assert.ThrowsAsync<GitCliException>(() => repository.PullAsync());
        Assert.Equal("network error", exception.StdErr);
    }

    [Fact]
    public async Task PushAsync_Without_Options_Runs_Plain_Push()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.PushAsync();

        Assert.Equal(new[] { "push" }, runner.Calls.Single());
    }

    [Fact]
    public async Task PushAsync_With_All_Options_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.PushAsync("origin", "main", force: true, setUpstream: true);

        Assert.Equal(new[] { "push", "-u", "--force-with-lease", "origin", "main" }, runner.Calls.Single());
    }

    [Fact]
    public async Task PushAsync_Throws_On_Failure()
    {
        var runner = new FakeGitRunner().Enqueue(1, stderr: "rejected");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await Assert.ThrowsAsync<GitCliException>(() => repository.PushAsync());
    }

    [Fact]
    public async Task GetCurrentBranchAsync_Trims_Output()
    {
        var runner = new FakeGitRunner().Enqueue(0, stdout: "main\n");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var branch = await repository.GetCurrentBranchAsync();

        Assert.Equal("main", branch);
        Assert.Equal(new[] { "rev-parse", "--abbrev-ref", "HEAD" }, runner.Calls.Single());
    }

    [Fact]
    public async Task GetBranchesAsync_Without_Remote_Parses_Lines()
    {
        var runner = new FakeGitRunner().Enqueue(0, stdout: "main\nfeature/one\n");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var branches = await repository.GetBranchesAsync();

        Assert.Equal(new[] { "main", "feature/one" }, branches);
        Assert.Equal(new[] { "branch", "--format=%(refname:short)" }, runner.Calls.Single());
    }

    [Fact]
    public async Task GetBranchesAsync_With_IncludeRemote_Adds_All_Flag()
    {
        var runner = new FakeGitRunner().Enqueue(0, stdout: "main\norigin/main\n");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var branches = await repository.GetBranchesAsync(includeRemote: true);

        Assert.Equal(new[] { "main", "origin/main" }, branches);
        Assert.Equal(new[] { "branch", "--format=%(refname:short)", "--all" }, runner.Calls.Single());
    }

    [Fact]
    public async Task CreateBranchAsync_Without_StartPoint_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.CreateBranchAsync("feature");

        Assert.Equal(new[] { "branch", "feature" }, runner.Calls.Single());
    }

    [Fact]
    public async Task CreateBranchAsync_With_StartPoint_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.CreateBranchAsync("feature", "main");

        Assert.Equal(new[] { "branch", "feature", "main" }, runner.Calls.Single());
    }

    [Theory]
    [InlineData(false, null, new[] { "checkout", "feature" })]
    [InlineData(true, null, new[] { "checkout", "-b", "feature" })]
    [InlineData(true, "main", new[] { "checkout", "-b", "feature", "main" })]
    public async Task CheckoutAsync_Builds_Expected_Arguments(bool createNew, string? startPoint, string[] expected)
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.CheckoutAsync("feature", createNew, startPoint);

        Assert.Equal(expected, runner.Calls.Single());
    }

    [Fact]
    public async Task CheckoutAsync_Ignores_StartPoint_When_Not_Creating_New_Branch()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.CheckoutAsync("feature", createNew: false, startPoint: "main");

        Assert.Equal(new[] { "checkout", "feature" }, runner.Calls.Single());
    }

    [Fact]
    public async Task CheckoutAsync_With_UpdateWorkingTree_False_Verifies_Branch_Before_SymbolicRef()
    {
        var runner = new FakeGitRunner().Enqueue(0).Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.CheckoutAsync("feature", createNew: false, updateWorkingTree: false);

        Assert.Equal(new[] { "show-ref", "--verify", "--quiet", "refs/heads/feature" }, runner.Calls[0]);
        Assert.Equal(new[] { "symbolic-ref", "HEAD", "refs/heads/feature" }, runner.Calls[1]);
    }

    [Fact]
    public async Task CheckoutAsync_With_UpdateWorkingTree_False_Throws_When_Branch_Does_Not_Exist()
    {
        var runner = new FakeGitRunner().Enqueue(1, stderr: "not a valid ref");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CheckoutAsync("missing", createNew: false, updateWorkingTree: false));

        Assert.Equal(new[] { "show-ref", "--verify", "--quiet", "refs/heads/missing" }, runner.Calls.Single());
    }

    [Theory]
    [InlineData(false, "-d")]
    [InlineData(true, "-D")]
    public async Task DeleteBranchAsync_Uses_Force_Flag(bool force, string expectedFlag)
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.DeleteBranchAsync("feature", force);

        Assert.Equal(new[] { "branch", expectedFlag, "feature" }, runner.Calls.Single());
    }

    [Fact]
    public async Task RenameBranchAsync_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.RenameBranchAsync("old", "new");

        Assert.Equal(new[] { "branch", "-m", "old", "new" }, runner.Calls.Single());
    }

    [Fact]
    public async Task MergeAsync_Returns_Success_When_Exit_Code_Is_Zero()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var result = await repository.MergeAsync("feature");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ConflictedFiles);
        Assert.Equal(new[] { "merge", "--no-edit", "feature" }, runner.Calls.Single());
    }

    [Fact]
    public async Task MergeAsync_Returns_Conflicts_When_Merge_Fails_With_Conflicted_Files()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "a.txt\n");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var result = await repository.MergeAsync("feature");

        Assert.False(result.IsSuccess);
        Assert.Equal(new[] { "a.txt" }, result.ConflictedFiles);
    }

    [Fact]
    public async Task MergeAsync_Throws_When_Failure_Is_Not_A_Conflict()
    {
        var runner = new FakeGitRunner()
            .Enqueue(128, stderr: "not something we can merge")
            .Enqueue(0, stdout: "");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await Assert.ThrowsAsync<GitCliException>(() => repository.MergeAsync("feature"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task IsMergeInProgressAsync_Reflects_ExitCode(int exitCode, bool expected)
    {
        var runner = new FakeGitRunner().Enqueue(exitCode);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var result = await repository.IsMergeInProgressAsync();

        Assert.Equal(expected, result);
        Assert.Equal(new[] { "rev-parse", "-q", "--verify", "MERGE_HEAD" }, runner.Calls.Single());
    }

    [Fact]
    public async Task GetConflictedFilesAsync_Parses_NonEmpty_Lines()
    {
        var runner = new FakeGitRunner().Enqueue(0, stdout: "a.txt\r\nb.txt\r\n\r\n");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var files = await repository.GetConflictedFilesAsync();

        Assert.Equal(new[] { "a.txt", "b.txt" }, files);
    }

    [Fact]
    public async Task ResolveConflictAsync_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.ResolveConflictAsync("path/to/file.txt");

        Assert.Equal(new[] { "add", "--", "path/to/file.txt" }, runner.Calls.Single());
    }

    [Fact]
    public async Task ContinueMergeAsync_Without_Message_Uses_NoEdit()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.ContinueMergeAsync();

        Assert.Equal(new[] { "commit", "--no-edit" }, runner.Calls.Single());
    }

    [Fact]
    public async Task ContinueMergeAsync_With_Message_Passes_Message()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.ContinueMergeAsync("Merge feature");

        Assert.Equal(new[] { "commit", "-m", "Merge feature" }, runner.Calls.Single());
    }

    [Fact]
    public async Task AbortMergeAsync_Builds_Expected_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.AbortMergeAsync();

        Assert.Equal(new[] { "merge", "--abort" }, runner.Calls.Single());
    }

    [Fact]
    public async Task RunAsync_Passes_Arguments_And_Returns_StdOut()
    {
        var runner = new FakeGitRunner().Enqueue(0, stdout: "output");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var output = await repository.RunAsync("status", "--short");

        Assert.Equal("output", output);
        Assert.Equal(new[] { "status", "--short" }, runner.Calls.Single());
    }

    [Fact]
    public async Task RunAsync_WithCancellationToken_Passes_Arguments()
    {
        var runner = new FakeGitRunner().Enqueue(0, stdout: "output");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var output = await repository.RunAsync(CancellationToken.None, "status");

        Assert.Equal("output", output);
        Assert.Equal(new[] { "status" }, runner.Calls.Single());
    }

    [Fact]
    public async Task RunAsync_Throws_GitCliException_On_Failure()
    {
        var runner = new FakeGitRunner().Enqueue(1, stderr: "failure");
        var repository = new GitCliRepository(ExistingDirectory, runner);

        var exception = await Assert.ThrowsAsync<GitCliException>(() => repository.RunAsync("bad-command"));
        Assert.Equal(1, exception.ExitCode);
        Assert.Equal("failure", exception.StdErr);
        Assert.Equal(new[] { "bad-command" }, exception.Arguments);
    }

    [Fact]
    public async Task RunGit_Uses_RootPath_As_WorkingDirectory()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var repository = new GitCliRepository(ExistingDirectory, runner);

        await repository.RunAsync("status");

        Assert.Equal(ExistingDirectory, runner.WorkingDirectories.Single());
    }
}
