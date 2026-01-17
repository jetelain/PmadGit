using System;
using System.Collections.Generic;
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

	[Fact]
	public async Task CreateCommitAsync_AddsFileAndUpdatesBranch()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GetHeadReference(repo);
		var metadata = CreateMetadata("Add file through API");

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new AddFileOperation("src/api.txt", Encoding.UTF8.GetBytes("api payload"))
			},
			metadata);

		var headValue = repo.RunGit("rev-parse HEAD").Trim();
		Assert.Equal(headValue, commitHash.Value);

		var commit = await gitRepository.GetCommitAsync(commitHash.Value);
		Assert.Equal(metadata.Message, commit.Message);
		var content = await gitRepository.ReadFileAsync("src/api.txt", commitHash.Value);
		Assert.Equal("api payload", Encoding.UTF8.GetString(content));
	}

	[Fact]
	public async Task CreateCommitAsync_SupportsUpdateAndRemoveOperations()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("src/app.txt", "v1"), ("docs/old.md", "legacy"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GetHeadReference(repo);
		var metadata = CreateMetadata("Update and cleanup");

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("src/app.txt", Encoding.UTF8.GetBytes("v2")),
				new RemoveFileOperation("docs/old.md"),
				new AddFileOperation("src/new.txt", Encoding.UTF8.GetBytes("new"))
			},
			metadata);

		var updatedApp = await gitRepository.ReadFileAsync("src/app.txt", commitHash.Value);
		Assert.Equal("v2", Encoding.UTF8.GetString(updatedApp));

		var newFile = await gitRepository.ReadFileAsync("src/new.txt", commitHash.Value);
		Assert.Equal("new", Encoding.UTF8.GetString(newFile));

		await Assert.ThrowsAsync<FileNotFoundException>(() => gitRepository.ReadFileAsync("docs/old.md", commitHash.Value));
	}

	[Fact]
	public async Task CreateCommitAsync_CanMoveFiles()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed file", ("docs/readme.txt", "hello"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GetHeadReference(repo);
		var metadata = CreateMetadata("Move file");

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new MoveFileOperation("docs/readme.txt", "src/docs/readme.txt")
			},
			metadata);

		var movedContent = await gitRepository.ReadFileAsync("src/docs/readme.txt", commitHash.Value);
		Assert.Equal("hello", Encoding.UTF8.GetString(movedContent));
		await Assert.ThrowsAsync<FileNotFoundException>(() => gitRepository.ReadFileAsync("docs/readme.txt", commitHash.Value));

		var cliListing = repo.RunGit("ls-tree -r --name-only HEAD");
		Assert.Contains("src/docs/readme.txt", cliListing.Split('\n', StringSplitOptions.RemoveEmptyEntries));
	}

	[Fact]
	public async Task CreateCommitAsync_ReflectsChangesForGitCli()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GetHeadReference(repo);
		var metadata = CreateMetadata("CLI verification");

		await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new AddFileOperation("cli/sample.txt", Encoding.UTF8.GetBytes("cli payload"))
			},
			metadata);

		var cliContent = repo.RunGit("show HEAD:cli/sample.txt").Trim();
		Assert.Equal("cli payload", cliContent);

		var gitAuthor = repo.RunGit("log -1 --pretty=%an").Trim();
		Assert.Equal(metadata.AuthorName, gitAuthor);
	}

	[Fact]
	public async Task CreateCommitAsync_RemoveMissingFileThrows()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GetHeadReference(repo);

		await Assert.ThrowsAsync<FileNotFoundException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new RemoveFileOperation("missing.txt")
				},
				CreateMetadata("Remove missing")));
	}

	[Fact]
	public async Task CreateCommitAsync_NoEffectiveChangesThrows()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed", ("data.txt", "same"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GetHeadReference(repo);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("data.txt", Encoding.UTF8.GetBytes("same"))
				},
				CreateMetadata("No change")));
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

	private static GitCommitMetadata CreateMetadata(string message)
		=> new(
			message,
			new GitCommitSignature("Api User",
			"api@example.com",
			new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero)));
}

