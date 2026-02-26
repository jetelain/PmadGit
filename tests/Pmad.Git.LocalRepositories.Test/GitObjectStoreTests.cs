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
        
        using var reader = new StreamReader(objectStream.Content, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        Assert.Contains("Pack me", content);
    }

    [Fact]
    public async Task WriteObjectAsync_WithMemory_WritesAndReadsBackBlobContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("test blob content");

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);

        Assert.NotEqual(GitHash.Zero, hash);
        var readData = await store.ReadObjectAsync(hash);
        Assert.Equal(GitObjectType.Blob, readData.Type);
        Assert.Equal("test blob content", Encoding.UTF8.GetString(readData.Content));
    }

    [Fact]
    public async Task WriteObjectAsync_WithStream_WritesAndReadsBackBlobContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("test blob from stream");
        using var stream = new MemoryStream(content);

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, stream, CancellationToken.None);

        Assert.NotEqual(GitHash.Zero, hash);
        var readData = await store.ReadObjectAsync(hash);
        Assert.Equal(GitObjectType.Blob, readData.Type);
        Assert.Equal("test blob from stream", Encoding.UTF8.GetString(readData.Content));
    }

    [Fact]
    public async Task WriteObjectAsync_WithMemory_ReusesExistingObject()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("duplicate content");

        var hash1 = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);
        var hash2 = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);

        Assert.Equal(hash1, hash2);
        var readData = await store.ReadObjectAsync(hash1);
        Assert.Equal("duplicate content", Encoding.UTF8.GetString(readData.Content));
    }

    [Fact]
    public async Task WriteObjectAsync_WithStream_ReusesExistingObject()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("duplicate stream content");
        
        using var stream1 = new MemoryStream(content);
        var hash1 = await store.WriteObjectAsync(GitObjectType.Blob, stream1, CancellationToken.None);
        
        using var stream2 = new MemoryStream(content);
        var hash2 = await store.WriteObjectAsync(GitObjectType.Blob, stream2, CancellationToken.None);

        Assert.Equal(hash1, hash2);
        var readData = await store.ReadObjectAsync(hash1);
        Assert.Equal("duplicate stream content", Encoding.UTF8.GetString(readData.Content));
    }

    [Fact]
    public async Task WriteObjectAsync_WithMemory_HandlesEmptyContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Array.Empty<byte>();

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);

        Assert.NotEqual(GitHash.Zero, hash);
        var readData = await store.ReadObjectAsync(hash);
        Assert.Equal(GitObjectType.Blob, readData.Type);
        Assert.Empty(readData.Content);
    }

    [Fact]
    public async Task WriteObjectAsync_WithStream_HandlesEmptyContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        using var stream = new MemoryStream(Array.Empty<byte>());

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, stream, CancellationToken.None);

        Assert.NotEqual(GitHash.Zero, hash);
        var readData = await store.ReadObjectAsync(hash);
        Assert.Equal(GitObjectType.Blob, readData.Type);
        Assert.Empty(readData.Content);
    }

    [Fact]
    public async Task WriteObjectAsync_WithMemory_HandlesLargeContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = new byte[1024 * 1024];
        new Random(42).NextBytes(content);

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);

        Assert.NotEqual(GitHash.Zero, hash);
        var readData = await store.ReadObjectAsync(hash);
        Assert.Equal(GitObjectType.Blob, readData.Type);
        Assert.Equal(content, readData.Content);
    }

    [Fact]
    public async Task WriteObjectAsync_WithStream_HandlesLargeContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = new byte[1024 * 1024];
        new Random(42).NextBytes(content);
        using var stream = new MemoryStream(content);

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, stream, CancellationToken.None);

        Assert.NotEqual(GitHash.Zero, hash);
        var readData = await store.ReadObjectAsync(hash);
        Assert.Equal(GitObjectType.Blob, readData.Type);
        Assert.Equal(content, readData.Content);
    }

    [Fact]
    public async Task WriteObjectAsync_WithMemory_DifferentTypesProduceDifferentHashes()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("same content");

        var blobHash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);
        var treeHash = await store.WriteObjectAsync(GitObjectType.Tree, content, CancellationToken.None);

        Assert.NotEqual(blobHash, treeHash);
    }

    [Fact]
    public async Task WriteObjectAsync_WithStream_DifferentTypesProduceDifferentHashes()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("same content");
        
        using var stream1 = new MemoryStream(content);
        var blobHash = await store.WriteObjectAsync(GitObjectType.Blob, stream1, CancellationToken.None);
        
        using var stream2 = new MemoryStream(content);
        var treeHash = await store.WriteObjectAsync(GitObjectType.Tree, stream2, CancellationToken.None);

        Assert.NotEqual(blobHash, treeHash);
    }

    [Fact]
    public async Task WriteObjectAsync_StreamAndMemoryVariantsProduceSameHash()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("identical content");

        var memoryHash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);
        
        using var stream = new MemoryStream(content);
        var streamHash = await store.WriteObjectAsync(GitObjectType.Blob, stream, CancellationToken.None);

        Assert.Equal(memoryHash, streamHash);
    }

    [Fact]
    public async Task WriteObjectAsync_StreamAndMemoryVariantsProduceSameStoredContent()
    {
        using var repo1 = GitTestRepository.Create();
        using var repo2 = GitTestRepository.Create();
        var store1 = new GitObjectStore(repo1.GitDirectory);
        var store2 = new GitObjectStore(repo2.GitDirectory);
        var content = Encoding.UTF8.GetBytes("test content for comparison");

        var memoryHash = await store1.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);
        
        using var stream = new MemoryStream(content);
        var streamHash = await store2.WriteObjectAsync(GitObjectType.Blob, stream, CancellationToken.None);

        Assert.Equal(memoryHash, streamHash);
        
        var memoryData = await store1.ReadObjectAsync(memoryHash);
        var streamData = await store2.ReadObjectAsync(streamHash);
        
        Assert.Equal(memoryData.Type, streamData.Type);
        Assert.Equal(memoryData.Content, streamData.Content);
    }

    [Fact]
    public async Task WriteObjectAsync_StreamAndMemoryVariantsProduceSameHashForLargeContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = new byte[1024 * 1024];
        new Random(123).NextBytes(content);

        var memoryHash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);
        
        using var stream = new MemoryStream(content);
        var streamHash = await store.WriteObjectAsync(GitObjectType.Blob, stream, CancellationToken.None);

        Assert.Equal(memoryHash, streamHash);
    }

    [Fact]
    public async Task WriteObjectAsync_StreamAndMemoryVariantsProduceSameHashForEmptyContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Array.Empty<byte>();

        var memoryHash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);
        
        using var stream = new MemoryStream(content);
        var streamHash = await store.WriteObjectAsync(GitObjectType.Blob, stream, CancellationToken.None);

        Assert.Equal(memoryHash, streamHash);
    }

    [Fact]
    public async Task WriteObjectAsync_StreamAndMemoryVariantsProduceSameHashForAllObjectTypes()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("content for all types");

        var objectTypes = new[] { GitObjectType.Blob, GitObjectType.Tree, GitObjectType.Commit, GitObjectType.Tag };

        foreach (var objectType in objectTypes)
        {
            var memoryHash = await store.WriteObjectAsync(objectType, content, CancellationToken.None);
            
            using var stream = new MemoryStream(content);
            var streamHash = await store.WriteObjectAsync(objectType, stream, CancellationToken.None);

            Assert.Equal(memoryHash, streamHash);
        }
    }

    [Fact]
    public async Task ReadObjectAsync_AndReadObjectStreamAsync_ReturnSameContentForLooseObject()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add blob", ("data.txt", "test content"));
        var blobHash = new GitHash(repo.RunGit("rev-parse HEAD:data.txt").Trim());
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectAsync(blobHash);
        
        await using var objectStream = await store.ReadObjectStreamAsync(blobHash);
        using var ms = new MemoryStream();
        await objectStream.Content.CopyToAsync(ms);
        var streamContent = ms.ToArray();

        Assert.Equal(data.Type, objectStream.Type);
        Assert.Equal(data.Content, streamContent);
    }

    [Fact]
    public async Task ReadObjectAsync_AndReadObjectStreamAsync_ReturnSameContentForPackedObject()
    {
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Packed content", ("file.txt", "packed data"));
        repo.RunGit("gc --aggressive --prune=now");
        RemoveLooseObject(repo.GitDirectory, commit);

        var store = new GitObjectStore(repo.GitDirectory);
        var data = await store.ReadObjectAsync(commit);
        
        await using var objectStream = await store.ReadObjectStreamAsync(commit);
        using var ms = new MemoryStream();
        await objectStream.Content.CopyToAsync(ms);
        var streamContent = ms.ToArray();

        Assert.Equal(data.Type, objectStream.Type);
        Assert.Equal(data.Content, streamContent);
    }

    [Fact]
    public async Task ReadObjectAsync_AndReadObjectStreamAsync_ReturnSameContentForCommit()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectAsync(repo.Head);
        
        await using var objectStream = await store.ReadObjectStreamAsync(repo.Head);
        using var ms = new MemoryStream();
        await objectStream.Content.CopyToAsync(ms);
        var streamContent = ms.ToArray();

        Assert.Equal(GitObjectType.Commit, data.Type);
        Assert.Equal(GitObjectType.Commit, objectStream.Type);
        Assert.Equal(data.Content, streamContent);
    }

    [Fact]
    public async Task ReadObjectAsync_AndReadObjectStreamAsync_ReturnSameContentForLargeBlob()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var largeContent = new byte[1024 * 1024];
        new Random(999).NextBytes(largeContent);

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, largeContent, CancellationToken.None);

        var data = await store.ReadObjectAsync(hash);
        
        await using var objectStream = await store.ReadObjectStreamAsync(hash);
        using var ms = new MemoryStream();
        await objectStream.Content.CopyToAsync(ms);
        var streamContent = ms.ToArray();

        Assert.Equal(data.Type, objectStream.Type);
        Assert.Equal(data.Content, streamContent);
        Assert.Equal(largeContent, streamContent);
    }

    [Fact]
    public async Task ReadObjectAsync_AndReadObjectStreamAsync_ReturnSameContentForEmptyBlob()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var emptyContent = Array.Empty<byte>();

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, emptyContent, CancellationToken.None);

        var data = await store.ReadObjectAsync(hash);
        
        await using var objectStream = await store.ReadObjectStreamAsync(hash);
        using var ms = new MemoryStream();
        await objectStream.Content.CopyToAsync(ms);
        var streamContent = ms.ToArray();

        Assert.Equal(GitObjectType.Blob, data.Type);
        Assert.Equal(GitObjectType.Blob, objectStream.Type);
        Assert.Empty(data.Content);
        Assert.Empty(streamContent);
    }

    [Fact]
    public async Task ReadObjectAsync_AndReadObjectStreamAsync_ReturnSameContentForAllObjectTypes()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("content for type comparison");

        var objectTypes = new[] { GitObjectType.Blob, GitObjectType.Tree, GitObjectType.Commit, GitObjectType.Tag };

        foreach (var objectType in objectTypes)
        {
            var hash = await store.WriteObjectAsync(objectType, content, CancellationToken.None);

            var data = await store.ReadObjectAsync(hash);
            
            await using var objectStream = await store.ReadObjectStreamAsync(hash);
            using var ms = new MemoryStream();
            await objectStream.Content.CopyToAsync(ms);
            var streamContent = ms.ToArray();

            Assert.Equal(objectType, data.Type);
            Assert.Equal(objectType, objectStream.Type);
            Assert.Equal(data.Content, streamContent);
        }
    }

    [Fact]
    public async Task ReadObjectStreamAsync_ReturnsCorrectLengthMatchingContent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);
        var content = Encoding.UTF8.GetBytes("content with known length");

        var hash = await store.WriteObjectAsync(GitObjectType.Blob, content, CancellationToken.None);

        await using var objectStream = await store.ReadObjectStreamAsync(hash);
        using var ms = new MemoryStream();
        await objectStream.Content.CopyToAsync(ms);
        var streamContent = ms.ToArray();

        Assert.Equal(content.Length, objectStream.Length);
        Assert.Equal(content.Length, streamContent.Length);
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
