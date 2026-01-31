using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitObjectStoreTests
{
    [Fact]
    public async Task ReadObjectAsync_ReturnsLooseCommitData()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectAsync(repo.Head);

        Assert.Equal(GitObjectType.Commit, data.Type);
        var content = Encoding.UTF8.GetString(data.Content);
        Assert.Contains("Initial commit", content);
    }

    [Fact]
    public async Task ReadObjectAsync_ReturnsBlobContent()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add blob", ("src/data.txt", "payload"));
        var blobHash = new GitHash(repo.RunGit("rev-parse HEAD:src/data.txt").Trim());
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectAsync(blobHash);

        Assert.Equal(GitObjectType.Blob, data.Type);
        Assert.Equal("payload", Encoding.UTF8.GetString(data.Content));
    }

    [Fact]
    public async Task ReadObjectAsync_LoadsDataFromPackFile()
    {
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Pack me", ("file.txt", "content"));
        repo.RunGit("gc --aggressive --prune=now");
        RemoveLooseObject(repo.GitDirectory, commit);

        var store = new GitObjectStore(repo.GitDirectory);
        var data = await store.ReadObjectAsync(commit);

        Assert.Equal(GitObjectType.Commit, data.Type);
        Assert.Contains("Pack me", Encoding.UTF8.GetString(data.Content));
    }

    [Fact]
    public async Task ReadObjectAsync_SupportsSha256Repositories()
    {
        using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectAsync(repo.Head);

        Assert.Equal(GitObjectType.Commit, data.Type);
        Assert.Contains("Initial commit", Encoding.UTF8.GetString(data.Content));
    }

    [Fact]
    public async Task ReadObjectStreamAsync_ReturnsLooseCommitDataAsStream()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);

        await using var objectStream = await store.ReadObjectStreamAsync(repo.Head);

        Assert.Equal(GitObjectType.Commit, objectStream.Type);
        Assert.NotNull(objectStream.Length);
        
        using var reader = new StreamReader(objectStream.Content, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        Assert.Contains("Initial commit", content);
    }

    [Fact]
    public async Task ReadObjectStreamAsync_ReturnsBlobContentAsStream()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add blob", ("src/data.txt", "payload"));
        var blobHash = new GitHash(repo.RunGit("rev-parse HEAD:src/data.txt").Trim());
        var store = new GitObjectStore(repo.GitDirectory);

        await using var objectStream = await store.ReadObjectStreamAsync(blobHash);

        Assert.Equal(GitObjectType.Blob, objectStream.Type);
        Assert.Equal(7, objectStream.Length);
        
        using var reader = new StreamReader(objectStream.Content, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("payload", content);
    }

    [Fact]
    public async Task ReadObjectStreamAsync_LoadsDataFromPackFileAsMemoryStream()
    {
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Pack me", ("file.txt", "content"));
        repo.RunGit("gc --aggressive --prune=now");
        RemoveLooseObject(repo.GitDirectory, commit);

        var store = new GitObjectStore(repo.GitDirectory);
        await using var objectStream = await store.ReadObjectStreamAsync(commit);

        Assert.Equal(GitObjectType.Commit, objectStream.Type);
        Assert.NotNull(objectStream.Length);
        
        using var reader = new StreamReader(objectStream.Content, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        Assert.Contains("Pack me", content);
    }

    private static void RemoveLooseObject(string gitDirectory, GitHash hash)
    {
        var path = Path.Combine(gitDirectory, "objects", hash.Value[..2], hash.Value[2..]);
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
}
