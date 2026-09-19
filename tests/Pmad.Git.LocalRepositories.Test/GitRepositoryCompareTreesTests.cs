using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitRepositoryCompareTreesTests
{
    [Fact]
    public async Task CompareTreesAsync_IdenticalTrees_ReturnsEmpty()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial commit", ("file1.txt", "content1"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync();

        var changes = await repo.CompareTreesAsync(headCommit.Tree, headCommit.Tree);

        Assert.Empty(changes);
    }

    [Fact]
    public async Task CompareTreesAsync_DetectsAdditionsModificationsDeletions()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1",
            ("a.txt", "v1"),
            ("b.txt", "v1"),
            ("sub/c.txt", "v1"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var c1 = await repo.GetCommitAsync("HEAD");

        testRepo.Commit("C2 - Modify a and add new",
            ("a.txt", "v2"),        // modified
            ("new.txt", "new"));    // added
        testRepo.RemoveFiles("C3 - Delete sub/c.txt", "sub/c.txt"); // deleted

        repo.InvalidateCaches();
        var c3 = await repo.GetCommitAsync("HEAD");

        var changes = await repo.CompareTreesAsync(c1.Tree, c3.Tree);

        Assert.Equal(3, changes.Count);

        // Ordinal sort: a.txt, new.txt, sub/c.txt
        Assert.Equal("a.txt", changes[0].Path);
        Assert.Equal(GitChangeKind.Modified, changes[0].Kind);
        Assert.NotNull(changes[0].OldHash);
        Assert.NotNull(changes[0].NewHash);
        Assert.NotEqual(changes[0].OldHash, changes[0].NewHash);

        Assert.Equal("new.txt", changes[1].Path);
        Assert.Equal(GitChangeKind.Added, changes[1].Kind);
        Assert.Null(changes[1].OldHash);
        Assert.NotNull(changes[1].NewHash);

        Assert.Equal("sub/c.txt", changes[2].Path);
        Assert.Equal(GitChangeKind.Deleted, changes[2].Kind);
        Assert.NotNull(changes[2].OldHash);
        Assert.Null(changes[2].NewHash);
    }

    [Fact]
    public async Task GetCommitChangesAsync_RootCommit_ReportsAllAsAdded()
    {
        using var testRepo = GitTestRepository.Create();
        // The GitTestRepository.Initialize creates a seed commit with README.md as the root
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var commits = new List<GitCommit>();
        await foreach (var c in repo.EnumerateCommitsAsync())
        {
            commits.Add(c);
        }
        var rootCommit = commits[^1];

        var changes = await repo.GetCommitChangesAsync(rootCommit.Id);

        Assert.Single(changes);
        Assert.Equal("README.md", changes[0].Path);
        Assert.Equal(GitChangeKind.Added, changes[0].Kind);
        Assert.Null(changes[0].OldHash);
        Assert.NotNull(changes[0].NewHash);
    }

    [Fact]
    public async Task GetCommitChangesAsync_NonRootCommit_ReportsChangesFromParent()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("file.txt", "v1"));
        testRepo.Commit("C2", ("file.txt", "v2"), ("added.txt", "new"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var changes = await repo.GetCommitChangesAsync("HEAD");

        Assert.Equal(2, changes.Count);
        Assert.Equal("added.txt", changes[0].Path);
        Assert.Equal(GitChangeKind.Added, changes[0].Kind);

        Assert.Equal("file.txt", changes[1].Path);
        Assert.Equal(GitChangeKind.Modified, changes[1].Kind);
    }

    [Fact]
    public async Task GetCommitChangesAsync_DefaultReferenceIsHead()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("test.txt", "v1"));
        testRepo.Commit("Update", ("test.txt", "v2"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var changes = await repo.GetCommitChangesAsync();

        Assert.Single(changes);
        Assert.Equal("test.txt", changes[0].Path);
        Assert.Equal(GitChangeKind.Modified, changes[0].Kind);
    }
}
