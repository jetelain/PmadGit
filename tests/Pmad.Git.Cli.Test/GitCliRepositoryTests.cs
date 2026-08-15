using Pmad.Git.Cli.Test.Infrastructure;

namespace Pmad.Git.Cli.Test;

public class GitCliRepositoryTests
{
    [Fact]
    public void Constructor_Throws_When_Directory_Does_Not_Exist()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "PmadGitCliTests", Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentException>(() => new GitCliRepository(missingPath));
    }

    [Fact]
    public void Constructor_Sets_RootPath_And_GitCliPath()
    {
        using var repo = GitCliTestRepository.Create();

        var repository = new GitCliRepository(repo.WorkingDirectory);

        Assert.Equal(repo.WorkingDirectory, repository.RootPath);
        Assert.Equal("git", repository.GitCliPath);
    }

    [Fact]
    public async Task RunAsync_Returns_StandardOutput()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);

        var output = await repository.RunAsync("rev-parse", "--abbrev-ref", "HEAD");

        Assert.Equal(GitCliTestRepository.DefaultBranch, output.Trim());
    }

    [Fact]
    public async Task RunAsync_WithCancellationToken_Returns_StandardOutput()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);

        var output = await repository.RunAsync(CancellationToken.None, "rev-parse", "--abbrev-ref", "HEAD");

        Assert.Equal(GitCliTestRepository.DefaultBranch, output.Trim());
    }

    [Fact]
    public async Task RunAsync_Throws_GitCliException_On_Failure()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);

        var exception = await Assert.ThrowsAsync<GitCliException>(() => repository.RunAsync("not-a-git-command"));

        Assert.NotEqual(0, exception.ExitCode);
    }

    [Fact]
    public async Task FetchAsync_Succeeds_From_Remote()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var origin = GitCliTestRepository.Create();
        origin.AddRemote("origin", remote);
        origin.RunGit($"push origin {GitCliTestRepository.DefaultBranch}");

        using var clone = GitCliTestRepository.Clone(remote);
        origin.Commit("Second commit", ("file2.txt", "content"));
        origin.RunGit($"push origin {GitCliTestRepository.DefaultBranch}");

        var repository = new GitCliRepository(clone.WorkingDirectory);

        await repository.FetchAsync();

        var log = await repository.RunAsync("log", "origin/" + GitCliTestRepository.DefaultBranch, "--oneline");
        Assert.Contains("Second commit", log);
    }
}
