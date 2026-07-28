using Pmad.Git.Cli.Test.Infrastructure;

namespace Pmad.Git.Cli.Test;

public class GitCliRepositoryMergeTests
{
    [Fact]
    public async Task MergeAsync_Succeeds_When_There_Is_No_Conflict()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.CheckoutAsync("feature", createNew: true);
        repo.Commit("Feature change", ("feature.txt", "content"));
        await repository.CheckoutAsync(GitCliTestRepository.DefaultBranch);

        var result = await repository.MergeAsync("feature");

        Assert.True(result.IsSuccess);
        Assert.False(result.HasConflicts);
        Assert.True(File.Exists(Path.Combine(repo.WorkingDirectory, "feature.txt")));
    }

    [Fact]
    public async Task MergeAsync_Returns_Conflicted_Files_When_Merge_Conflicts()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.CheckoutAsync("feature", createNew: true);
        repo.Commit("Feature change", ("README.md", "from feature"));
        await repository.CheckoutAsync(GitCliTestRepository.DefaultBranch);
        repo.Commit("Main change", ("README.md", "from main"));

        var result = await repository.MergeAsync("feature");

        Assert.False(result.IsSuccess);
        Assert.True(result.HasConflicts);
        Assert.Contains("README.md", result.ConflictedFiles);
        Assert.True(await repository.IsMergeInProgressAsync());
    }

    [Fact]
    public async Task ResolveConflictAsync_And_ContinueMergeAsync_Complete_The_Merge()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.CheckoutAsync("feature", createNew: true);
        repo.Commit("Feature change", ("README.md", "from feature"));
        await repository.CheckoutAsync(GitCliTestRepository.DefaultBranch);
        repo.Commit("Main change", ("README.md", "from main"));

        var result = await repository.MergeAsync("feature");
        Assert.True(result.HasConflicts);

        repo.WriteFile("README.md", "resolved content");
        await repository.ResolveConflictAsync("README.md");
        await repository.ContinueMergeAsync("Merge feature into main");

        Assert.False(await repository.IsMergeInProgressAsync());
        Assert.Equal("resolved content", repo.ReadFile("README.md"));
    }

    [Fact]
    public async Task AbortMergeAsync_Restores_PreMerge_State()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.CheckoutAsync("feature", createNew: true);
        repo.Commit("Feature change", ("README.md", "from feature"));
        await repository.CheckoutAsync(GitCliTestRepository.DefaultBranch);
        repo.Commit("Main change", ("README.md", "from main"));

        var result = await repository.MergeAsync("feature");
        Assert.True(result.HasConflicts);

        await repository.AbortMergeAsync();

        Assert.False(await repository.IsMergeInProgressAsync());
        Assert.Equal("from main", repo.ReadFile("README.md"));
    }

    [Fact]
    public async Task GetConflictedFilesAsync_Returns_Empty_When_No_Conflict()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);

        var conflicts = await repository.GetConflictedFilesAsync();

        Assert.Empty(conflicts);
    }
}
