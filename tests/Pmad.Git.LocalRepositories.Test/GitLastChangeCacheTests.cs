using System.Text.Json;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitLastChangeCacheTests : IDisposable
{
    private readonly string _gitDirectory;
    private readonly GitLastChangeCache _cache;

    public GitLastChangeCacheTests()
    {
        _gitDirectory = Path.Combine(Path.GetTempPath(), "PmadGitCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gitDirectory);
        _cache = new GitLastChangeCache(_gitDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_gitDirectory, recursive: true); } catch { }
    }

    private static GitCommit MakeCommit(string hashHex)
    {
        var id = new GitHash(hashHex);
        var tree = new GitHash("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        return new GitCommit(id, tree, [], new Dictionary<string, string>(), string.Empty);
    }

    private static Task<GitCommit> GetCommitStub(GitHash hash)
    {
        return Task.FromResult(MakeCommit(hash.Value));
    }

    // --------------- TryReadAsync ---------------

    [Fact]
    public async Task TryReadAsync_CacheMiss_ReturnsNull()
    {
        var hash = new GitHash("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var result = await _cache.TryReadAsync(hash, GetCommitStub, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadAsync_AfterWrite_ReturnsEntries()
    {
        var commitHash = new GitHash("1111111111111111111111111111111111111111");
        var commit1 = MakeCommit("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var commit2 = MakeCommit("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var entries = new List<GitFileLastChange>
        {
            new("src/Program.cs", commit1),
            new("README.md",      commit2),
        };

        await _cache.WriteAsync(commitHash, entries, CancellationToken.None);
        var result = await _cache.TryReadAsync(commitHash, GetCommitStub, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Contains(result, e => e.Path == "src/Program.cs" && e.Commit.Id == commit1.Id);
        Assert.Contains(result, e => e.Path == "README.md" && e.Commit.Id == commit2.Id);
    }

    [Fact]
    public async Task TryReadAsync_UsesGetCommitCallback()
    {
        var commitHash = new GitHash("2222222222222222222222222222222222222222");
        var storedCommitId = new GitHash("cccccccccccccccccccccccccccccccccccccccc");
        var resolvedCommit = MakeCommit(storedCommitId.Value);
        var entries = new List<GitFileLastChange> { new("file.txt", resolvedCommit) };

        await _cache.WriteAsync(commitHash, entries, CancellationToken.None);

        GitHash? capturedHash = null;
        var result = await _cache.TryReadAsync(commitHash, hash =>
        {
            capturedHash = hash;
            return Task.FromResult(resolvedCommit);
        }, CancellationToken.None);

        Assert.Equal(storedCommitId, capturedHash);
        Assert.NotNull(result);
        Assert.Same(resolvedCommit, result![0].Commit);
    }

    [Fact]
    public async Task TryReadAsync_CorruptFile_ReturnsNull()
    {
        var hash = new GitHash("3333333333333333333333333333333333333333");
        var cacheDir = Path.Combine(_gitDirectory, "pmad-cache", "last-change", "33");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "33333333333333333333333333333333333333.json"), "not-valid-json");

        var result = await _cache.TryReadAsync(hash, GetCommitStub, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadAsync_WrongVersion_ReturnsNull()
    {
        var hash = new GitHash("4444444444444444444444444444444444444444");
        var cacheDir = Path.Combine(_gitDirectory, "pmad-cache", "last-change", "44");
        Directory.CreateDirectory(cacheDir);
        var json = JsonSerializer.Serialize(new { v = 99, f = new[] { new { p = "file.txt", c = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } } });
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "44444444444444444444444444444444444444.json"), json);

        var result = await _cache.TryReadAsync(hash, GetCommitStub, CancellationToken.None);

        Assert.Null(result);
    }

    // --------------- TryReadRawAsync ---------------

    [Fact]
    public async Task TryReadRawAsync_CacheMiss_ReturnsNull()
    {
        var hash = new GitHash("5555555555555555555555555555555555555555");

        var result = await _cache.TryReadRawAsync(hash, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadRawAsync_AfterWrite_ReturnsPathToCommitMapping()
    {
        var commitHash = new GitHash("6666666666666666666666666666666666666666");
        var commit1 = MakeCommit("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var commit2 = MakeCommit("cccccccccccccccccccccccccccccccccccccccc");
        var entries = new List<GitFileLastChange>
        {
            new("src/Program.cs", commit1),
            new("README.md",      commit2),
        };

        await _cache.WriteAsync(commitHash, entries, CancellationToken.None);
        var result = await _cache.TryReadRawAsync(commitHash, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(commit1.Id.Value, result!["src/Program.cs"]);
        Assert.Equal(commit2.Id.Value, result["README.md"]);
    }

    [Fact]
    public async Task TryReadRawAsync_CorruptFile_ReturnsNull()
    {
        var hash = new GitHash("7777777777777777777777777777777777777777");
        var cacheDir = Path.Combine(_gitDirectory, "pmad-cache", "last-change", "77");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "77777777777777777777777777777777777777.json"), "{{{{");

        var result = await _cache.TryReadRawAsync(hash, CancellationToken.None);

        Assert.Null(result);
    }

    // --------------- WriteAsync ---------------

    [Fact]
    public async Task WriteAsync_CreatesExpectedCacheFile()
    {
        var commitHash = new GitHash("8888888888888888888888888888888888888888");
        var commit = MakeCommit("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var entries = new List<GitFileLastChange> { new("file.cs", commit) };

        await _cache.WriteAsync(commitHash, entries, CancellationToken.None);

        // Cache files are sharded by the first two hex chars of the commit hash.
        var shardDir = Path.Combine(_gitDirectory, "pmad-cache", "last-change", "88");
        var files = Directory.GetFiles(shardDir, "*.json");
        Assert.Single(files);
    }

    [Fact]
    public async Task WriteAsync_EmptyEntries_CanBeReadBack()
    {
        var commitHash = new GitHash("9999999999999999999999999999999999999999");

        await _cache.WriteAsync(commitHash, [], CancellationToken.None);
        var result = await _cache.TryReadAsync(commitHash, GetCommitStub, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public async Task WriteAsync_SecondWrite_OverwritesFirstWrite()
    {
        var commitHash = new GitHash("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var commit1 = MakeCommit("1111111111111111111111111111111111111111");
        var commit2 = MakeCommit("2222222222222222222222222222222222222222");

        await _cache.WriteAsync(commitHash, [new("file.cs", commit1)], CancellationToken.None);
        await _cache.WriteAsync(commitHash, [new("file.cs", commit2), new("other.cs", commit2)], CancellationToken.None);

        var result = await _cache.TryReadAsync(commitHash, GetCommitStub, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.All(result, e => Assert.Equal(commit2.Id, e.Commit.Id));
    }

    [Fact]
    public async Task WriteAsync_DifferentCommitHashes_StoreIndependently()
    {
        var hash1 = new GitHash("aaaa111111111111111111111111111111111111");
        var hash2 = new GitHash("bbbb222222222222222222222222222222222222");
        var commit = MakeCommit("cccccccccccccccccccccccccccccccccccccccc");

        await _cache.WriteAsync(hash1, [new("a.cs", commit)], CancellationToken.None);
        await _cache.WriteAsync(hash2, [new("b.cs", commit)], CancellationToken.None);

        var result1 = await _cache.TryReadAsync(hash1, GetCommitStub, CancellationToken.None);
        var result2 = await _cache.TryReadAsync(hash2, GetCommitStub, CancellationToken.None);

        Assert.Single(result1!);
        Assert.Equal("a.cs", result1![0].Path);
        Assert.Single(result2!);
        Assert.Equal("b.cs", result2![0].Path);
    }
}
