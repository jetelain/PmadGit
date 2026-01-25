using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitRepository path existence checking methods.
/// </summary>
public sealed class GitRepositoryPathExistsTests
{
    #region GetPathTypeAsync

    [Fact]
    public async Task GetPathTypeAsync_ForFile_ReturnsBlob()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("file.txt");

        Assert.Equal(GitTreeEntryKind.Blob, type);
    }

    [Fact]
    public async Task GetPathTypeAsync_ForDirectory_ReturnsTree()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add directory", ("dir/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("dir");

        Assert.Equal(GitTreeEntryKind.Tree, type);
    }

    [Fact]
    public async Task GetPathTypeAsync_ForNestedFile_ReturnsBlob()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add nested file", ("a/b/c/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("a/b/c/file.txt");

        Assert.Equal(GitTreeEntryKind.Blob, type);
    }

    [Fact]
    public async Task GetPathTypeAsync_ForNestedDirectory_ReturnsTree()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add nested directory", ("a/b/c/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("a/b");

        Assert.Equal(GitTreeEntryKind.Tree, type);
    }

    [Fact]
    public async Task GetPathTypeAsync_ForNonExistentPath_ReturnsNull()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("non-existent.txt");

        Assert.Null(type);
    }

    [Fact]
    public async Task GetPathTypeAsync_ForRootPath_ReturnsTree()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("");

        Assert.Equal(GitTreeEntryKind.Tree, type);
    }

    [Fact]
    public async Task GetPathTypeAsync_WithBackslashPath_NormalizesCorrectly()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("dir/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("dir\\file.txt");

        Assert.Equal(GitTreeEntryKind.Blob, type);
    }

    [Fact]
    public async Task GetPathTypeAsync_WithReference_ChecksSpecificCommit()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("Add file", ("file.txt", "content"));
        repo.RunGit("rm file.txt");
        var commit2 = repo.Commit("Remove file");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var typeInCommit1 = await gitRepository.GetPathTypeAsync("file.txt", commit1.Value);
        var typeInCommit2 = await gitRepository.GetPathTypeAsync("file.txt", commit2.Value);

        Assert.Equal(GitTreeEntryKind.Blob, typeInCommit1);
        Assert.Null(typeInCommit2);
    }

    [Fact]
    public async Task GetPathTypeAsync_WithPathThroughFile_ReturnsNull()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var type = await gitRepository.GetPathTypeAsync("file.txt/subpath");

        Assert.Null(type);
    }

    #endregion

    #region PathExistsAsync

    [Fact]
    public async Task PathExistsAsync_ForExistingFile_ReturnsTrue()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.PathExistsAsync("file.txt");

        Assert.True(exists);
    }

    [Fact]
    public async Task PathExistsAsync_ForExistingDirectory_ReturnsTrue()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add directory", ("dir/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.PathExistsAsync("dir");

        Assert.True(exists);
    }

    [Fact]
    public async Task PathExistsAsync_ForNonExistentPath_ReturnsFalse()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.PathExistsAsync("non-existent.txt");

        Assert.False(exists);
    }

    [Fact]
    public async Task PathExistsAsync_WithReference_ChecksSpecificCommit()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("Add file", ("file.txt", "content"));
        repo.RunGit("rm file.txt");
        var commit2 = repo.Commit("Remove file");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var existsInCommit1 = await gitRepository.PathExistsAsync("file.txt", commit1.Value);
        var existsInCommit2 = await gitRepository.PathExistsAsync("file.txt", commit2.Value);

        Assert.True(existsInCommit1);
        Assert.False(existsInCommit2);
    }

    #endregion

    #region FileExistsAsync

    [Fact]
    public async Task FileExistsAsync_ForExistingFile_ReturnsTrue()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.FileExistsAsync("file.txt");

        Assert.True(exists);
    }

    [Fact]
    public async Task FileExistsAsync_ForDirectory_ReturnsFalse()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add directory", ("dir/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.FileExistsAsync("dir");

        Assert.False(exists);
    }

    [Fact]
    public async Task FileExistsAsync_ForNonExistentPath_ReturnsFalse()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.FileExistsAsync("non-existent.txt");

        Assert.False(exists);
    }

    [Fact]
    public async Task FileExistsAsync_WithReference_ChecksSpecificCommit()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("Add file", ("file.txt", "content"));
        repo.RunGit("rm file.txt");
        var commit2 = repo.Commit("Remove file");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var existsInCommit1 = await gitRepository.FileExistsAsync("file.txt", commit1.Value);
        var existsInCommit2 = await gitRepository.FileExistsAsync("file.txt", commit2.Value);

        Assert.True(existsInCommit1);
        Assert.False(existsInCommit2);
    }

    [Fact]
    public async Task FileExistsAsync_ForNestedFile_ReturnsTrue()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add nested file", ("a/b/c/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.FileExistsAsync("a/b/c/file.txt");

        Assert.True(exists);
    }

    [Fact]
    public async Task FileExistsAsync_WithBackslashPath_NormalizesCorrectly()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("dir/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.FileExistsAsync("dir\\file.txt");

        Assert.True(exists);
    }

    #endregion

    #region DirectoryExistsAsync

    [Fact]
    public async Task DirectoryExistsAsync_ForExistingDirectory_ReturnsTrue()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add directory", ("dir/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.DirectoryExistsAsync("dir");

        Assert.True(exists);
    }

    [Fact]
    public async Task DirectoryExistsAsync_ForFile_ReturnsFalse()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.DirectoryExistsAsync("file.txt");

        Assert.False(exists);
    }

    [Fact]
    public async Task DirectoryExistsAsync_ForNonExistentPath_ReturnsFalse()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.DirectoryExistsAsync("non-existent-dir");

        Assert.False(exists);
    }

    [Fact]
    public async Task DirectoryExistsAsync_WithReference_ChecksSpecificCommit()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("Add directory", ("dir/file.txt", "content"));
        repo.RunGit("rm -r dir");
        var commit2 = repo.Commit("Remove directory");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var existsInCommit1 = await gitRepository.DirectoryExistsAsync("dir", commit1.Value);
        var existsInCommit2 = await gitRepository.DirectoryExistsAsync("dir", commit2.Value);

        Assert.True(existsInCommit1);
        Assert.False(existsInCommit2);
    }

    [Fact]
    public async Task DirectoryExistsAsync_ForNestedDirectory_ReturnsTrue()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add nested directory", ("a/b/c/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.DirectoryExistsAsync("a/b");

        Assert.True(exists);
    }

    [Fact]
    public async Task DirectoryExistsAsync_WithBackslashPath_NormalizesCorrectly()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add directory", ("dir/sub/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.DirectoryExistsAsync("dir\\sub");

        Assert.True(exists);
    }

    [Fact]
    public async Task DirectoryExistsAsync_ForRootPath_ReturnsTrue()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var exists = await gitRepository.DirectoryExistsAsync("");

        Assert.True(exists);
    }

    #endregion

    #region Cancellation Token Support

    [Fact]
    public async Task GetPathTypeAsync_RespectsCancellationToken()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gitRepository.GetPathTypeAsync("file.txt", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task PathExistsAsync_RespectsCancellationToken()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gitRepository.PathExistsAsync("file.txt", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task FileExistsAsync_RespectsCancellationToken()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gitRepository.FileExistsAsync("file.txt", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task DirectoryExistsAsync_RespectsCancellationToken()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add directory", ("dir/file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gitRepository.DirectoryExistsAsync("dir", cancellationToken: cts.Token));
    }

    #endregion
}
