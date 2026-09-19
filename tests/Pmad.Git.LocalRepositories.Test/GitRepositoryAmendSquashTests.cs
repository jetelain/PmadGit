using System.Text;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitRepositoryAmendSquashTests
{
    private static readonly GitCommitSignature TestAuthor = new("Author", "author@example.com", DateTimeOffset.UtcNow);

    [Fact]
    public async Task AmendCommitAsync_PreservesParentsOfAmendedCommit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("a.txt", "1"));
        testRepo.Commit("C2", ("b.txt", "2"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var c2 = await repo.GetCommitAsync("master");
        var c1Hash = c2.Parents[0];

        var amendedHash = await repo.AmendCommitAsync(
            "master",
            new[] { new AddFileOperation("c.txt", Encoding.UTF8.GetBytes("3")) });

        var amendedCommit = await repo.GetCommitAsync(amendedHash.Value);

        Assert.Equal(c2.Parents, amendedCommit.Parents);
        Assert.Contains(c1Hash, amendedCommit.Parents);
        Assert.DoesNotContain(c2.Id, amendedCommit.Parents);

        // Verify the tree contains a.txt, b.txt, and c.txt
        Assert.True(await repo.FileExistsAsync("a.txt", amendedHash.Value));
        Assert.True(await repo.FileExistsAsync("b.txt", amendedHash.Value));
        Assert.True(await repo.FileExistsAsync("c.txt", amendedHash.Value));

        // Master branch now points to amended commit
        var currentHead = await repo.ReferenceStore.TryResolveReferenceAsync("refs/heads/master");
        Assert.Equal(amendedHash, currentHead);
    }

    [Fact]
    public async Task AmendCommitAsync_WithNullMetadata_PreservesOriginalMessageAndAuthor()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Original message", ("file.txt", "initial"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var originalCommit = await repo.GetCommitAsync("master");

        var amendedHash = await repo.AmendCommitAsync(
            "master",
            new[] { new UpdateFileOperation("file.txt", Encoding.UTF8.GetBytes("amended")) });

        var amendedCommit = await repo.GetCommitAsync(amendedHash.Value);

        Assert.Equal(originalCommit.Message, amendedCommit.Message);
        Assert.Equal(originalCommit.Metadata.Author.Name, amendedCommit.Metadata.Author.Name);
        Assert.Equal(originalCommit.Metadata.Author.Email, amendedCommit.Metadata.Author.Email);
        Assert.Equal(originalCommit.Metadata.Author.Timestamp, amendedCommit.Metadata.Author.Timestamp);
    }

    [Fact]
    public async Task AmendCommitAsync_WithNewMetadata_UpdatesMetadata()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Original", ("file.txt", "initial"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var newMeta = new GitCommitMetadata("New message", new GitCommitSignature("New Author", "new@example.com", DateTimeOffset.UtcNow));

        var amendedHash = await repo.AmendCommitAsync(
            "master",
            new[] { new AddFileOperation("other.txt", Encoding.UTF8.GetBytes("other")) },
            newMeta);

        var amendedCommit = await repo.GetCommitAsync(amendedHash.Value);

        Assert.Equal("New message", amendedCommit.Message);
        Assert.Equal("New Author", amendedCommit.Metadata.Author.Name);
    }

    [Fact]
    public async Task AmendCommitAsync_NonExistentBranch_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.AmendCommitAsync(
                "non-existent-branch",
                new[] { new AddFileOperation("file.txt", Encoding.UTF8.GetBytes("test")) }));
    }

    [Fact]
    public async Task SquashCommitsAsync_CreatesSingleParentCommitWithCumulativeTree()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Base", ("base.txt", "base"));
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var baseCommit = await repo.GetCommitAsync("master");

        testRepo.Commit("Draft 1", ("story.txt", "panel 1\n"));
        testRepo.Commit("Draft 2", ("story.txt", "panel 1\npanel 2\n"));
        testRepo.Commit("Draft 3", ("story.txt", "panel 1\npanel 2\npanel 3\n"), ("notes.txt", "draft notes"));

        repo.InvalidateCaches();
        var draft3Commit = await repo.GetCommitAsync("master");

        var milestoneMeta = new GitCommitMetadata("story(01): completed initial draft", TestAuthor);
        var squashedHash = await repo.SquashCommitsAsync("master", baseCommit.Id, milestoneMeta);

        var squashedCommit = await repo.GetCommitAsync(squashedHash.Value);

        // Sole parent must be the base commit
        Assert.Single(squashedCommit.Parents);
        Assert.Equal(baseCommit.Id, squashedCommit.Parents[0]);

        // Tree matches the final draft3 tree
        Assert.Equal(draft3Commit.Tree, squashedCommit.Tree);

        // Files match
        var content = await repo.ReadFileAsync("story.txt", squashedHash.Value);
        Assert.Equal("panel 1\npanel 2\npanel 3\n", Encoding.UTF8.GetString(content));
        Assert.True(await repo.FileExistsAsync("notes.txt", squashedHash.Value));

        // Master now points to squashed commit
        var currentHead = await repo.ReferenceStore.TryResolveReferenceAsync("refs/heads/master");
        Assert.Equal(squashedHash, currentHead);
    }

    [Fact]
    public async Task SquashCommitsAsync_WhenAlreadyAtBase_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var headCommit = await repo.GetCommitAsync("master");

        var meta = new GitCommitMetadata("Milestone", TestAuthor);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.SquashCommitsAsync("master", headCommit.Id, meta));
    }

    [Fact]
    public async Task SquashCommitsAsync_NonAncestorBase_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Base", ("base.txt", "0"));

        testRepo.RunGit("checkout -b other master --quiet");
        testRepo.Commit("Other branch commit", ("other.txt", "other"));

        testRepo.RunGit("checkout master --quiet");
        testRepo.Commit("Master commit", ("master.txt", "master"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var otherCommit = await repo.GetCommitAsync("other");

        var meta = new GitCommitMetadata("Milestone", TestAuthor);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.SquashCommitsAsync("master", otherCommit.Id, meta));
    }

    [Fact]
    public async Task AmendAndSquash_RaiseChangedEvent()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("C1", ("a.txt", "1"));
        testRepo.Commit("C2", ("b.txt", "2"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var c2 = await repo.GetCommitAsync("master");
        var c1Hash = c2.Parents[0];

        var eventCount = 0;
        repo.Changed += (_, _) => eventCount++;

        await repo.AmendCommitAsync("master", new[] { new AddFileOperation("c.txt", Encoding.UTF8.GetBytes("3")) });
        Assert.Equal(1, eventCount);

        var meta = new GitCommitMetadata("Squashed", TestAuthor);
        await repo.SquashCommitsAsync("master", c1Hash, meta);
        Assert.Equal(2, eventCount);
    }
}
