using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitRepository.ReadFileStreamAsync.
/// </summary>
public sealed class GitRepositoryReadFileStreamTests
{
    #region Basic content reading

    [Fact]
    public async Task ReadFileStreamAsync_ReturnsCorrectContent()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("hello.txt", "hello from stream"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("hello.txt");

        Assert.Equal(GitObjectType.Blob, result.Type);
        using var reader = new StreamReader(result.Content, Encoding.UTF8);
        Assert.Equal("hello from stream", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ReadFileStreamAsync_ReturnsCorrectLength()
    {
        using var repo = GitTestRepository.Create();
        var content = "length check";
        repo.Commit("Add file", ("file.txt", content));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("file.txt");

        Assert.Equal(Encoding.UTF8.GetByteCount(content), result.Length);
    }

    [Fact]
    public async Task ReadFileStreamAsync_ContentMatchesReadFileAsync()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("data.txt", "compare me"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var bytes = await gitRepository.ReadFileAsync("data.txt");

        await using var streamResult = await gitRepository.ReadFileStreamAsync("data.txt");
        using var ms = new MemoryStream();
        await streamResult.Content.CopyToAsync(ms);

        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task ReadFileStreamAsync_ContentMatchesGitShow()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("git.txt", "git content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("git.txt");
        using var reader = new StreamReader(result.Content, Encoding.UTF8);
        var streamContent = await reader.ReadToEndAsync();

        var gitContent = repo.RunGit("show HEAD:git.txt").Trim();
        Assert.Equal(gitContent, streamContent);
    }

    [Fact]
    public async Task ReadFileStreamAsync_EmptyFile_ReturnsEmptyStream()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add empty file", ("empty.txt", ""));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("empty.txt");
        using var ms = new MemoryStream();
        await result.Content.CopyToAsync(ms);

        Assert.Equal(0, result.Length);
        Assert.Empty(ms.ToArray());
    }

    #endregion

    #region Reference resolution

    [Fact]
    public async Task ReadFileStreamAsync_WithSpecificCommitHash_ReturnsCorrectVersion()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("Version 1", ("file.txt", "version 1"));
        repo.Commit("Version 2", ("file.txt", "version 2"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("file.txt", commit1.Value);
        using var reader = new StreamReader(result.Content, Encoding.UTF8);

        Assert.Equal("version 1", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ReadFileStreamAsync_WithBranchName_ReadsFromHead()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("branch.txt", "branch content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var branchName = headRef.Replace("refs/heads/", "");

        await using var result = await gitRepository.ReadFileStreamAsync("branch.txt", branchName);
        using var reader = new StreamReader(result.Content, Encoding.UTF8);

        Assert.Equal("branch content", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ReadFileStreamAsync_DifferentVersionsHaveDifferentContent()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("v1", ("versioned.txt", "v1 content"));
        var commit2 = repo.Commit("v2", ("versioned.txt", "v2 content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var r1 = await gitRepository.ReadFileStreamAsync("versioned.txt", commit1.Value);
        await using var r2 = await gitRepository.ReadFileStreamAsync("versioned.txt", commit2.Value);

        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        await r1.Content.CopyToAsync(ms1);
        await r2.Content.CopyToAsync(ms2);

        Assert.Equal("v1 content", Encoding.UTF8.GetString(ms1.ToArray()));
        Assert.Equal("v2 content", Encoding.UTF8.GetString(ms2.ToArray()));
    }

    #endregion

    #region Nested paths

    [Fact]
    public async Task ReadFileStreamAsync_NestedPath_ReturnsCorrectContent()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add nested", ("src/lib/module.txt", "nested content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("src/lib/module.txt");
        using var reader = new StreamReader(result.Content, Encoding.UTF8);

        Assert.Equal("nested content", await reader.ReadToEndAsync());
    }

    #endregion

    #region Pack file objects

    [Fact]
    public async Task ReadFileStreamAsync_LooseObject_ReturnsCorrectContent()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add loose", ("loose.txt", "loose content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("loose.txt");
        using var reader = new StreamReader(result.Content, Encoding.UTF8);

        Assert.Equal("loose content", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ReadFileStreamAsync_PackedObject_ReturnsCorrectContent()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add packed", ("packed.txt", "packed content"));
        repo.RunGit("gc --aggressive --prune=now");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("packed.txt");
        using var reader = new StreamReader(result.Content, Encoding.UTF8);

        Assert.Equal("packed content", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ReadFileStreamAsync_PackedObject_ContentMatchesLooseRead()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("compare.txt", "compare content"));

        var looseRepository = GitRepository.Open(repo.WorkingDirectory);
        var looseBytes = await looseRepository.ReadFileAsync("compare.txt");

        repo.RunGit("gc --aggressive --prune=now");
        var packedRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var packedStream = await packedRepository.ReadFileStreamAsync("compare.txt");
        using var ms = new MemoryStream();
        await packedStream.Content.CopyToAsync(ms);

        Assert.Equal(looseBytes, ms.ToArray());
    }

    #endregion

    #region Error cases

    [Fact]
    public async Task ReadFileStreamAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => gitRepository.ReadFileStreamAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task ReadFileStreamAsync_FileNotFoundInSpecificCommit_ThrowsFileNotFoundException()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("No file", ("other.txt", "other"));
        repo.Commit("Add file", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => gitRepository.ReadFileStreamAsync("file.txt", commit1.Value));
    }

    [Fact]
    public async Task ReadFileStreamAsync_InvalidReference_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gitRepository.ReadFileStreamAsync("README.md", "refs/heads/nonexistent"));
    }

    #endregion

    #region Dispose behaviour

    [Fact]
    public async Task ReadFileStreamAsync_DisposedStream_CannotBeReadAgain()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("dispose.txt", "dispose content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        GitObjectStream result = await gitRepository.ReadFileStreamAsync("dispose.txt");
        result.Dispose();

        // Reading after dispose should throw
        Assert.Throws<ObjectDisposedException>(() => result.Content.ReadByte());
    }

    [Fact]
    public async Task ReadFileStreamAsync_AwaitUsingDisposes_StreamIsDisposed()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add file", ("async-dispose.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        Stream capturedStream;
        await using (var result = await gitRepository.ReadFileStreamAsync("async-dispose.txt"))
        {
            capturedStream = result.Content;
            // consume stream inside using scope
            using var ms = new MemoryStream();
            await capturedStream.CopyToAsync(ms);
            Assert.NotEmpty(ms.ToArray());
        }

        Assert.Throws<ObjectDisposedException>(() => capturedStream.ReadByte());
    }

    #endregion

    #region SHA-256 repository

    [Fact]
    public async Task ReadFileStreamAsync_Sha256Repository_ReturnsCorrectContent()
    {
        using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
        repo.Commit("Add file", ("sha256.txt", "sha256 content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        await using var result = await gitRepository.ReadFileStreamAsync("sha256.txt");
        using var reader = new StreamReader(result.Content, Encoding.UTF8);

        Assert.Equal(GitObjectType.Blob, result.Type);
        Assert.Equal("sha256 content", await reader.ReadToEndAsync());
    }

    #endregion
}
