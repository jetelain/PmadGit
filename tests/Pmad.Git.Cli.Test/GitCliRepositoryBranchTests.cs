using Pmad.Git.Cli.Test.Infrastructure;

namespace Pmad.Git.Cli.Test;

public class GitCliRepositoryBranchTests
{
    [Fact]
    public async Task GetCurrentBranchAsync_Returns_Default_Branch()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);

        var branch = await repository.GetCurrentBranchAsync();

        Assert.Equal(GitCliTestRepository.DefaultBranch, branch);
    }

    [Fact]
    public async Task CreateBranchAsync_Creates_Branch_Without_Checking_It_Out()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);

        await repository.CreateBranchAsync("feature/one");

        var branches = await repository.GetBranchesAsync();
        Assert.Contains("feature/one", branches);
        Assert.Equal(GitCliTestRepository.DefaultBranch, await repository.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task CheckoutAsync_With_CreateNew_Creates_And_Switches_Branch()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);

        await repository.CheckoutAsync("feature/two", createNew: true);

        Assert.Equal("feature/two", await repository.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task CheckoutAsync_Switches_To_Existing_Branch()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.CreateBranchAsync("feature/three");

        await repository.CheckoutAsync("feature/three");

        Assert.Equal("feature/three", await repository.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task DeleteBranchAsync_Removes_Branch()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.CreateBranchAsync("feature/four");

        await repository.DeleteBranchAsync("feature/four");

        var branches = await repository.GetBranchesAsync();
        Assert.DoesNotContain("feature/four", branches);
    }

    [Fact]
    public async Task RenameBranchAsync_Renames_Current_Branch()
    {
        using var repo = GitCliTestRepository.Create();
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.CheckoutAsync("old-name", createNew: true);

        await repository.RenameBranchAsync("old-name", "new-name");

        Assert.Equal("new-name", await repository.GetCurrentBranchAsync());
        var branches = await repository.GetBranchesAsync();
        Assert.Contains("new-name", branches);
        Assert.DoesNotContain("old-name", branches);
    }

    [Fact]
    public async Task GetBranchesAsync_IncludeRemote_Lists_RemoteTracking_Branches()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repo = GitCliTestRepository.Create();
        repo.AddRemote("origin", remote);
        var repository = new GitCliRepository(repo.WorkingDirectory);
        await repository.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        var branches = await repository.GetBranchesAsync(includeRemote: true);

        Assert.Contains(branches, b => b.Contains("origin/" + GitCliTestRepository.DefaultBranch));
    }
}
