using System.Diagnostics;
using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for basic GitRepository operations: opening, reading commits, reading files, and properties.
/// </summary>
public sealed class GitRepositoryBasicOperationsTests
{
	#region Opening Repositories

	[Fact]
	public void Open_WithGitDirectory_OpensRepository()
	{
		using var repo = GitTestRepository.Create();
		
		var gitRepository = GitRepository.Open(Path.Combine(repo.WorkingDirectory, ".git"));

		Assert.NotNull(gitRepository);
		Assert.Equal(repo.WorkingDirectory, gitRepository.RootPath);
	}

	[Fact]
	public void Open_WithBareRepository_OpensCorrectly()
	{
		var bareRepoPath = Path.Combine(Path.GetTempPath(), $"test-bare-{Guid.NewGuid():N}.git");
		try
		{
			Directory.CreateDirectory(bareRepoPath);
            GitTestHelper.RunGit(bareRepoPath, "init --bare");

			var gitRepository = GitRepository.Open(bareRepoPath);

			Assert.NotNull(gitRepository);
			Assert.Equal(bareRepoPath, gitRepository.RootPath);
			Assert.Equal(bareRepoPath, gitRepository.GitDirectory);
		}
		finally
		{
			if (Directory.Exists(bareRepoPath))
			{
				Directory.Delete(bareRepoPath, recursive: true);
			}
		}
	}

	[Fact]
	public void Open_NonExistentPath_ThrowsDirectoryNotFoundException()
	{
		var nonExistent = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid():N}");

		Assert.Throws<DirectoryNotFoundException>(() =>
			GitRepository.Open(nonExistent));
	}

	#endregion

	#region Getting Commits

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
	public async Task GetCommitAsync_WithBranchName_ReturnsCommit()
	{
		using var repo = GitTestRepository.Create();
		repo.RunGit("branch feature");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var commit = await gitRepository.GetCommitAsync("feature");

		Assert.NotNull(commit);
		Assert.Equal(repo.Head, commit.Id);
	}

	[Fact]
	public async Task GetCommitAsync_WithFullRef_ReturnsCommit()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var branchName = GitTestHelper.GetDefaultBranch(repo);

		var commit = await gitRepository.GetCommitAsync($"refs/heads/{branchName}");

		Assert.NotNull(commit);
		Assert.Equal(repo.Head, commit.Id);
	}

	[Fact]
	public async Task GetCommitAsync_WithTagName_ReturnsCommit()
	{
		using var repo = GitTestRepository.Create();
		repo.RunGit("tag v1.0");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var commit = await gitRepository.GetCommitAsync("v1.0");

		Assert.NotNull(commit);
		Assert.Equal(repo.Head, commit.Id);
	}

	[Fact]
	public async Task GetCommitAsync_WithCommitHash_ReturnsCommit()
	{
		using var repo = GitTestRepository.Create();
		var hash = repo.Head;
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var commit = await gitRepository.GetCommitAsync(hash.Value);

		Assert.Equal(hash, commit.Id);
	}

	[Fact]
	public async Task GetCommitAsync_WithInvalidReference_ThrowsInvalidOperationException()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			gitRepository.GetCommitAsync("non-existent-ref"));
	}

	#endregion

	#region Reading Files

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
	public async Task ReadFileAsync_WithBackslashPath_NormalizesCorrectly()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("dir/file.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var content = await gitRepository.ReadFileAsync("dir\\file.txt");

		Assert.Equal("content", Encoding.UTF8.GetString(content));
	}

	[Fact]
	public async Task ReadFileAsync_WithTrailingSlash_ThrowsArgumentException()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("dir/file.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await Assert.ThrowsAsync<ArgumentException>(() =>
			gitRepository.ReadFileAsync("/"));
	}

	[Fact]
	public async Task ReadFileAsync_NonExistent_ThrowsFileNotFoundException()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await Assert.ThrowsAsync<FileNotFoundException>(() =>
			gitRepository.ReadFileAsync("non-existent.txt"));
	}

	[Fact]
	public async Task ReadFileAsync_WithSpecificCommit_ReadsFromThatCommit()
	{
		using var repo = GitTestRepository.Create();
		var commit1 = repo.Commit("Version 1", ("file.txt", "version 1"));
		var commit2 = repo.Commit("Version 2", ("file.txt", "version 2"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var content1 = await gitRepository.ReadFileAsync("file.txt", commit1.Value);
		var content2 = await gitRepository.ReadFileAsync("file.txt", commit2.Value);

		Assert.Equal("version 1", Encoding.UTF8.GetString(content1));
		Assert.Equal("version 2", Encoding.UTF8.GetString(content2));
	}

	#endregion

	#region Properties

	[Fact]
	public void RootPath_ReturnsAbsolutePath()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		Assert.True(Path.IsPathFullyQualified(gitRepository.RootPath));
	}

	[Fact]
	public void GitDirectory_ReturnsAbsolutePath()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		Assert.True(Path.IsPathFullyQualified(gitRepository.GitDirectory));
	}

	[Fact]
	public void HashLengthBytes_ReturnsValidLength()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		Assert.True(gitRepository.HashLengthBytes == 20 || gitRepository.HashLengthBytes == 32);
	}

	[Fact]
	public void HashLengthBytes_ForSha256Repo_Returns32()
	{
		using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		Assert.Equal(32, gitRepository.HashLengthBytes);
	}

	#endregion

	#region Caching

	[Fact]
	public async Task InvalidateCaches_RefreshesBranchReferences()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var initialCommit = await gitRepository.GetCommitAsync(headRef);
		var updatedCommit = repo.Commit("Update after caching", ("cache.txt", Guid.NewGuid().ToString("N")));

		var staleCommit = await gitRepository.GetCommitAsync(headRef);
		Assert.Equal(initialCommit.Id, staleCommit.Id);

		gitRepository.InvalidateCaches();

		var refreshedCommit = await gitRepository.GetCommitAsync(headRef);
		Assert.Equal(updatedCommit, refreshedCommit.Id);
	}

	[Fact]
	public async Task InvalidateCaches_WithClearAllData_ClearsCommitCache()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		
		var commit1 = await gitRepository.GetCommitAsync();
		
		gitRepository.InvalidateCaches(clearAllData: true);
		
		var commit2 = await gitRepository.GetCommitAsync();
		
		Assert.Equal(commit1.Id, commit2.Id);
	}

	[Fact]
	public async Task Multiple_GetCommitAsync_UsesCaching()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		
		var sw = Stopwatch.StartNew();
		var commit1 = await gitRepository.GetCommitAsync();
		var time1 = sw.ElapsedMilliseconds;
		
		sw.Restart();
		var commit2 = await gitRepository.GetCommitAsync();
		var time2 = sw.ElapsedMilliseconds;
		
		Assert.Equal(commit1.Id, commit2.Id);
	}

	#endregion

	#region SHA-256 Support

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

	#endregion

	#region Cancellation Token Support

	[Fact]
	public async Task GetCommitAsync_RespectsCancellationToken()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			gitRepository.GetCommitAsync(cancellationToken: cts.Token));
	}

	#endregion
}
