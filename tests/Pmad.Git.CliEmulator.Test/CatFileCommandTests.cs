using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class CatFileCommandTests
{
    [Fact]
    public async Task CatFile_Commit_Type_ReturnsCommit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["cat-file", "-t", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("commit\n", response.StdOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task CatFile_Commit_PrettyPrint_ReturnsDetails()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Second commit", ("file2.txt", "v2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["cat-file", "-p", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("tree ", response.StdOut);
        Assert.Contains("parent ", response.StdOut);
        Assert.Contains("author ", response.StdOut);
        Assert.Contains("committer ", response.StdOut);
        Assert.Contains("Second commit", response.StdOut);
    }

    [Fact]
    public async Task CatFile_Commit_MissingFlags_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["cat-file", "HEAD"], approval);

        Assert.Equal(1, response.ExitCode);
        Assert.Contains("Specify -t or -p.", response.StdErr);
    }

    [Fact]
    public async Task CatFile_Blob_TypeAndPrint_Succeeds()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Add file", ("sample.txt", "hello blob content"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var blobBytes = System.Text.Encoding.UTF8.GetBytes("hello blob content");
        var blobHash = GitHashHelper.ComputeBlobHash(blobBytes).ToString();

        // Test -t on blob
        var typeResp = await emulator.InvokeAsync(["cat-file", "-t", blobHash], approval);
        Assert.Equal(0, typeResp.ExitCode);
        Assert.Equal("blob\n", typeResp.StdOut.Replace("\r\n", "\n"));

        // Test -p on blob
        var printResp = await emulator.InvokeAsync(["cat-file", "-p", blobHash], approval);
        Assert.Equal(0, printResp.ExitCode);
        Assert.Equal("hello blob content", printResp.StdOut);

        // Test missing flag on blob
        var noFlagResp = await emulator.InvokeAsync(["cat-file", blobHash], approval);
        Assert.Equal(1, noFlagResp.ExitCode);
        Assert.Contains("Specify -t or -p.", noFlagResp.StdErr);
    }

    [Fact]
    public async Task CatFile_Tree_Type_ReturnsTree()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var commit = await repo.GetCommitAsync("HEAD");
        var treeHash = commit.Tree.ToString();

        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var typeResp = await emulator.InvokeAsync(["cat-file", "-t", treeHash], approval);
        Assert.Equal(0, typeResp.ExitCode);
        Assert.Equal("tree\n", typeResp.StdOut.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task CatFile_NonExistentObject_ReturnsFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["cat-file", "-p", "0123456789012345678901234567890123456789"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal:", response.StdErr);
    }
}
