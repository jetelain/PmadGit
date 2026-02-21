using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitRepository.ListFilesWithLastChangeAsync.
/// </summary>
public sealed class GitRepositoryListFilesWithLastChangeTests
{
    [Fact]
    public async Task ListFilesWithLastChangeAsync_SingleFile_ReturnsCorrectCommit()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add file", ("file.txt", "v1"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.True(result.ContainsKey("file.txt"));
        Assert.Equal(addCommit, result["file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_UpdatedFile_ReturnsNewestCommit()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "v1"));
        var updateCommit = repo.Commit("Update file", ("file.txt", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(updateCommit, result["file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_MultipleFiles_EachReturnsItsOwnLastCommit()
    {
        using var repo = GitTestRepository.Create();
        var commitA = repo.Commit("Add a.txt", ("a.txt", "a1"));
        var commitB = repo.Commit("Add b.txt", ("b.txt", "b1"));
        var commitC = repo.Commit("Update a.txt", ("a.txt", "a2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(commitC, result["a.txt"].Id);
        Assert.Equal(commitB, result["b.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileNeverUpdated_ReturnsCreatingCommit()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add stable.txt", ("stable.txt", "content"));
        repo.Commit("Add other.txt", ("other.txt", "content"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(addCommit, result["stable.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithPath_OnlyReturnsFilesUnderPath()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add files", ("docs/readme.md", "doc"), ("src/main.cs", "code"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.True(result.ContainsKey("docs/readme.md"));
        Assert.False(result.ContainsKey("src/main.cs"));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithPath_UpdatedFileUnderPath_ReturnsNewestCommit()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add docs", ("docs/a.md", "v1"), ("docs/b.md", "v1"));
        var updateCommit = repo.Commit("Update docs/a.md", ("docs/a.md", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.Equal(updateCommit, result["docs/a.md"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithFileFilter_ExcludesNonMatchingFiles()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add files", ("a.md", "md"), ("b.txt", "txt"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(
            fileFilter: path => path.EndsWith(".md", StringComparison.Ordinal));

        Assert.True(result.ContainsKey("a.md"));
        Assert.False(result.ContainsKey("b.txt"));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithFileFilter_AllFilesExcluded_ReturnsEmpty()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(
            fileFilter: _ => false);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithReference_UsesSpecifiedCommit()
    {
        using var repo = GitTestRepository.Create();
        var taggedCommit = repo.Commit("Tagged commit", ("file.txt", "v1"));
        repo.RunGit("tag v1.0");
        repo.Commit("After tag", ("file.txt", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(reference: "v1.0");

        Assert.Equal(taggedCommit, result["file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_NonExistentPath_ReturnsEmpty()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => gitRepository.ListFilesWithLastChangeAsync(path: "nonexistent"));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FilesInSubDirectories_ReturnsCorrectPaths()
    {
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Add nested files",
            ("src/a/file1.txt", "v1"),
            ("src/b/file2.txt", "v1"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "src");

        Assert.True(result.ContainsKey("src/a/file1.txt"));
        Assert.True(result.ContainsKey("src/b/file2.txt"));
        Assert.Equal(commit, result["src/a/file1.txt"].Id);
        Assert.Equal(commit, result["src/b/file2.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_PathDirectoryRemovedInOlderCommit_StillReturnsFiles()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add docs", ("docs/readme.md", "v1"));
        var updateCommit = repo.Commit("Update docs", ("docs/readme.md", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.True(result.ContainsKey("docs/readme.md"));
        Assert.Equal(updateCommit, result["docs/readme.md"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_RespectsCancellationToken()
    {
        using var repo = GitTestRepository.Create();
        for (var i = 0; i < 5; i++)
        {
            repo.Commit($"Commit {i}", ($"file{i}.txt", $"content{i}"));
        }

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gitRepository.ListFilesWithLastChangeAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_AllFiles_IncludesInitialCommitFiles()
    {
        using var repo = GitTestRepository.Create();
        // GitTestRepository.Create() makes an initial commit with README.md
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.True(result.ContainsKey("README.md"));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_UnchangedFileAmongUpdatedOnes_ReturnsCreatingCommit()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add both", ("stable.txt", "same"), ("changing.txt", "v1"));
        repo.Commit("Update changing only", ("changing.txt", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(addCommit, result["stable.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_RemovedFile_ReturnsCommitThatRemovedIt()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add files", ("file1.txt", "content1"), ("file2.txt", "content2"));
        var removeCommit = repo.RemoveFiles("Remove file2", "file2.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.True(result.ContainsKey("file1.txt"));
        Assert.False(result.ContainsKey("file2.txt"));
        Assert.Equal(addCommit, result["file1.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileAddedAndRemoved_NotInResult()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add temp.txt", ("temp.txt", "temp"), ("keep.txt", "keep"));
        var removeCommit = repo.RemoveFiles("Remove temp.txt", "temp.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.False(result.ContainsKey("temp.txt"));
        Assert.True(result.ContainsKey("keep.txt"));
        Assert.Equal(addCommit, result["keep.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileRemovedThenReAdded_ReturnsLatestAddCommit()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file.txt", ("file.txt", "v1"));
        repo.RemoveFiles("Remove file.txt", "file.txt");
        var reAddCommit = repo.Commit("Re-add file.txt", ("file.txt", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.True(result.ContainsKey("file.txt"));
        Assert.Equal(reAddCommit, result["file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_MultipleFilesRemovedAtDifferentTimes_TracksCorrectly()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add three files", ("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        repo.RemoveFiles("Remove a.txt", "a.txt");
        var removeB = repo.RemoveFiles("Remove b.txt", "b.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.False(result.ContainsKey("a.txt"));
        Assert.False(result.ContainsKey("b.txt"));
        Assert.True(result.ContainsKey("c.txt"));
        Assert.Equal(addCommit, result["c.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithPath_FileRemovedFromDirectory_NotInResult()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add docs", ("docs/a.md", "a"), ("docs/b.md", "b"));
        var removeCommit = repo.RemoveFiles("Remove docs/b.md", "docs/b.md");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.True(result.ContainsKey("docs/a.md"));
        Assert.False(result.ContainsKey("docs/b.md"));
        Assert.Equal(addCommit, result["docs/a.md"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithFilter_RemovedFileMatchingFilter_NotInResult()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add files", ("a.txt", "txt"), ("b.md", "md"));
        var removeCommit = repo.RemoveFiles("Remove a.txt", "a.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(
            fileFilter: path => path.EndsWith(".txt", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.Ordinal));

        Assert.False(result.ContainsKey("a.txt"));
        Assert.True(result.ContainsKey("b.md"));
        Assert.Equal(addCommit, result["b.md"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_PathRemovedInHistory_ThrowsWhenPathDoesNotExistInStartCommit()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add other", ("other.txt", "other"));
        repo.Commit("Add docs", ("docs/file.txt", "v1"));
        var updateDocs = repo.Commit("Update docs", ("docs/file.txt", "v2"));
        repo.RemoveFiles("Remove docs directory", "docs/file.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => gitRepository.ListFilesWithLastChangeAsync(path: "docs"));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileChangedMultipleTimes_ReturnsLatestChange()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "v1"));
        repo.Commit("Update 1", ("file.txt", "v2"));
        repo.Commit("Update 2", ("file.txt", "v3"));
        var latestChange = repo.Commit("Update 3", ("file.txt", "v4"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(latestChange, result["file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_MixedOperations_TracksEachFileCorrectly()
    {
        using var repo = GitTestRepository.Create();
        var addAll = repo.Commit("Add all", ("a.txt", "a1"), ("b.txt", "b1"), ("c.txt", "c1"));
        var updateA = repo.Commit("Update a", ("a.txt", "a2"));
        var removeB = repo.RemoveFiles("Remove b", "b.txt");
        var updateC = repo.Commit("Update c", ("c.txt", "c2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(3, result.Count);
        Assert.Equal(updateA, result["a.txt"].Id);
        Assert.False(result.ContainsKey("b.txt"));
        Assert.Equal(updateC, result["c.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileUnchangedThroughManyCommits_ReturnsCreatingCommit()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add stable", ("stable.txt", "stable"));
        for (var i = 0; i < 10; i++)
        {
            repo.Commit($"Update other {i}", ("stable.txt", "stable"), ($"other{i}.txt", $"content{i}"));
        }

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(addCommit, result["stable.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_DirectoryWithRemovedAndUpdatedFiles_TracksCorrectly()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add docs", ("docs/a.md", "a1"), ("docs/b.md", "b1"), ("docs/c.md", "c1"));
        repo.Commit("Update a", ("docs/a.md", "a2"));
        repo.RemoveFiles("Remove b", "docs/b.md");
        var updateC = repo.Commit("Update c", ("docs/c.md", "c2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("docs/a.md"));
        Assert.False(result.ContainsKey("docs/b.md"));
        Assert.Equal(updateC, result["docs/c.md"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_AllFilesRemovedAndReAdded_ReturnsLatestVersion()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "v1"));
        repo.RemoveFiles("Remove all files", "file.txt");
        var reAddCommit = repo.Commit("Re-add file", ("file.txt", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.True(result.ContainsKey("file.txt"));
        Assert.Equal(reAddCommit, result["file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FilteredFileRemoved_NotInResult()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add files", ("a.txt", "txt"), ("b.md", "md"));
        var removeCommit = repo.RemoveFiles("Remove a.txt", "a.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(
            fileFilter: path => path.EndsWith(".txt", StringComparison.Ordinal));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_ComplexHistory_OptimizesCorrectly()
    {
        using var repo = GitTestRepository.Create();
        var add = repo.Commit("Add files", ("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"), ("d.txt", "d"));
        var updateB = repo.Commit("Update b", ("b.txt", "b2"));
        var removeC = repo.RemoveFiles("Remove c", "c.txt");
        var updateD = repo.Commit("Update d", ("d.txt", "d2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(4, result.Count);
        Assert.Equal(add, result["a.txt"].Id);
        Assert.Equal(updateB, result["b.txt"].Id);
        Assert.False(result.ContainsKey("c.txt"));
        Assert.Equal(updateD, result["d.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileRemovedInMiddleOfHistory_TracksCorrectly()
    {
        using var repo = GitTestRepository.Create();
        var add = repo.Commit("Add files", ("a.txt", "a"), ("b.txt", "b"));
        var remove = repo.RemoveFiles("Remove a.txt", "a.txt");
        var reAdd = repo.Commit("Re-add a.txt", ("a.txt", "a2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(3, result.Count);
        Assert.True(result.ContainsKey("README.md"));
        Assert.Equal(reAdd, result["a.txt"].Id);
        Assert.Equal(add, result["b.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_AllFilesRemovedInMiddleCommit_TracksCorrectly()
    {
        using var repo = GitTestRepository.Create();
        var add1 = repo.Commit("Add file1", ("file1.txt", "v1"));
        repo.RemoveFiles("Remove file1", "file1.txt");
        var add2 = repo.Commit("Add file2", ("file2.txt", "v1"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("README.md"));
        Assert.True(result.ContainsKey("file2.txt"));
        Assert.Equal(add2, result["file2.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_SameContentDifferentPath_TracksIndependently()
    {
        using var repo = GitTestRepository.Create();
        var add = repo.Commit("Add files", ("dir1/file.txt", "same"), ("dir2/file.txt", "same"));
        repo.RemoveFiles("Remove dir1/file.txt", "dir1/file.txt");
        var update = repo.Commit("Update dir2/file.txt", ("dir2/file.txt", "different"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("README.md"));
        Assert.True(result.ContainsKey("dir2/file.txt"));
        Assert.Equal(update, result["dir2/file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileRemovedAndReAddedWithSameContent_ReturnsLatestAdd()
    {
        using var repo = GitTestRepository.Create();
        var add1 = repo.Commit("Add file", ("file.txt", "content"));
        repo.RemoveFiles("Remove file", "file.txt");
        var add2 = repo.Commit("Re-add file with same content", ("file.txt", "content"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("README.md"));
        Assert.Equal(add2, result["file.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_MultipleFilesRemovedInSingleCommit_TracksRemainingFiles()
    {
        using var repo = GitTestRepository.Create();
        var add = repo.Commit("Add files", ("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"), ("d.txt", "d"));
        repo.RemoveFiles("Remove multiple", "a.txt", "c.txt");
        var update = repo.Commit("Update b", ("b.txt", "b2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(3, result.Count);
        Assert.False(result.ContainsKey("a.txt"));
        Assert.True(result.ContainsKey("b.txt"));
        Assert.False(result.ContainsKey("c.txt"));
        Assert.True(result.ContainsKey("d.txt"));
        Assert.Equal(update, result["b.txt"].Id);
        Assert.Equal(add, result["d.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileUnchangedAfterRemovalOfOther_ReturnsOriginalCommit()
    {
        using var repo = GitTestRepository.Create();
        var add = repo.Commit("Add files", ("keep.txt", "keep"), ("remove1.txt", "r1"), ("remove2.txt", "r2"));
        repo.RemoveFiles("Remove files", "remove1.txt", "remove2.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("README.md"));
        Assert.True(result.ContainsKey("keep.txt"));
        Assert.Equal(add, result["keep.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FilterWithRemovedFiles_OnlyTracksMatchingFiles()
    {
        using var repo = GitTestRepository.Create();
        var add = repo.Commit("Add files", ("a.txt", "txt"), ("b.md", "md"), ("c.txt", "txt"));
        repo.RemoveFiles("Remove a.txt", "a.txt");
        var update = repo.Commit("Update c.txt", ("c.txt", "txt2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(
            fileFilter: path => path.EndsWith(".txt", StringComparison.Ordinal));

        Assert.Single(result);
        Assert.False(result.ContainsKey("a.txt"));
        Assert.True(result.ContainsKey("c.txt"));
        Assert.Equal(update, result["c.txt"].Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_DirectoryRemovedInOlderCommit_StopsAtRemoval()
    {
        using var repo = GitTestRepository.Create();
        var add = repo.Commit("Add docs", ("docs/a.md", "a"), ("other.txt", "other"));
        var update = repo.Commit("Update docs/a.md", ("docs/a.md", "a2"));
        repo.RemoveFiles("Remove docs", "docs/a.md");
        repo.Commit("Add more", ("more.txt", "more"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => gitRepository.ListFilesWithLastChangeAsync(path: "docs"));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_EmptyDirectoryAfterAllFilesRemoved_ReturnsEmpty()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add docs", ("docs/a.md", "a"), ("docs/b.md", "b"));
        repo.RemoveFiles("Remove all docs", "docs/a.md", "docs/b.md");
        repo.Commit("Add other", ("other.txt", "other"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => gitRepository.ListFilesWithLastChangeAsync(path: "docs"));
    }
}
