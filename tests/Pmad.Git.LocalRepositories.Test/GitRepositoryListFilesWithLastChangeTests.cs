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

        Assert.Contains(result, e => e.Path == "file.txt");
        Assert.Equal(addCommit, result.Single(e => e.Path == "file.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_UpdatedFile_ReturnsNewestCommit()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "v1"));
        var updateCommit = repo.Commit("Update file", ("file.txt", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(updateCommit, result.Single(e => e.Path == "file.txt").Commit.Id);
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

        Assert.Equal(commitC, result.Single(e => e.Path == "a.txt").Commit.Id);
        Assert.Equal(commitB, result.Single(e => e.Path == "b.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileNeverUpdated_ReturnsCreatingCommit()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add stable.txt", ("stable.txt", "content"));
        repo.Commit("Add other.txt", ("other.txt", "content"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(addCommit, result.Single(e => e.Path == "stable.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithPath_OnlyReturnsFilesUnderPath()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add files", ("docs/readme.md", "doc"), ("src/main.cs", "code"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.Contains(result, e => e.Path == "docs/readme.md");
        Assert.DoesNotContain(result, e => e.Path == "src/main.cs");
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithPath_UpdatedFileUnderPath_ReturnsNewestCommit()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add docs", ("docs/a.md", "v1"), ("docs/b.md", "v1"));
        var updateCommit = repo.Commit("Update docs/a.md", ("docs/a.md", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.Equal(updateCommit, result.Single(e => e.Path == "docs/a.md").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithFileFilter_ExcludesNonMatchingFiles()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add files", ("a.md", "md"), ("b.txt", "txt"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(
            fileFilter: path => path.EndsWith(".md", StringComparison.Ordinal));

        Assert.Contains(result, e => e.Path == "a.md");
        Assert.DoesNotContain(result, e => e.Path == "b.txt");
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

        Assert.Equal(taggedCommit, result.Single(e => e.Path == "file.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_NonExistentPath_ThrowsDirectoryNotFoundException()
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

        Assert.Contains(result, e => e.Path == "src/a/file1.txt");
        Assert.Contains(result, e => e.Path == "src/b/file2.txt");
        Assert.Equal(commit, result.Single(e => e.Path == "src/a/file1.txt").Commit.Id);
        Assert.Equal(commit, result.Single(e => e.Path == "src/b/file2.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_PathDirectoryCreatedInOlderCommit_StillReturnsFiles()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add docs", ("docs/readme.md", "v1"));
        var updateCommit = repo.Commit("Update docs", ("docs/readme.md", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.Contains(result, e => e.Path == "docs/readme.md");
        Assert.Equal(updateCommit, result.Single(e => e.Path == "docs/readme.md").Commit.Id);
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

        Assert.Contains(result, e => e.Path == "README.md");
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_UnchangedFileAmongUpdatedOnes_ReturnsCreatingCommit()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add both", ("stable.txt", "same"), ("changing.txt", "v1"));
        repo.Commit("Update changing only", ("changing.txt", "v2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Equal(addCommit, result.Single(e => e.Path == "stable.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_RemovedFile_DoesNotReturnRemovedFile()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add files", ("file1.txt", "content1"), ("file2.txt", "content2"));
        repo.RemoveFiles("Remove file2", "file2.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.Contains(result, e => e.Path == "file1.txt");
        Assert.DoesNotContain(result, e => e.Path == "file2.txt");
        Assert.Equal(addCommit, result.Single(e => e.Path == "file1.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_FileAddedAndRemoved_NotInResult()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add temp.txt", ("temp.txt", "temp"), ("keep.txt", "keep"));
        var removeCommit = repo.RemoveFiles("Remove temp.txt", "temp.txt");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        Assert.DoesNotContain(result, e => e.Path == "temp.txt");
        Assert.Contains(result, e => e.Path == "keep.txt");
        Assert.Equal(addCommit, result.Single(e => e.Path == "keep.txt").Commit.Id);
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

        Assert.Contains(result, e => e.Path == "file.txt");
        Assert.Equal(reAddCommit, result.Single(e => e.Path == "file.txt").Commit.Id);
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

        Assert.DoesNotContain(result, e => e.Path == "a.txt");
        Assert.DoesNotContain(result, e => e.Path == "b.txt");
        Assert.Contains(result, e => e.Path == "c.txt");
        Assert.Equal(addCommit, result.Single(e => e.Path == "c.txt").Commit.Id);
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_WithPath_FileRemovedFromDirectory_NotInResult()
    {
        using var repo = GitTestRepository.Create();
        var addCommit = repo.Commit("Add docs", ("docs/a.md", "a"), ("docs/b.md", "b"));
        var removeCommit = repo.RemoveFiles("Remove docs/b.md", "docs/b.md");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync(path: "docs");

        Assert.Contains(result, e => e.Path == "docs/a.md");
        Assert.DoesNotContain(result, e => e.Path == "docs/b.md");
        Assert.Equal(addCommit, result.Single(e => e.Path == "docs/a.md").Commit.Id);
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

        Assert.DoesNotContain(result, e => e.Path == "a.txt");
        Assert.Contains(result, e => e.Path == "b.md");
        Assert.Equal(addCommit, result.Single(e => e.Path == "b.md").Commit.Id);
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

        Assert.Equal(latestChange, result.Single(e => e.Path == "file.txt").Commit.Id);
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
        Assert.Equal(updateA, result.Single(e => e.Path == "a.txt").Commit.Id);
        Assert.DoesNotContain(result, e => e.Path == "b.txt");
        Assert.Equal(updateC, result.Single(e => e.Path == "c.txt").Commit.Id);
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

        Assert.Equal(addCommit, result.Single(e => e.Path == "stable.txt").Commit.Id);
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
        Assert.Contains(result, e => e.Path == "docs/a.md");
        Assert.DoesNotContain(result, e => e.Path == "docs/b.md");
        Assert.Equal(updateC, result.Single(e => e.Path == "docs/c.md").Commit.Id);
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

        Assert.Contains(result, e => e.Path == "file.txt");
        Assert.Equal(reAddCommit, result.Single(e => e.Path == "file.txt").Commit.Id);
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
        Assert.Equal(add, result.Single(e => e.Path == "a.txt").Commit.Id);
        Assert.Equal(updateB, result.Single(e => e.Path == "b.txt").Commit.Id);
        Assert.DoesNotContain(result, e => e.Path == "c.txt");
        Assert.Equal(updateD, result.Single(e => e.Path == "d.txt").Commit.Id);
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
        Assert.Contains(result, e => e.Path == "README.md");
        Assert.Equal(reAdd, result.Single(e => e.Path == "a.txt").Commit.Id);
        Assert.Equal(add, result.Single(e => e.Path == "b.txt").Commit.Id);
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
        Assert.Contains(result, e => e.Path == "README.md");
        Assert.Contains(result, e => e.Path == "file2.txt");
        Assert.Equal(add2, result.Single(e => e.Path == "file2.txt").Commit.Id);
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
        Assert.Contains(result, e => e.Path == "README.md");
        Assert.Contains(result, e => e.Path == "dir2/file.txt");
        Assert.Equal(update, result.Single(e => e.Path == "dir2/file.txt").Commit.Id);
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
        Assert.Contains(result, e => e.Path == "README.md");
        Assert.Equal(add2, result.Single(e => e.Path == "file.txt").Commit.Id);
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
        Assert.DoesNotContain(result, e => e.Path == "a.txt");
        Assert.Contains(result, e => e.Path == "b.txt");
        Assert.DoesNotContain(result, e => e.Path == "c.txt");
        Assert.Contains(result, e => e.Path == "d.txt");
        Assert.Equal(update, result.Single(e => e.Path == "b.txt").Commit.Id);
        Assert.Equal(add, result.Single(e => e.Path == "d.txt").Commit.Id);
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
        Assert.Contains(result, e => e.Path == "README.md");
        Assert.Contains(result, e => e.Path == "keep.txt");
        Assert.Equal(add, result.Single(e => e.Path == "keep.txt").Commit.Id);
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
        Assert.DoesNotContain(result, e => e.Path == "a.txt");
        Assert.Contains(result, e => e.Path == "c.txt");
        Assert.Equal(update, result.Single(e => e.Path == "c.txt").Commit.Id);
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
    public async Task ListFilesWithLastChangeAsync_EmptyDirectoryAfterAllFilesRemoved_ThrowsDirectoryNotFoundException()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add docs", ("docs/a.md", "a"), ("docs/b.md", "b"));
        repo.RemoveFiles("Remove all docs", "docs/a.md", "docs/b.md");
        repo.Commit("Add other", ("other.txt", "other"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => gitRepository.ListFilesWithLastChangeAsync(path: "docs"));
    }

    [Fact]
    public async Task ListFilesWithLastChangeAsync_ResultIsSortedByPathOrdinal()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add files", ("z.txt", "z"), ("a.txt", "a"), ("m.txt", "m"), ("B.txt", "B"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var result = await gitRepository.ListFilesWithLastChangeAsync();

        var paths = result.Select(e => e.Path).ToList();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal), paths);
    }
}

