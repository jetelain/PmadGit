using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for stream-based commit operations: AddFileStreamOperation and UpdateFileStreamOperation.
/// </summary>
public sealed class GitRepositoryStreamOperationsTests
{
    #region AddFileStreamOperation - Constructor

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddFileStreamOperation_InvalidPath_Throws(string? path)
    {
        Assert.Throws<ArgumentException>(() => new AddFileStreamOperation(path!, Stream.Null));
    }

    [Fact]
    public void AddFileStreamOperation_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AddFileStreamOperation("file.txt", null!));
    }

    [Fact]
    public void AddFileStreamOperation_SetsProperties()
    {
        using var stream = new MemoryStream();
        var op = new AddFileStreamOperation("src/file.txt", stream);

        Assert.Equal("src/file.txt", op.Path);
        Assert.Same(stream, op.Content);
    }

    #endregion

    #region UpdateFileStreamOperation - Constructor

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateFileStreamOperation_InvalidPath_Throws(string? path)
    {
        Assert.Throws<ArgumentException>(() => new UpdateFileStreamOperation(path!, Stream.Null));
    }

    [Fact]
    public void UpdateFileStreamOperation_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UpdateFileStreamOperation("file.txt", null!));
    }

    [Fact]
    public void UpdateFileStreamOperation_SetsProperties()
    {
        using var stream = new MemoryStream();
        var op = new UpdateFileStreamOperation("src/file.txt", stream);

        Assert.Equal("src/file.txt", op.Path);
        Assert.Same(stream, op.Content);
        Assert.Null(op.ExpectedPreviousHash);
    }

    [Fact]
    public void UpdateFileStreamOperation_SetsExpectedPreviousHash()
    {
        using var stream = new MemoryStream();
        var hash = new GitHash("0000000000000000000000000000000000000001");
        var op = new UpdateFileStreamOperation("src/file.txt", stream, hash);

        Assert.Equal(hash, op.ExpectedPreviousHash);
    }

    #endregion

    #region AddFileStreamOperation - CreateCommitAsync

    [Fact]
    public async Task AddFileStreamOperation_AddsNewFile()
    {
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello from stream"));
        var commitHash = await gitRepository.CreateCommitAsync(
            headRef,
            [new AddFileStreamOperation("stream.txt", stream)],
            CreateMetadata("Add via stream"));

        var content = await gitRepository.ReadFileAsync("stream.txt", commitHash.Value);
        Assert.Equal("hello from stream", Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task AddFileStreamOperation_ProducesSameBlobAsAddFileOperation()
    {
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var fileBytes = Encoding.UTF8.GetBytes("identical content");

        var byteCommit = await gitRepository.CreateCommitAsync(
            headRef,
            [new AddFileOperation("by-bytes.txt", fileBytes)],
            CreateMetadata("Add by bytes"));

        using var stream = new MemoryStream(fileBytes);
        gitRepository.InvalidateCaches();
        var streamCommit = await gitRepository.CreateCommitAsync(
            headRef,
            [new AddFileStreamOperation("by-stream.txt", stream)],
            CreateMetadata("Add by stream"));

        var byteHash = (await gitRepository.ReadFileAndHashAsync("by-bytes.txt", byteCommit.Value)).Hash;
        var streamHash = (await gitRepository.ReadFileAndHashAsync("by-stream.txt", streamCommit.Value)).Hash;
        Assert.Equal(byteHash, streamHash);
    }

    [Fact]
    public async Task AddFileStreamOperation_FileAlreadyExists_Throws()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Seed", ("existing.txt", "original"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("new content"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gitRepository.CreateCommitAsync(
                headRef,
                [new AddFileStreamOperation("existing.txt", stream)],
                CreateMetadata("Should fail")));
    }

    [Fact]
    public async Task AddFileStreamOperation_NestedPath_CreatesIntermediateDirectories()
    {
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("deep content"));
        var commitHash = await gitRepository.CreateCommitAsync(
            headRef,
            [new AddFileStreamOperation("a/b/c/deep.txt", stream)],
            CreateMetadata("Add nested via stream"));

        var content = await gitRepository.ReadFileAsync("a/b/c/deep.txt", commitHash.Value);
        Assert.Equal("deep content", Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task AddFileStreamOperation_IsVisibleToGitCli()
    {
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("cli visible"));
        await gitRepository.CreateCommitAsync(
            headRef,
            [new AddFileStreamOperation("cli.txt", stream)],
            CreateMetadata("Stream commit"));

        var gitShow = repo.RunGit("show HEAD:cli.txt");
        Assert.Equal("cli visible", gitShow.Trim());
    }

    #endregion

    #region UpdateFileStreamOperation - CreateCommitAsync

    [Fact]
    public async Task UpdateFileStreamOperation_UpdatesExistingFile()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Seed", ("data.txt", "original"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("updated via stream"));
        var commitHash = await gitRepository.CreateCommitAsync(
            headRef,
            [new UpdateFileStreamOperation("data.txt", stream)],
            CreateMetadata("Update via stream"));

        var content = await gitRepository.ReadFileAsync("data.txt", commitHash.Value);
        Assert.Equal("updated via stream", Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task UpdateFileStreamOperation_FileDoesNotExist_Throws()
    {
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("content"));
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => gitRepository.CreateCommitAsync(
                headRef,
                [new UpdateFileStreamOperation("missing.txt", stream)],
                CreateMetadata("Should fail")));
    }

    [Fact]
    public async Task UpdateFileStreamOperation_WithCorrectExpectedHash_Succeeds()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Seed", ("config.txt", "version 1"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        var fileInfo = await gitRepository.ReadFileAndHashAsync("config.txt");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("version 2"));
        var commitHash = await gitRepository.CreateCommitAsync(
            headRef,
            [new UpdateFileStreamOperation("config.txt", stream, fileInfo.Hash)],
            CreateMetadata("Update with hash validation"));

        var content = await gitRepository.ReadFileAsync("config.txt", commitHash.Value);
        Assert.Equal("version 2", Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task UpdateFileStreamOperation_WithIncorrectExpectedHash_ThrowsGitFileConflictException()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Seed", ("config.txt", "version 1"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        var wrongHash = new GitHash("0000000000000000000000000000000000000000");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("version 2"));
        var exception = await Assert.ThrowsAsync<GitFileConflictException>(
            () => gitRepository.CreateCommitAsync(
                headRef,
                [new UpdateFileStreamOperation("config.txt", stream, wrongHash)],
                CreateMetadata("Update with wrong hash")));

        Assert.Contains("config.txt", exception.Message);
        Assert.Contains("has hash", exception.Message);
        Assert.Contains("expected", exception.Message);
        Assert.Equal("config.txt", exception.FilePath);
    }

    [Fact]
    public async Task UpdateFileStreamOperation_SameContent_NoChangeThrows()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Seed", ("data.txt", "same"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        // Use content that differs from "same" but then update back - this ensures the blob already
        // exists in the object store so the stream path exercises the no-effective-change guard.
        await gitRepository.CreateCommitAsync(
            headRef,
            [new AddFileOperation("other.txt", Encoding.UTF8.GetBytes("other"))],
            CreateMetadata("Add other"));

        gitRepository.InvalidateCaches();

        // Now update data.txt with the identical content using the byte-based operation to seed the
        // blob into the object store first, then verify the stream operation also detects no change.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("same"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gitRepository.CreateCommitAsync(
                headRef,
                [new UpdateFileStreamOperation("data.txt", stream)],
                CreateMetadata("No change")));
    }

    [Fact]
    public async Task UpdateFileStreamOperation_ProducesSameBlobAsUpdateFileOperation()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Seed", ("file.txt", "original"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var newBytes = Encoding.UTF8.GetBytes("new content");

        var byteCommit = await gitRepository.CreateCommitAsync(
            headRef,
            [new UpdateFileOperation("file.txt", newBytes)],
            CreateMetadata("Update by bytes"));

        gitRepository.InvalidateCaches();
        repo.Commit("Reset", ("file.txt", "original"));
        gitRepository.InvalidateCaches();

        using var stream = new MemoryStream(newBytes);
        var streamCommit = await gitRepository.CreateCommitAsync(
            headRef,
            [new UpdateFileStreamOperation("file.txt", stream)],
            CreateMetadata("Update by stream"));

        var byteHash = (await gitRepository.ReadFileAndHashAsync("file.txt", byteCommit.Value)).Hash;
        var streamHash = (await gitRepository.ReadFileAndHashAsync("file.txt", streamCommit.Value)).Hash;
        Assert.Equal(byteHash, streamHash);
    }

    #endregion

    #region Mixed stream and non-stream operations

    [Fact]
    public async Task MixedOperations_StreamAndBytes_Succeeds()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Seed", ("update.txt", "v1"), ("remove.txt", "old"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        using var addStream = new MemoryStream(Encoding.UTF8.GetBytes("added via stream"));
        using var updateStream = new MemoryStream(Encoding.UTF8.GetBytes("updated via stream"));

        var commitHash = await gitRepository.CreateCommitAsync(
            headRef,
            [
                new AddFileStreamOperation("added.txt", addStream),
                new UpdateFileStreamOperation("update.txt", updateStream),
                new AddFileOperation("added-bytes.txt", Encoding.UTF8.GetBytes("added via bytes")),
                new RemoveFileOperation("remove.txt")
            ],
            CreateMetadata("Mixed stream and byte operations"));

        Assert.Equal("added via stream", Encoding.UTF8.GetString(await gitRepository.ReadFileAsync("added.txt", commitHash.Value)));
        Assert.Equal("updated via stream", Encoding.UTF8.GetString(await gitRepository.ReadFileAsync("update.txt", commitHash.Value)));
        Assert.Equal("added via bytes", Encoding.UTF8.GetString(await gitRepository.ReadFileAsync("added-bytes.txt", commitHash.Value)));
        await Assert.ThrowsAsync<FileNotFoundException>(() => gitRepository.ReadFileAsync("remove.txt", commitHash.Value));
    }

    #endregion

    #region Helper Methods

    private static GitCommitMetadata CreateMetadata(string message)
        => new(
            message,
            new GitCommitSignature(
                "Api User",
                "api@example.com",
                new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero)));

    #endregion
}
