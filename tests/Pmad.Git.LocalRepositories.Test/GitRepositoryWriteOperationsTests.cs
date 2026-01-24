using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitRepository write operations: creating commits, writing objects, and managing references.
/// </summary>
public sealed class GitRepositoryWriteOperationsTests
{
	#region CreateCommitAsync - Initial Commit

	[Fact]
	public async Task CreateCommitAsync_CanCreateInitialCommit()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"initial-commit-test-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);
			var metadata = CreateMetadata("Initial commit");

			var commitHash = await repository.CreateCommitAsync(
				"main",
				new GitCommitOperation[]
				{
					new AddFileOperation("README.md", Encoding.UTF8.GetBytes("# My Project")),
					new AddFileOperation("src/app.txt", Encoding.UTF8.GetBytes("app content"))
				},
				metadata);

			var commit = await repository.GetCommitAsync(commitHash.Value);
			Assert.Equal("Initial commit", commit.Message.Trim());
			Assert.Empty(commit.Parents);

			var readmeContent = await repository.ReadFileAsync("README.md", commitHash.Value);
			Assert.Equal("# My Project", Encoding.UTF8.GetString(readmeContent));

			var appContent = await repository.ReadFileAsync("src/app.txt", commitHash.Value);
			Assert.Equal("app content", Encoding.UTF8.GetString(appContent));

			var refs = await repository.GetReferencesAsync();
			Assert.Contains(refs, r => r.Key == "refs/heads/main");
			Assert.Equal(commitHash, refs["refs/heads/main"]);
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	[Fact]
	public async Task CreateCommitAsync_InitialCommitVisibleToGitCli()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"initial-cli-test-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);
			var metadata = CreateMetadata("First commit");

			var commitHash = await repository.CreateCommitAsync(
				"main",
				new GitCommitOperation[]
				{
					new AddFileOperation("file.txt", Encoding.UTF8.GetBytes("content"))
				},
				metadata);

			var gitLog = GitTestHelper.RunGit(repoPath, "log --oneline");
			Assert.Contains("First commit", gitLog);

			var gitShow = GitTestHelper.RunGit(repoPath, "show HEAD:file.txt");
			Assert.Equal("content", gitShow.Trim());

			var gitRevParse = GitTestHelper.RunGit(repoPath, "rev-parse HEAD").Trim();
			Assert.Equal(commitHash.Value, gitRevParse);

			var gitParents = GitTestHelper.RunGit(repoPath, "rev-list --parents -1 HEAD").Trim();
			var parts = gitParents.Split(' ');
			Assert.Single(parts);
			Assert.Equal(commitHash.Value, parts[0]);
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	[Fact]
	public async Task CreateCommitAsync_InitialCommitOnCustomBranch()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"initial-custom-branch-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath, initialBranch: "develop");
			var metadata = CreateMetadata("Initial on develop");

			var commitHash = await repository.CreateCommitAsync(
				"develop",
				new GitCommitOperation[]
				{
					new AddFileOperation("dev.txt", Encoding.UTF8.GetBytes("development"))
				},
				metadata);

			var refs = await repository.GetReferencesAsync();
			Assert.Contains(refs, r => r.Key == "refs/heads/develop");
			Assert.Equal(commitHash, refs["refs/heads/develop"]);

			var commit = await repository.GetCommitAsync("develop");
			Assert.Empty(commit.Parents);
			Assert.Equal("Initial on develop", commit.Message.Trim());
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	[Fact]
	public async Task CreateCommitAsync_InitialCommitThenSecondCommit()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"initial-then-second-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);

			var firstCommit = await repository.CreateCommitAsync(
				"main",
				new GitCommitOperation[] { new AddFileOperation("file1.txt", Encoding.UTF8.GetBytes("first")) },
				CreateMetadata("First commit"));

			repository.InvalidateCaches();

			var secondCommit = await repository.CreateCommitAsync(
				"main",
				new GitCommitOperation[] { new AddFileOperation("file2.txt", Encoding.UTF8.GetBytes("second")) },
				CreateMetadata("Second commit"));

			var commit2 = await repository.GetCommitAsync(secondCommit.Value);
			Assert.Single(commit2.Parents);
			Assert.Equal(firstCommit, commit2.Parents[0]);

			var commit1 = await repository.GetCommitAsync(firstCommit.Value);
			Assert.Empty(commit1.Parents);

			var file1Content = await repository.ReadFileAsync("file1.txt", secondCommit.Value);
			Assert.Equal("first", Encoding.UTF8.GetString(file1Content));

			var file2Content = await repository.ReadFileAsync("file2.txt", secondCommit.Value);
			Assert.Equal("second", Encoding.UTF8.GetString(file2Content));
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	[Fact]
	public async Task CreateCommitAsync_InitialCommitWithEmptyOperationsThrows()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"initial-empty-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);

			await Assert.ThrowsAsync<InvalidOperationException>(
				() => repository.CreateCommitAsync(
					"main",
					Array.Empty<GitCommitOperation>(),
					CreateMetadata("Empty commit")));
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	[Fact]
	public async Task CreateCommitAsync_InitialCommitWithMultipleFiles()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"initial-multi-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);
			var metadata = CreateMetadata("Setup project");

			var commitHash = await repository.CreateCommitAsync(
				"main",
				new GitCommitOperation[]
				{
					new AddFileOperation("README.md", Encoding.UTF8.GetBytes("# Project")),
					new AddFileOperation("src/main.cs", Encoding.UTF8.GetBytes("// Main")),
					new AddFileOperation("src/utils.cs", Encoding.UTF8.GetBytes("// Utils")),
					new AddFileOperation("docs/guide.md", Encoding.UTF8.GetBytes("# Guide")),
					new AddFileOperation(".gitignore", Encoding.UTF8.GetBytes("*.log"))
				},
				metadata);

			var commit = await repository.GetCommitAsync(commitHash.Value);
			Assert.Empty(commit.Parents);

			var items = new List<GitTreeItem>();
			await foreach (var item in repository.EnumerateCommitTreeAsync(commitHash.Value))
			{
				items.Add(item);
			}

			Assert.Equal(5, items.Count(i => i.Entry.Kind == GitTreeEntryKind.Blob));
			Assert.Contains(items, i => i.Path == "README.md");
			Assert.Contains(items, i => i.Path == "src/main.cs");
			Assert.Contains(items, i => i.Path == "src/utils.cs");
			Assert.Contains(items, i => i.Path == "docs/guide.md");
			Assert.Contains(items, i => i.Path == ".gitignore");
		}
		finally
		{
			GitTestHelper.TryDeleteDirectory(repoPath);
		}
	}

	#endregion

	#region CreateCommitAsync - Add Operations

	[Fact]
	public async Task CreateCommitAsync_AddsFileAndUpdatesBranch()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);
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
	public async Task CreateCommitAsync_ReflectsChangesForGitCli()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);
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

	#endregion

	#region CreateCommitAsync - Update and Remove Operations

	[Fact]
	public async Task CreateCommitAsync_SupportsUpdateAndRemoveOperations()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed files", ("src/app.txt", "v1"), ("docs/old.md", "legacy"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);
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
	public async Task CreateCommitAsync_RemoveMissingFileThrows()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

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
		var headRef = GitTestHelper.GetHeadReference(repo);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("data.txt", Encoding.UTF8.GetBytes("same"))
				},
				CreateMetadata("No change")));
	}

	#endregion

	#region CreateCommitAsync - Move Operations

	[Fact]
	public async Task CreateCommitAsync_CanMoveFiles()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Seed file", ("docs/readme.txt", "hello"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);
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

	#endregion

	#region ReadObjectAsync / WriteObjectAsync

	[Fact]
	public async Task WriteObjectAsync_CreatesBlob()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var content = Encoding.UTF8.GetBytes("test content");

		var hash = await gitRepository.WriteObjectAsync(GitObjectType.Blob, content);

		var obj = await gitRepository.ReadObjectAsync(hash);
		Assert.Equal(GitObjectType.Blob, obj.Type);
		Assert.Equal("test content", Encoding.UTF8.GetString(obj.Content));
	}

	[Fact]
	public async Task WriteObjectAsync_MatchesGitHashCalc()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var content = Encoding.UTF8.GetBytes("test content for hashing");

		var hash = await gitRepository.WriteObjectAsync(GitObjectType.Blob, content);

		var catFile = repo.RunGit($"cat-file -t {hash.Value}");
		Assert.Equal("blob", catFile.Trim());
		
		var catContent = repo.RunGit($"cat-file -p {hash.Value}");
		Assert.Equal("test content for hashing", catContent.Trim());
	}

	[Fact]
	public async Task ReadObjectAsync_ReadsBlobFromGit()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("test.txt", "file content"));
		var blobHash = new GitHash(repo.RunGit("rev-parse HEAD:test.txt").Trim());
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var obj = await gitRepository.ReadObjectAsync(blobHash);

		Assert.Equal(GitObjectType.Blob, obj.Type);
		Assert.Equal("file content", Encoding.UTF8.GetString(obj.Content));
	}

	[Fact]
	public async Task ReadObjectAsync_ReadsCommitObject()
	{
		using var repo = GitTestRepository.Create();
		var commitHash = repo.Commit("Test commit", ("file.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var obj = await gitRepository.ReadObjectAsync(commitHash);

		Assert.Equal(GitObjectType.Commit, obj.Type);
		var content = Encoding.UTF8.GetString(obj.Content);
		Assert.Contains("Test commit", content);
	}

	[Fact]
	public async Task ReadObjectAsync_ReadsTreeObject()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Test commit", ("file.txt", "content"));
		var treeHash = new GitHash(repo.RunGit("rev-parse HEAD^{tree}").Trim());
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var obj = await gitRepository.ReadObjectAsync(treeHash);

		Assert.Equal(GitObjectType.Tree, obj.Type);
	}

	[Fact]
	public async Task ReadObjectAsync_NonExistent_ThrowsFileNotFoundException()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var fakeHash = new GitHash("1234567890123456789012345678901234567890");

		await Assert.ThrowsAsync<FileNotFoundException>(() =>
			gitRepository.ReadObjectAsync(fakeHash));
	}

	#endregion

	#region GetReferencesAsync

	[Fact]
	public async Task GetReferencesAsync_ReturnsAllBranches()
	{
		using var repo = GitTestRepository.Create();
		var defaultBranch = GitTestHelper.GetDefaultBranch(repo);
		repo.RunGit("branch feature1");
		repo.RunGit("branch feature2");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var references = await gitRepository.GetReferencesAsync();

		Assert.Contains(references, r => r.Key == $"refs/heads/{defaultBranch}");
		Assert.Contains(references, r => r.Key == "refs/heads/feature1");
		Assert.Contains(references, r => r.Key == "refs/heads/feature2");
	}

	[Fact]
	public async Task GetReferencesAsync_ReturnsTags()
	{
		using var repo = GitTestRepository.Create();
		repo.RunGit("tag v1.0");
		repo.RunGit("tag -a v2.0 -m \"Version 2.0\"");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var references = await gitRepository.GetReferencesAsync();

		Assert.Contains(references, r => r.Key == "refs/tags/v1.0");
		Assert.Contains(references, r => r.Key == "refs/tags/v2.0");
	}

	[Fact]
	public async Task GetReferencesAsync_IsReadOnly()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var references = await gitRepository.GetReferencesAsync();

		Assert.IsAssignableFrom<IReadOnlyDictionary<string, GitHash>>(references);
	}

	[Fact]
	public async Task GetReferencesAsync_ReturnsSnapshot()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var refs1 = await gitRepository.GetReferencesAsync();
		var countBefore = refs1.Count;

		repo.RunGit("branch new-branch");

		var refs2 = await gitRepository.GetReferencesAsync();

		Assert.Equal(countBefore, refs1.Count);
		Assert.Equal(countBefore, refs2.Count);

		gitRepository.InvalidateCaches();
		var refs3 = await gitRepository.GetReferencesAsync();

		Assert.True(refs3.Count > countBefore);
	}

	#endregion

	#region Integration Test

	[Fact]
	public async Task FullWorkflow_CreateCommitAndRead_WorksEndToEnd()
	{
		var repoPath = Path.Combine(Path.GetTempPath(), $"workflow-test-{Guid.NewGuid():N}");
		try
		{
			var repository = GitRepository.Init(repoPath);

			GitTestHelper.RunGit(repoPath, "config user.name \"Test User\"");
            GitTestHelper.RunGit(repoPath, "config user.email test@test.com");
			File.WriteAllText(Path.Combine(repoPath, "README.md"), "# Test");
			GitTestHelper.RunGit(repoPath, "add README.md");
            GitTestHelper.RunGit(repoPath, "commit -m \"Initial commit\"");

			var metadata = new GitCommitMetadata(
				"Add feature",
				new GitCommitSignature("Library User", "lib@test.com", DateTimeOffset.UtcNow));

			var commitHash = await repository.CreateCommitAsync(
				"main",
				new GitCommitOperation[]
				{
					new AddFileOperation("feature.txt", Encoding.UTF8.GetBytes("feature content")),
					new AddFileOperation("docs/api.md", Encoding.UTF8.GetBytes("# API Documentation"))
				},
				metadata);

			var commit = await repository.GetCommitAsync();
			Assert.Equal(commitHash, commit.Id);
			Assert.Equal("Add feature", commit.Message.Trim());

			var featureContent = await repository.ReadFileAsync("feature.txt");
			Assert.Equal("feature content", Encoding.UTF8.GetString(featureContent));

			var apiContent = await repository.ReadFileAsync("docs/api.md");
			Assert.Equal("# API Documentation", Encoding.UTF8.GetString(apiContent));

			var gitLog = GitTestHelper.RunGit(repoPath, "log --oneline");
			Assert.Contains("Add feature", gitLog);

			var gitShow = GitTestHelper.RunGit(repoPath, "show HEAD:feature.txt");
			Assert.Equal("feature content", gitShow.Trim());
		}
        finally
        {
            GitTestHelper.TryDeleteDirectory(repoPath);
        }
    }

	#endregion

	#region Helper Methods

	private static GitCommitMetadata CreateMetadata(string message)
		=> new(
			message,
			new GitCommitSignature("Api User",
			"api@example.com",
			new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero)));

	#endregion
}
