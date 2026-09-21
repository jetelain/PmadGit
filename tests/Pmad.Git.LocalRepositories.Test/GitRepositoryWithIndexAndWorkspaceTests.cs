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

    [Fact]
    public async Task CommitAsync_ThrowsOnUnmergedEntries()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "initial"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Add a conflicted stage 2 entry into index
        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        var blobHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Blob, "conflict"u8.ToArray());
        index.AddOrUpdate(new GitIndexEntry("conflict.txt", blobHash, 33188, flags: (ushort)(2 << 12)));
        await index.WriteAsync(repo.IndexManager.IndexPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CommitAsync("Commit conflicted"));
    }

    [Fact]
    public async Task CommitAmendAsync_ThrowsOnUnmergedEntries()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "initial"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        var blobHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Blob, "conflict"u8.ToArray());
        index.AddOrUpdate(new GitIndexEntry("conflict.txt", blobHash, 33188, flags: (ushort)(1 << 12)));
        await index.WriteAsync(repo.IndexManager.IndexPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CommitAmendAsync());
    }

    [Fact]
    public async Task CommitAsync_InDetachedHead_UpdatesHeadDirectly()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "initial"));

        // Detach HEAD
        var initialHash = testRepo.RunGit("rev-parse HEAD").Trim();
        testRepo.RunGit($"checkout {initialHash}");
        var masterBefore = testRepo.RunGit("rev-parse master").Trim();

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        Assert.True(await repo.IsHeadDetachedAsync());

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file.txt"), "detached change");
        await repo.StageAsync("file.txt");
        var commitHash = await repo.CommitAsync("Detached commit", new GitCommitMetadata("Detached commit", TestSignature));

        Assert.True(await repo.IsHeadDetachedAsync());
        var headCommit = await repo.GetCommitAsync("HEAD");
        Assert.Equal(commitHash, headCommit.Id);
        Assert.Equal("Detached commit", headCommit.Message);

        // master must NOT have changed!
        var masterAfter = testRepo.RunGit("rev-parse master").Trim();
        Assert.Equal(masterBefore, masterAfter);
    }

    [Fact]
    public async Task CommitAsync_InDetachedHead_SynchronizesOnHeadLock()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "initial"));

        var initialHash = testRepo.RunGit("rev-parse HEAD").Trim();
        testRepo.RunGit($"checkout {initialHash}");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        Assert.True(await repo.IsHeadDetachedAsync());

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file.txt"), "detached change");
        await repo.StageAsync("file.txt");

        // Hold the HEAD reference lock
        var headLock = await repo.LockManager.AcquireReferenceLockAsync("HEAD");

        // Attempting to commit while HEAD lock is held must block
        var commitTask = Task.Run(async () =>
        {
            return await repo.CommitAsync("Detached commit under lock", new GitCommitMetadata("Detached commit under lock", TestSignature));
        });

        // Ensure commitTask is waiting on HEAD lock
        var completed = await Task.WhenAny(commitTask, Task.Delay(100));
        Assert.NotEqual(commitTask, completed);

        // Release the HEAD lock
        headLock.Dispose();

        // Commit must now finish successfully
        var commitHash = await commitTask;
        var headCommit = await repo.GetCommitAsync("HEAD");
        Assert.Equal(commitHash, headCommit.Id);
        Assert.Equal("Detached commit under lock", headCommit.Message);
    }

    [Fact]
    public async Task RevertAsync_InDirtyWorkspace_ThrowsInvalidOperationException()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("file.txt", "v1"));
        testRepo.Commit("C2", ("file.txt", "v2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var c2 = await repo.GetCommitAsync("HEAD");

        // Dirty the workspace
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "uncommitted.txt"), "unsaved work");

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RevertAsync(c2.Id));
    }

    [Fact]
    public async Task SquashRangeAsync_WithBaseEqualToHead_ThrowsInvalidOperationException()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("file.txt", "v1"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var head = await repo.GetCommitAsync("HEAD");

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SquashRangeAsync(head.Id, "Squash"));
    }

    [Fact]
    public async Task SquashRangeAsync_WithUnreachableBase_ThrowsArgumentException()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("file.txt", "v1"));

        // Create an unrelated commit in object store
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var blobHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Blob, "unrelated"u8.ToArray());
        var unrelatedIndex = new GitIndex();
        unrelatedIndex.AddOrUpdate(new GitIndexEntry("unrelated.txt", blobHash, 33188));
        var treeHash = await repo.WriteTreeAsync(unrelatedIndex);
        var payload = GitRepository.BuildCommitPayload(treeHash, Array.Empty<GitHash>(), new GitCommitMetadata("Unrelated", TestSignature));
        var unrelatedCommit = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, payload);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.SquashRangeAsync(unrelatedCommit, "Squash"));
    }

    [Fact]
    public async Task CreateCommitAsync_OnCurrentBranch_UpdatesWorkspaceAndIndex()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("init.txt", "hello"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var currentBranch = await repo.ReferenceStore.GetCurrentBranchNameAsync();
        Assert.NotNull(currentBranch);

        var commitHash = await repo.CreateCommitAsync(
            currentBranch,
            new GitCommitOperation[]
            {
                new AddFileOperation("generated.txt", Encoding.UTF8.GetBytes("auto-generated content"))
            },
            new GitCommitMetadata("Direct commit on current branch", TestSignature));

        // Working tree must contain generated.txt
        var generatedPath = Path.Combine(testRepo.WorkingDirectory, "generated.txt");
        Assert.True(File.Exists(generatedPath));
        Assert.Equal("auto-generated content", await File.ReadAllTextAsync(generatedPath));

        // Index and working tree must be clean
        Assert.True(await repo.IsWorkingTreeCleanAsync());
    }

    [Fact]
    public async Task AmendCommitAsync_OnCurrentBranch_UpdatesWorkspaceAndIndex()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("init.txt", "hello"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var currentBranch = await repo.ReferenceStore.GetCurrentBranchNameAsync();
        Assert.NotNull(currentBranch);

        var commitHash = await repo.AmendCommitAsync(
            currentBranch,
            new GitCommitOperation[]
            {
                new AddFileOperation("amended.txt", Encoding.UTF8.GetBytes("amended content"))
            },
            new GitCommitMetadata("Amended on current branch", TestSignature));

        // Working tree must contain amended.txt
        var amendedPath = Path.Combine(testRepo.WorkingDirectory, "amended.txt");
        Assert.True(File.Exists(amendedPath));
        Assert.Equal("amended content", await File.ReadAllTextAsync(amendedPath));

        // Index and working tree must be clean
        Assert.True(await repo.IsWorkingTreeCleanAsync());
    }
}

