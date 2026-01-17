using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Test.Infrastructure;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitRepositoryTests
{
	[Fact]
	public async Task GetCommitAsync_ReturnsHeadCommit()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var headCommit = await gitRepository.GetCommitAsync();

		Assert.Equal(repo.Head, headCommit.Id);
		Assert.Contains("Initial commit", headCommit.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReadFileAsync_ReturnsContentFromHead()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add async file", ("src/module/async.txt", "async hello"));

		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var content = await gitRepository.ReadFileAsync("src/module/async.txt");

		Assert.Equal("async hello", Encoding.UTF8.GetString(content));
	}

	[Fact]
	public async Task GetFileHistoryAsync_ReturnsCommitsThatModifyFile()
	{
		using var repo = GitTestRepository.Create();
		var addCommit = repo.Commit("Add tracked file", ("app.txt", "v1"));
		var updateCommit = repo.Commit("Update tracked file", ("app.txt", "v2"));

		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var history = new List<GitCommit>();
		await foreach (var commit in gitRepository.GetFileHistoryAsync("app.txt"))
		{
			history.Add(commit);
		}

		Assert.Collection(
			history,
			commit => Assert.Equal(updateCommit, commit.Id),
			commit => Assert.Equal(addCommit, commit.Id));
	}

	[Fact]
	public async Task EnumerateCommitsAsync_ReturnsCommitsFromHead()
	{
		using var repo = GitTestRepository.Create();
		var initialHead = repo.Head;
		var second = repo.Commit("Add file", ("file.txt", "v1"));
		var third = repo.Commit("Update file", ("file.txt", "v2"));

		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var commits = new List<GitCommit>();
		await foreach (var commit in gitRepository.EnumerateCommitsAsync())
		{
			commits.Add(commit);
		}

		Assert.Collection(
			commits,
			commit => Assert.Equal(third, commit.Id),
			commit => Assert.Equal(second, commit.Id),
			commit => Assert.Equal(initialHead, commit.Id));
	}

	[Fact]
	public async Task EnumerateCommitTreeAsync_WithPathEnumeratesChildren()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit(
			"Add tree content",
			("src/Program.cs", "Console.WriteLine(\"Hi\");"),
			("src/Lib/Class1.cs", "class Class1 {}"));

		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var entries = new List<GitTreeItem>();
		await foreach (var item in gitRepository.EnumerateCommitTreeAsync(path: "src"))
		{
			entries.Add(item);
		}

		var paths = entries.Select(item => item.Path).ToList();
		Assert.Contains("src/Program.cs", paths);
		Assert.Contains("src/Lib/Class1.cs", paths);
		var libEntry = entries.First(e => e.Path == "src/Lib/Class1.cs");
		Assert.Equal(GitTreeEntryKind.Blob, libEntry.Entry.Kind);
	}

	[Fact]
	public async Task Sha256Repository_IsReadableAsync()
	{
		using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
		var targetCommit = repo.Commit("Add sha256 file", ("sha/file.txt", "sha256 payload"));

		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var commit = await gitRepository.GetCommitAsync(targetCommit.Value);
		var fileContent = await gitRepository.ReadFileAsync("sha/file.txt", targetCommit.Value);

		Assert.Equal(targetCommit, commit.Id);
		Assert.Equal("sha256 payload", Encoding.UTF8.GetString(fileContent));
	}

	[Fact]
	public async Task InvalidateCaches_RefreshesBranchReferences()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GetHeadReference(repo);

		var initialCommit = await gitRepository.GetCommitAsync(headRef);
		var updatedCommit = repo.Commit("Update after caching", ("cache.txt", Guid.NewGuid().ToString("N")));

		var staleCommit = await gitRepository.GetCommitAsync(headRef);
		Assert.Equal(initialCommit.Id, staleCommit.Id);

		gitRepository.InvalidateCaches();

		var refreshedCommit = await gitRepository.GetCommitAsync(headRef);
		Assert.Equal(updatedCommit, refreshedCommit.Id);
	}

	private static string GetHeadReference(GitTestRepository repo)
	{
		var headPath = Path.Combine(repo.GitDirectory, "HEAD");
		var content = File.ReadAllText(headPath).Trim();
		if (!content.StartsWith("ref: ", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("HEAD is not pointing to a symbolic reference");
		}

		return content[5..].Trim();
	}
}

