using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Test.Infrastructure;
using Xunit;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitObjectStoreTests
{
	[Fact]
	public async Task ReadObjectAsync_ReturnsLooseCommitData()
	{
		using var repo = GitTestRepository.Create();
		var store = new GitObjectStore(repo.GitDirectory);

		var data = await store.ReadObjectAsync(repo.Head);
        var data2 = await store.ReadObjectAsync(repo.Head);

        Assert.Equal(GitObjectType.Commit, data.Type);
		var content = Encoding.UTF8.GetString(data.Content);
		Assert.Contains("Initial commit", content);
        Assert.Same(data.Content, data2.Content);
	}

	[Fact]
	public async Task ReadObjectAsync_ReturnsBlobContent()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add blob", ("src/data.txt", "payload"));
		var blobHash = new GitHash(repo.RunGit("rev-parse HEAD:src/data.txt").Trim());
		var store = new GitObjectStore(repo.GitDirectory);

		var data = await store.ReadObjectAsync(blobHash);
        var data2 = await store.ReadObjectAsync(blobHash);

        Assert.Equal(GitObjectType.Blob, data.Type);
		Assert.Equal("payload", Encoding.UTF8.GetString(data.Content));
        Assert.Same(data.Content, data2.Content);
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
        var data2 = await store.ReadObjectAsync(commit);

        Assert.Equal(GitObjectType.Commit, data.Type);
		Assert.Contains("Pack me", Encoding.UTF8.GetString(data.Content));
        Assert.Same(data.Content, data2.Content);
    }

	[Fact]
	public async Task ReadObjectAsync_SupportsSha256Repositories()
	{
		using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
		var store = new GitObjectStore(repo.GitDirectory);

		var data = await store.ReadObjectAsync(repo.Head);
        var data2 = await store.ReadObjectAsync(repo.Head);

        Assert.Equal(GitObjectType.Commit, data.Type);
		Assert.Contains("Initial commit", Encoding.UTF8.GetString(data.Content));
        Assert.Same(data.Content, data2.Content);
    }

    [Fact]
    public async Task ReadObjectNoCacheAsync_ReturnsLooseCommitData()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectNoCacheAsync(repo.Head);

        Assert.Equal(GitObjectType.Commit, data.Type);
        var content = Encoding.UTF8.GetString(data.Content);
        Assert.Contains("Initial commit", content);
    }

    [Fact]
    public async Task ReadObjectNoCacheAsync_ReturnsBlobContent()
    {
        using var repo = GitTestRepository.Create();
        repo.Commit("Add blob", ("src/data.txt", "payload"));
        var blobHash = new GitHash(repo.RunGit("rev-parse HEAD:src/data.txt").Trim());
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectNoCacheAsync(blobHash);

        Assert.Equal(GitObjectType.Blob, data.Type);
        Assert.Equal("payload", Encoding.UTF8.GetString(data.Content));
    }

    [Fact]
    public async Task ReadObjectNoCacheAsync_LoadsDataFromPackFile()
    {
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Pack me", ("file.txt", "content"));
        repo.RunGit("gc --aggressive --prune=now");
        RemoveLooseObject(repo.GitDirectory, commit);

        var store = new GitObjectStore(repo.GitDirectory);
        var data = await store.ReadObjectNoCacheAsync(commit);

        Assert.Equal(GitObjectType.Commit, data.Type);
        Assert.Contains("Pack me", Encoding.UTF8.GetString(data.Content));
    }

    [Fact]
    public async Task ReadObjectNoCacheAsync_SupportsSha256Repositories()
    {
        using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
        var store = new GitObjectStore(repo.GitDirectory);

        var data = await store.ReadObjectNoCacheAsync(repo.Head);

        Assert.Equal(GitObjectType.Commit, data.Type);
        Assert.Contains("Initial commit", Encoding.UTF8.GetString(data.Content));
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
