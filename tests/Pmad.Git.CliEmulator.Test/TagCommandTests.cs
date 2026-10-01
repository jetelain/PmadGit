using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class TagCommandTests
{
    [Fact]
    public async Task Tag_CreateAtSpecificCommit_SetsCorrectRef()
    {
        using var testRepo = GitTestRepository.Create();
        var firstHash = testRepo.Head.ToString();
        testRepo.Commit("Commit 2", ("f2.txt", "content"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Tag at the first commit, not HEAD
        var response = await emulator.InvokeAsync(["tag", "v0.1", firstHash], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("v0.1", response.StdOut);
        Assert.Contains(firstHash[..7], response.StdOut);
    }

    [Fact]
    public async Task Tag_Delete_WithoutName_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["tag", "-d"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }

    [Fact]
    public async Task Tag_List_MultipleTags_OrderedAlphabetically()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["tag", "v2.0"], approval);
        await emulator.InvokeAsync(["tag", "v1.0"], approval);
        await emulator.InvokeAsync(["tag", "v3.0"], approval);

        var response = await emulator.InvokeAsync(["tag"], approval);

        Assert.Equal(0, response.ExitCode);
        var tags = response.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["v1.0", "v2.0", "v3.0"], tags);
    }
}
