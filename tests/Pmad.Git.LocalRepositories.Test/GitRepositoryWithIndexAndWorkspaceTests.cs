using System.IO;
using System.Text;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitRepositoryWithIndexAndWorkspaceTests
{
    private static readonly GitCommitSignature TestSignature = new("Author", "author@example.com", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Init_CreatesWorkspaceRepository_ReadableByGitCli()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "PmadWorkspaceInitTest", Guid.NewGuid().ToString("N"));
        try
        {
            using var repo = GitRepositoryWithIndexAndWorkspace.Init(tempPath, "main");
            Assert.False(repo.IsBare);
            Assert.NotNull(repo.IndexManager);
            Assert.True(Directory.Exists(repo.GitDirectory));
            Assert.True(File.Exists(Path.Combine(repo.GitDirectory, "HEAD")));

            // Create and commit initial file
            await File.WriteAllTextAsync(Path.Combine(tempPath, "hello.txt"), "hello world");
            await repo.StageAsync("hello.txt");

            var commitHash = await repo.CommitAsync("Initial commit", new GitCommitMetadata("Initial commit", TestSignature));
            Assert.NotEqual(GitHash.Zero, commitHash);

            var commit = await repo.GetCommitAsync(commitHash.Value);
            Assert.Equal("Initial commit", commit.Message);
            Assert.Empty(commit.Parents);

            // Verify clean working tree
            Assert.True(await repo.IsWorkingTreeCleanAsync());
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempPath);
        }
    }

    [Fact]
    public async Task CommitAsync_StagesAndCommits_GitCliSeesCleanState()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "doc.md"), "# Documentation");
        await repo.StageAsync("doc.md");

        var commitHash = await repo.CommitAsync("Add doc", new GitCommitMetadata("Add doc", TestSignature));

        Assert.True(await repo.IsWorkingTreeCleanAsync());

        // Verify git CLI agrees that working tree and index are clean
        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Empty(cliStatus.Trim());

        var headCli = testRepo.RunGit("rev-parse HEAD").Trim();
        Assert.Equal(commitHash.Value, headCli);
    }

    [Fact]
    public async Task CommitAsync_WithStageAll_StagesAndCommitsEverything()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file1.txt", "1"), ("file2.txt", "2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file1.txt"), "1-updated");
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "file2.txt"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file3.txt"), "3-new");

        var commitHash = await repo.CommitAsync("Update all", new GitCommitMetadata("Update all", TestSignature), stageAll: true);

        Assert.True(await repo.IsWorkingTreeCleanAsync());

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Empty(cliStatus.Trim());

        Assert.True(await repo.FileExistsAsync("file1.txt", commitHash.Value));
        Assert.False(await repo.FileExistsAsync("file2.txt", commitHash.Value));
        Assert.True(await repo.FileExistsAsync("file3.txt", commitHash.Value));
    }

    [Fact]
    public async Task CommitAmendAsync_AmendsHeadCommitWithoutAlteringParents()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("c1.txt", "1"));
        testRepo.Commit("C2 Draft", ("c2.txt", "2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var c2 = await repo.GetCommitAsync("master");
        var c1Hash = c2.Parents[0];

        // Edit drafting file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "c2.txt"), "2-amended");

        var amendedHash = await repo.CommitAmendAsync(stageAll: true);
        var amendedCommit = await repo.GetCommitAsync(amendedHash.Value);

        Assert.Equal("C2 Draft", amendedCommit.Message);
        Assert.Single(amendedCommit.Parents);
        Assert.Equal(c1Hash, amendedCommit.Parents[0]);
        Assert.NotEqual(c2.Id, amendedHash);

        Assert.True(await repo.IsWorkingTreeCleanAsync());
        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Empty(cliStatus.Trim());
    }

    [Fact]
    public async Task ResetAsync_SoftMode_PreservesIndexAndWorkingTree()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("a.txt", "1"));
        var c1 = await GitRepository.Open(testRepo.WorkingDirectory).GetCommitAsync("master");

        testRepo.Commit("C2", ("b.txt", "2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await repo.ResetAsync(c1.Id, GitResetMode.Soft);

        // HEAD is at C1
        var head = await repo.ReferenceStore.ResolveHeadAsync();
        Assert.Equal(c1.Id, head);

        // In soft reset, b.txt should be staged (in index, not in HEAD)
        var status = await repo.GetStatusAsync();
        var bStatus = status.FindEntry("b.txt");
        Assert.NotNull(bStatus);
        Assert.Equal(GitFileStatus.StagedNew, bStatus.StagedStatus);

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("A  b.txt", cliStatus);
    }

    [Fact]
    public async Task ResetAsync_MixedMode_ResetsIndexAndPreservesWorkingTree()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("a.txt", "1"));
        var c1 = await GitRepository.Open(testRepo.WorkingDirectory).GetCommitAsync("master");

        testRepo.Commit("C2", ("b.txt", "2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await repo.ResetAsync(c1.Id, GitResetMode.Mixed);

        var head = await repo.ReferenceStore.ResolveHeadAsync();
        Assert.Equal(c1.Id, head);

        // In mixed reset, b.txt is untracked
        var status = await repo.GetStatusAsync();
        var bStatus = status.FindEntry("b.txt");
        Assert.NotNull(bStatus);
        Assert.Equal(GitFileStatus.Untracked, bStatus.WorkingTreeStatus);

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("?? b.txt", cliStatus);
    }

    [Fact]
    public async Task ResetAsync_HardMode_DiscardsWorkingTreeAndResetsIndex()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("a.txt", "1"));
        var c1 = await GitRepository.Open(testRepo.WorkingDirectory).GetCommitAsync("master");

        testRepo.Commit("C2", ("b.txt", "2"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "1-dirty");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await repo.ResetAsync(c1.Id, GitResetMode.Hard);

        var head = await repo.ReferenceStore.ResolveHeadAsync();
        Assert.Equal(c1.Id, head);

        // b.txt should be deleted, a.txt restored to "1"
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
        Assert.Equal("1", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt")));

        Assert.True(await repo.IsWorkingTreeCleanAsync());
        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Empty(cliStatus.Trim());
    }

    [Fact]
    public async Task SquashRangeAsync_SquashesMultipleCommitsIntoMilestone()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("M0: Start", ("shared.txt", "initial"));
        var m0 = await GitRepository.Open(testRepo.WorkingDirectory).GetCommitAsync("master");

        testRepo.Commit("draft 1", ("shared.txt", "draft 1"));
        testRepo.Commit("draft 2", ("shared.txt", "draft 2"), ("panel.txt", "panel 1"));
        testRepo.Commit("draft 3", ("shared.txt", "final draft"), ("panel.txt", "panel 1+2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var squashedHash = await repo.SquashRangeAsync(m0.Id, "story(01): completed initial draft", new GitCommitMetadata("story(01): completed initial draft", TestSignature));

        var squashedCommit = await repo.GetCommitAsync(squashedHash.Value);
        Assert.Equal("story(01): completed initial draft", squashedCommit.Message);
        Assert.Single(squashedCommit.Parents);
        Assert.Equal(m0.Id, squashedCommit.Parents[0]);

        // Tree matches draft 3
        Assert.Equal("final draft", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "shared.txt")));
        Assert.Equal("panel 1+2", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "panel.txt")));

        Assert.True(await repo.IsWorkingTreeCleanAsync());
        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Empty(cliStatus.Trim());
    }

    [Fact]
    public async Task RevertAsync_InvertsCommitChanges()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Base", ("good.txt", "good v1"));

        testRepo.Commit("Agent change", ("good.txt", "good v2"), ("bad.txt", "bad"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var agentCommit = await repo.GetCommitAsync("master");

        var revertHash = await repo.RevertAsync(agentCommit.Id);
        var revertCommit = await repo.GetCommitAsync(revertHash.Value);

        Assert.Contains("Revert", revertCommit.Message);
        Assert.Single(revertCommit.Parents);
        Assert.Equal(agentCommit.Id, revertCommit.Parents[0]);

        // bad.txt deleted, good.txt reverted to v1
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "bad.txt")));
        Assert.Equal("good v1", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "good.txt")));

        Assert.True(await repo.IsWorkingTreeCleanAsync());
        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Empty(cliStatus.Trim());
    }
}
