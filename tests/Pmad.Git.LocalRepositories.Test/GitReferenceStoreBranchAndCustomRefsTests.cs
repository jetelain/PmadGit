using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitReferenceStoreBranchAndCustomRefsTests
{
    [Fact]
    public async Task GetCurrentBranchNameAsync_OnBranch_ReturnsBranchName()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var branchName = await repo.GetCurrentBranchNameAsync();

        Assert.Equal("master", branchName);
    }

    [Fact]
    public async Task GetCurrentBranchNameAsync_WhenDetached_ReturnsNull()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("checkout --detach HEAD --quiet");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var branchName = await repo.GetCurrentBranchNameAsync();

        Assert.Null(branchName);
    }

    [Fact]
    public async Task IsHeadDetachedAsync_OnBranch_ReturnsFalse()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var isDetached = await repo.IsHeadDetachedAsync();

        Assert.False(isDetached);
    }

    [Fact]
    public async Task IsHeadDetachedAsync_WhenDetached_ReturnsTrue()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("checkout --detach HEAD --quiet");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var isDetached = await repo.IsHeadDetachedAsync();

        Assert.True(isDetached);
    }

    [Fact]
    public async Task CreateReferenceAsync_WithoutOverwrite_CreatesNewRef()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        const string backupRef = "refs/backups/draft-20260919";
        await repo.CreateReferenceAsync(backupRef, headCommit.Id);

        var resolved = await repo.ReferenceStore.TryResolveReferenceAsync(backupRef);
        Assert.Equal(headCommit.Id, resolved);
    }

    [Fact]
    public async Task CreateReferenceAsync_ExistingRef_WithoutOverwrite_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        const string backupRef = "refs/backups/draft-existing";
        await repo.CreateReferenceAsync(backupRef, headCommit.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.CreateReferenceAsync(backupRef, headCommit.Id, overwrite: false));
    }

    [Fact]
    public async Task CreateReferenceAsync_ExistingRef_WithOverwrite_UpdatesRef()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("First", ("a.txt", "1"));
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var commit1 = await repo.GetCommitAsync();

        testRepo.Commit("Second", ("a.txt", "2"));
        repo.InvalidateCaches();
        var commit2 = await repo.GetCommitAsync();

        const string backupRef = "refs/backups/draft-overwrite";
        await repo.CreateReferenceAsync(backupRef, commit1.Id);
        await repo.CreateReferenceAsync(backupRef, commit2.Id, overwrite: true);

        var resolved = await repo.ReferenceStore.TryResolveReferenceAsync(backupRef);
        Assert.Equal(commit2.Id, resolved);
    }

    [Fact]
    public async Task GetReferencesByPrefixAsync_ReturnsMatchingRefsOnly()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        await repo.CreateReferenceAsync("refs/backups/draft-1", headCommit.Id);
        await repo.CreateReferenceAsync("refs/backups/draft-2", headCommit.Id);
        await repo.CreateReferenceAsync("refs/custom/other", headCommit.Id);

        var backupRefs = await repo.GetReferencesByPrefixAsync("refs/backups/");

        Assert.Equal(2, backupRefs.Count);
        Assert.Contains("refs/backups/draft-1", backupRefs.Keys);
        Assert.Contains("refs/backups/draft-2", backupRefs.Keys);
        Assert.DoesNotContain("refs/custom/other", backupRefs.Keys);
    }

    [Fact]
    public async Task DeleteReferenceAsync_RemovesRef()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        const string backupRef = "refs/backups/draft-to-delete";
        await repo.CreateReferenceAsync(backupRef, headCommit.Id);

        var beforeDelete = await repo.ReferenceStore.TryResolveReferenceAsync(backupRef);
        Assert.NotNull(beforeDelete);

        await repo.DeleteReferenceAsync(backupRef);

        var afterDelete = await repo.ReferenceStore.TryResolveReferenceAsync(backupRef);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task CreateAndDeleteReference_RaisesChangedEvent()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        var changeCount = 0;
        repo.Changed += (_, _) => changeCount++;

        const string backupRef = "refs/backups/event-test";
        await repo.CreateReferenceAsync(backupRef, headCommit.Id);
        Assert.Equal(1, changeCount);

        await repo.DeleteReferenceAsync(backupRef);
        Assert.Equal(2, changeCount);
    }

    [Fact]
    public async Task GetCurrentBranchNameAsync_WhenBranchDoesNotExist_ReturnsNull()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("symbolic-ref HEAD refs/heads/missing-branch");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var branchName = await repo.GetCurrentBranchNameAsync();

        Assert.Null(branchName);
    }

    [Fact]
    public async Task GetCurrentBranchNameAsync_WhenBranchDoesNotExistAndAllowUnbornIsTrue_ReturnsBranchName()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("symbolic-ref HEAD refs/heads/missing-branch");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var branchName = await repo.GetCurrentBranchNameAsync(allowUnborn: true);

        Assert.Equal("missing-branch", branchName);
    }

    [Fact]
    public async Task GetCurrentBranchNameAsync_WhenBranchIsPacked_ReturnsBranchName()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("pack-refs --all");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var branchName = await repo.GetCurrentBranchNameAsync();

        Assert.Equal("master", branchName);
    }

    [Theory]
    [InlineData("refs/../HEAD")]
    [InlineData("refs/heads/../../HEAD")]
    [InlineData("refs/./test")]
    [InlineData("refs/backups/..")]
    [InlineData("refs/heads/foo..bar")]
    [InlineData("refs/heads/.hidden")]
    [InlineData("refs/heads/branch.lock")]
    [InlineData("refs/heads/branch with space")]
    public async Task CreateReferenceAsync_WithInvalidOrTraversalPath_ThrowsArgumentException(string invalidRef)
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateReferenceAsync(invalidRef, headCommit.Id));
    }

    [Fact]
    public async Task DeleteReferenceAsync_PackedRef_RemovesFromPackedRefsAndCannotBeResolved()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        const string backupRef = "refs/backups/packed-backup";
        await repo.CreateReferenceAsync(backupRef, headCommit.Id);

        // Pack references using git CLI
        testRepo.RunGit("pack-refs --all");

        // Confirm it is in packed-refs
        var packedRefsPath = Path.Combine(testRepo.GitDirectory, "packed-refs");
        Assert.True(File.Exists(packedRefsPath));
        var packedContentBefore = await File.ReadAllTextAsync(packedRefsPath);
        Assert.Contains(backupRef, packedContentBefore);

        // Delete the reference
        await repo.DeleteReferenceAsync(backupRef);

        // Verify it cannot be resolved through reference store
        var resolved = await repo.ReferenceStore.TryResolveReferenceAsync(backupRef);
        Assert.Null(resolved);

        // Recreate cache/store to ensure it doesn't reappear from packed-refs
        repo.InvalidateCaches();
        var resolvedAfterInvalidate = await repo.ReferenceStore.TryResolveReferenceAsync(backupRef);
        Assert.Null(resolvedAfterInvalidate);

        var allRefs = await repo.ReferenceStore.GetReferencesAsync();
        Assert.DoesNotContain(backupRef, allRefs.Keys);

        // Verify packed-refs file was updated on disk
        var packedContentAfter = await File.ReadAllTextAsync(packedRefsPath);
        Assert.DoesNotContain(backupRef, packedContentAfter);
    }
}
