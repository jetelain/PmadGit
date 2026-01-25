using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for CreateCommitAsync path conflict validation to prevent overwriting directories with files.
/// </summary>
public sealed class GitRepositoryPathConflictTests
{
	#region Add Operation - Path Conflicts

	[Fact]
	public async Task CreateCommitAsync_AddFile_WhenParentPathIsFile_Throws()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed file", ("src", "file content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new AddFileOperation("src/app.txt", Encoding.UTF8.GetBytes("app content"))
				},
				CreateMetadata("Try to add file under existing file")));

		Assert.Contains("src", exception.Message);
		Assert.Contains("file, not a directory", exception.Message);
	}

	[Fact]
	public async Task CreateCommitAsync_AddFile_WhenPathWouldBecomeDirectory_Throws()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed file", ("src/lib/utils.cs", "utils"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new AddFileOperation("src/lib", Encoding.UTF8.GetBytes("lib content"))
				},
				CreateMetadata("Try to add file where directory exists")));

		Assert.Contains("src/lib", exception.Message);
		Assert.Contains("src/lib/utils.cs", exception.Message);
		Assert.Contains("exists under it", exception.Message);
	}

	[Fact]
	public async Task CreateCommitAsync_AddFile_WhenMultipleLevelsOfFilesExist_Throws()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("a/b/c/d.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new AddFileOperation("a/b", Encoding.UTF8.GetBytes("b content"))
				},
				CreateMetadata("Try to replace directory with file")));

		Assert.Contains("a/b", exception.Message);
	}

	[Fact]
	public async Task CreateCommitAsync_AddFile_WhenImmediateParentIsFile_Throws()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed file", ("docs/readme.md", "readme"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new AddFileOperation("docs/readme.md/nested.txt", Encoding.UTF8.GetBytes("nested"))
				},
				CreateMetadata("Try to nest file under file")));

		Assert.Contains("docs/readme.md", exception.Message);
		Assert.Contains("file, not a directory", exception.Message);
	}

	[Fact]
	public async Task CreateCommitAsync_AddFile_ValidPathWithExistingDirectory_Succeeds()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed file", ("src/utils.cs", "utils"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new AddFileOperation("src/app.cs", Encoding.UTF8.GetBytes("app content"))
			},
			CreateMetadata("Add sibling file"));

		var content = await gitRepository.ReadFileAsync("src/app.cs", commitHash.Value);
		Assert.Equal("app content", Encoding.UTF8.GetString(content));

		var utilsContent = await gitRepository.ReadFileAsync("src/utils.cs", commitHash.Value);
		Assert.Equal("utils", Encoding.UTF8.GetString(utilsContent));
	}

	#endregion

	#region Update Operation - Path Conflicts

	[Fact]
	public async Task CreateCommitAsync_UpdateFile_DoesNotThrowOnValidPath()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("src/app.txt", "v1"), ("src/lib/helper.cs", "helper"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("src/app.txt", Encoding.UTF8.GetBytes("v2"))
			},
			CreateMetadata("Update file"));

		var content = await gitRepository.ReadFileAsync("src/app.txt", commitHash.Value);
		Assert.Equal("v2", Encoding.UTF8.GetString(content));
	}

	#endregion

	#region Move Operation - Path Conflicts

	[Fact]
	public async Task CreateCommitAsync_MoveFile_WhenDestinationParentIsFile_Throws()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("file.txt", "file"), ("dest", "dest file"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new MoveFileOperation("file.txt", "dest/file.txt")
				},
				CreateMetadata("Move file to invalid destination")));

		Assert.Contains("dest", exception.Message);
		Assert.Contains("file, not a directory", exception.Message);
	}

	[Fact]
	public async Task CreateCommitAsync_MoveFile_WhenDestinationWouldBecomeDirectory_Throws()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("file.txt", "file"), ("dest/nested.txt", "nested"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new MoveFileOperation("file.txt", "dest")
				},
				CreateMetadata("Move file to path with children")));

		Assert.Contains("dest", exception.Message);
		Assert.Contains("dest/nested.txt", exception.Message);
	}

	[Fact]
	public async Task CreateCommitAsync_MoveFile_ValidDestination_Succeeds()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("old/file.txt", "content"), ("new/other.txt", "other"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new MoveFileOperation("old/file.txt", "new/file.txt")
			},
			CreateMetadata("Move file to valid destination"));

		var content = await gitRepository.ReadFileAsync("new/file.txt", commitHash.Value);
		Assert.Equal("content", Encoding.UTF8.GetString(content));

		await Assert.ThrowsAsync<FileNotFoundException>(
			() => gitRepository.ReadFileAsync("old/file.txt", commitHash.Value));
	}

	#endregion

	#region Initial Commit - Path Conflicts

	[Fact]
	public async Task CreateCommitAsync_InitialCommit_WithConflictingPaths_Throws()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"conflict-initial-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);

			var exception = await Assert.ThrowsAsync<InvalidOperationException>(
				() => repository.CreateCommitAsync(
					"main",
					new GitCommitOperation[]
					{
						new AddFileOperation("src", Encoding.UTF8.GetBytes("src file")),
						new AddFileOperation("src/app.txt", Encoding.UTF8.GetBytes("app"))
					},
					CreateMetadata("Initial commit with conflict")));

			Assert.Contains("src", exception.Message);
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	[Fact]
	public async Task CreateCommitAsync_InitialCommit_WithReverseConflictingPaths_Throws()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"conflict-reverse-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);

			var exception = await Assert.ThrowsAsync<InvalidOperationException>(
				() => repository.CreateCommitAsync(
					"main",
					new GitCommitOperation[]
					{
						new AddFileOperation("src/app.txt", Encoding.UTF8.GetBytes("app")),
						new AddFileOperation("src", Encoding.UTF8.GetBytes("src file"))
					},
					CreateMetadata("Initial commit with reverse conflict")));

			Assert.Contains("src", exception.Message);
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	[Fact]
	public async Task CreateCommitAsync_InitialCommit_WithDeepNesting_Succeeds()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"deep-nesting-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);

			var commitHash = await repository.CreateCommitAsync(
				"main",
				new GitCommitOperation[]
				{
					new AddFileOperation("a/b/c/d/e/file.txt", Encoding.UTF8.GetBytes("deep")),
					new AddFileOperation("a/b/other.txt", Encoding.UTF8.GetBytes("other"))
				},
				CreateMetadata("Deep nesting"));

			var deepContent = await repository.ReadFileAsync("a/b/c/d/e/file.txt", commitHash.Value);
			Assert.Equal("deep", Encoding.UTF8.GetString(deepContent));

			var otherContent = await repository.ReadFileAsync("a/b/other.txt", commitHash.Value);
			Assert.Equal("other", Encoding.UTF8.GetString(otherContent));
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	#endregion

	#region Edge Cases

	[Fact]
	public async Task CreateCommitAsync_AddFile_AtRootLevel_DoesNotConflict()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed file", ("README.md", "readme"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new AddFileOperation("LICENSE", Encoding.UTF8.GetBytes("license"))
			},
			CreateMetadata("Add root level file"));

		var content = await gitRepository.ReadFileAsync("LICENSE", commitHash.Value);
		Assert.Equal("license", Encoding.UTF8.GetString(content));
	}

	[Fact]
	public async Task CreateCommitAsync_RemoveAndAddInSamePath_Succeeds()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("path/to/file.txt", "old"), ("path/to/dir/nested.txt", "nested"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new RemoveFileOperation("path/to/dir/nested.txt"),
				new AddFileOperation("path/to/dir", Encoding.UTF8.GetBytes("now a file"))
			},
			CreateMetadata("Remove directory contents and replace with file"));

		var content = await gitRepository.ReadFileAsync("path/to/dir", commitHash.Value);
		Assert.Equal("now a file", Encoding.UTF8.GetString(content));

		await Assert.ThrowsAsync<FileNotFoundException>(
			() => gitRepository.ReadFileAsync("path/to/dir/nested.txt", commitHash.Value));
	}

	[Fact]
	public async Task CreateCommitAsync_PathConflict_WithComplexScenario_Throws()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", 
			("src/lib/core/module.cs", "module"),
			("src/lib/utils.cs", "utils"),
			("src/app.cs", "app"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new AddFileOperation("src/lib/core", Encoding.UTF8.GetBytes("core file"))
				},
				CreateMetadata("Try to overwrite directory")));

		Assert.Contains("src/lib/core", exception.Message);
		Assert.Contains("module.cs", exception.Message);
	}

	#endregion

	#region Helper Methods

	private static GitCommitMetadata CreateMetadata(string message)
		=> new(
			message,
			new GitCommitSignature("Test User",
			"test@example.com",
			new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero)));

	#endregion
}
