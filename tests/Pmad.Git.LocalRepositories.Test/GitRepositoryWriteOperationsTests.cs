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

			var refs = await repository.ReferenceStore.GetReferencesAsync();
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

			var refs = await repository.ReferenceStore.GetReferencesAsync();
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

	#region CreateCommitAsync - UpdateFileOperation with Hash Validation

	[Fact]
	public async Task UpdateFileOperation_WithCorrectExpectedHash_Succeeds()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("config.txt", "version 1"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var fileInfo = await gitRepository.ReadFileAndHashAsync("config.txt");
		var expectedHash = fileInfo.Hash;

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("config.txt", Encoding.UTF8.GetBytes("version 2"), expectedHash)
			},
			CreateMetadata("Update with hash validation"));

		var updatedContent = await gitRepository.ReadFileAsync("config.txt", commitHash.Value);
		Assert.Equal("version 2", Encoding.UTF8.GetString(updatedContent));
	}

	[Fact]
	public async Task UpdateFileOperation_WithIncorrectExpectedHash_ThrowsGitFileConflictException()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("config.txt", "version 1"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var wrongHash = new GitHash("0000000000000000000000000000000000000000");

		var exception = await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("config.txt", Encoding.UTF8.GetBytes("version 2"), wrongHash)
				},
				CreateMetadata("Update with wrong hash")));

		Assert.Contains("config.txt", exception.Message);
		Assert.Contains("has hash", exception.Message);
		Assert.Contains("expected", exception.Message);
		Assert.Equal("config.txt", exception.FilePath);
	}

	[Fact]
	public async Task UpdateFileOperation_WithExpectedHashAfterFileChanged_ThrowsGitFileConflictException()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("shared.txt", "version 1"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var fileInfo = await gitRepository.ReadFileAndHashAsync("shared.txt");
		var oldHash = fileInfo.Hash;

		repo.Commit("Update file externally", ("shared.txt", "version 2"));
		gitRepository.InvalidateCaches();

		var exception = await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("shared.txt", Encoding.UTF8.GetBytes("my version"), oldHash)
				},
				CreateMetadata("Update with stale hash")));

		Assert.Contains("shared.txt", exception.Message);
		Assert.Equal("shared.txt", exception.FilePath);
	}

	[Fact]
	public async Task UpdateFileOperation_ConflictException_ContainsBothHashes()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("file.txt", "original"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var expectedHash = new GitHash("1111111111111111111111111111111111111111");
		var actualInfo = await gitRepository.ReadFileAndHashAsync("file.txt");

		var exception = await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("file.txt", Encoding.UTF8.GetBytes("new content"), expectedHash)
				},
				CreateMetadata("Update with validation")));

		Assert.Contains(actualInfo.Hash.Value, exception.Message);
		Assert.Contains(expectedHash.Value, exception.Message);
	}

	[Fact]
	public async Task UpdateFileOperation_MultipleUpdatesWithCorrectHashes_Succeeds()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add files", ("file1.txt", "content1"), ("file2.txt", "content2"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var file1Info = await gitRepository.ReadFileAndHashAsync("file1.txt");
		var file2Info = await gitRepository.ReadFileAndHashAsync("file2.txt");

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("file1.txt", Encoding.UTF8.GetBytes("updated1"), file1Info.Hash),
				new UpdateFileOperation("file2.txt", Encoding.UTF8.GetBytes("updated2"), file2Info.Hash)
			},
			CreateMetadata("Update multiple files with validation"));

		var content1 = await gitRepository.ReadFileAsync("file1.txt", commitHash.Value);
		var content2 = await gitRepository.ReadFileAsync("file2.txt", commitHash.Value);
		Assert.Equal("updated1", Encoding.UTF8.GetString(content1));
		Assert.Equal("updated2", Encoding.UTF8.GetString(content2));
	}

	[Fact]
	public async Task UpdateFileOperation_SequentialUpdatesWithHashValidation_TracksChanges()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Initial", ("counter.txt", "0"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var version0 = await gitRepository.ReadFileAndHashAsync("counter.txt");

		await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("counter.txt", Encoding.UTF8.GetBytes("1"), version0.Hash)
			},
			CreateMetadata("Update to 1"));

		gitRepository.InvalidateCaches();
		var version1 = await gitRepository.ReadFileAndHashAsync("counter.txt");

		await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("counter.txt", Encoding.UTF8.GetBytes("2"), version1.Hash)
			},
			CreateMetadata("Update to 2"));

		gitRepository.InvalidateCaches();
		var version2 = await gitRepository.ReadFileAndHashAsync("counter.txt");

		Assert.NotEqual(version0.Hash, version1.Hash);
		Assert.NotEqual(version1.Hash, version2.Hash);
		Assert.NotEqual(version0.Hash, version2.Hash);

		Assert.Equal("0", Encoding.UTF8.GetString(version0.Content));
		Assert.Equal("1", Encoding.UTF8.GetString(version1.Content));
		Assert.Equal("2", Encoding.UTF8.GetString(version2.Content));
	}

	[Fact]
	public async Task UpdateFileOperation_SequentialUpdate_UsingOldHash_ThrowsConflict()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Initial", ("data.txt", "v1"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var v1Info = await gitRepository.ReadFileAndHashAsync("data.txt");

		await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("data.txt", Encoding.UTF8.GetBytes("v2"))
			},
			CreateMetadata("Update to v2"));

		gitRepository.InvalidateCaches();

		var exception = await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("data.txt", Encoding.UTF8.GetBytes("v3"), v1Info.Hash)
				},
				CreateMetadata("Update with old hash")));

		Assert.Equal("data.txt", exception.FilePath);
	}

	[Fact]
	public async Task UpdateFileOperation_OptimisticLockingWorkflow_DetectsConflicts()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Initial", ("doc.md", "# Document"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var initialState = await gitRepository.ReadFileAndHashAsync("doc.md");

		await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("doc.md", Encoding.UTF8.GetBytes("# Document\n\nEditor 1 changes"), initialState.Hash)
			},
			CreateMetadata("Editor 1 saves"));

		gitRepository.InvalidateCaches();

		var exception = await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("doc.md", Encoding.UTF8.GetBytes("# Document\n\nEditor 2 changes"), initialState.Hash)
				},
				CreateMetadata("Editor 2 saves")));

		Assert.Equal("doc.md", exception.FilePath);

		var currentContent = await gitRepository.ReadFileAsync("doc.md");
		Assert.Equal("# Document\n\nEditor 1 changes", Encoding.UTF8.GetString(currentContent));
	}

	[Fact]
	public async Task UpdateFileOperation_ConcurrentEditSimulation_SecondEditorCanRetry()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Initial", ("doc.md", "# Document"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var initialState = await gitRepository.ReadFileAndHashAsync("doc.md");

		await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("doc.md", Encoding.UTF8.GetBytes("# Document\n\nEditor 1 changes"), initialState.Hash)
			},
			CreateMetadata("Editor 1 saves"));

		gitRepository.InvalidateCaches();

		await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("doc.md", Encoding.UTF8.GetBytes("# Document\n\nEditor 2 changes"), initialState.Hash)
				},
				CreateMetadata("Editor 2 first attempt")));

		var refreshedState = await gitRepository.ReadFileAndHashAsync("doc.md");

		var retryCommit = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("doc.md", Encoding.UTF8.GetBytes("# Document\n\nEditor 1 changes\n\nEditor 2 merged changes"), refreshedState.Hash)
			},
			CreateMetadata("Editor 2 retry after refresh"));

		var finalContent = await gitRepository.ReadFileAsync("doc.md", retryCommit.Value);
		Assert.Equal("# Document\n\nEditor 1 changes\n\nEditor 2 merged changes", Encoding.UTF8.GetString(finalContent));
	}

	[Fact]
	public async Task UpdateFileOperation_MixedOperationsWithHashValidation_Succeeds()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Setup", ("update.txt", "v1"), ("keep.txt", "unchanged"), ("remove.txt", "old"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var updateInfo = await gitRepository.ReadFileAndHashAsync("update.txt");

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("update.txt", Encoding.UTF8.GetBytes("v2"), updateInfo.Hash),
				new AddFileOperation("new.txt", Encoding.UTF8.GetBytes("new")),
				new RemoveFileOperation("remove.txt")
			},
			CreateMetadata("Mixed operations"));

		var updatedContent = await gitRepository.ReadFileAsync("update.txt", commitHash.Value);
		Assert.Equal("v2", Encoding.UTF8.GetString(updatedContent));

		var newContent = await gitRepository.ReadFileAsync("new.txt", commitHash.Value);
		Assert.Equal("new", Encoding.UTF8.GetString(newContent));

		var keepContent = await gitRepository.ReadFileAsync("keep.txt", commitHash.Value);
		Assert.Equal("unchanged", Encoding.UTF8.GetString(keepContent));

		await Assert.ThrowsAsync<FileNotFoundException>(
			() => gitRepository.ReadFileAsync("remove.txt", commitHash.Value));
	}

	[Fact]
	public async Task UpdateFileOperation_MultipleUpdates_OneWithWrongHash_ThrowsAndRollsBack()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Setup", ("file1.txt", "content1"), ("file2.txt", "content2"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var file1Info = await gitRepository.ReadFileAndHashAsync("file1.txt");
		var wrongHash = new GitHash("0000000000000000000000000000000000000000");

		await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("file1.txt", Encoding.UTF8.GetBytes("new1"), file1Info.Hash),
					new UpdateFileOperation("file2.txt", Encoding.UTF8.GetBytes("new2"), wrongHash)
				},
				CreateMetadata("Multiple updates with one wrong hash")));

		gitRepository.InvalidateCaches();
		var file1Content = await gitRepository.ReadFileAsync("file1.txt");
		var file2Content = await gitRepository.ReadFileAsync("file2.txt");
		Assert.Equal("content1", Encoding.UTF8.GetString(file1Content));
		Assert.Equal("content2", Encoding.UTF8.GetString(file2Content));
	}

	[Fact]
	public async Task UpdateFileOperation_NestedPath_WithCorrectHash_Succeeds()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add nested file", ("src/lib/module/config.json", "{\"version\":1}"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var fileInfo = await gitRepository.ReadFileAndHashAsync("src/lib/module/config.json");

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("src/lib/module/config.json",
					Encoding.UTF8.GetBytes("{\"version\":2}"),
					fileInfo.Hash)
			},
			CreateMetadata("Update nested file"));

		var content = await gitRepository.ReadFileAsync("src/lib/module/config.json", commitHash.Value);
		Assert.Equal("{\"version\":2}", Encoding.UTF8.GetString(content));
	}

	[Fact]
	public async Task UpdateFileOperation_Sha256Repository_WithCorrectHash_Succeeds()
	{
		using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
		repo.Commit("Add file", ("sha256.txt", "original"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var fileInfo = await gitRepository.ReadFileAndHashAsync("sha256.txt");
		Assert.Equal(GitHash.Sha256HexLength, fileInfo.Hash.Value.Length);

		var commitHash = await gitRepository.CreateCommitAsync(
			headRef,
			new GitCommitOperation[]
			{
				new UpdateFileOperation("sha256.txt", Encoding.UTF8.GetBytes("updated"), fileInfo.Hash)
			},
			CreateMetadata("Update SHA-256 file"));

		var content = await gitRepository.ReadFileAsync("sha256.txt", commitHash.Value);
		Assert.Equal("updated", Encoding.UTF8.GetString(content));
	}

	[Fact]
	public async Task UpdateFileOperation_Sha256Repository_WithWrongHash_ThrowsConflict()
	{
		using var repo = GitTestRepository.Create(GitObjectFormat.Sha256);
		repo.Commit("Add file", ("sha256.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headRef = GitTestHelper.GetHeadReference(repo);

		var wrongHash = new GitHash(new string('0', GitHash.Sha256HexLength));

		var exception = await Assert.ThrowsAsync<GitFileConflictException>(
			() => gitRepository.CreateCommitAsync(
				headRef,
				new GitCommitOperation[]
				{
					new UpdateFileOperation("sha256.txt", Encoding.UTF8.GetBytes("new"), wrongHash)
				},
				CreateMetadata("Update with wrong SHA-256 hash")));

		Assert.Equal("sha256.txt", exception.FilePath);
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

		var hash = await gitRepository.ObjectStore.WriteObjectAsync(GitObjectType.Blob, content);

		var obj = await gitRepository.ObjectStore.ReadObjectAsync(hash);
		Assert.Equal(GitObjectType.Blob, obj.Type);
		Assert.Equal("test content", Encoding.UTF8.GetString(obj.Content));
	}

	[Fact]
	public async Task WriteObjectAsync_MatchesGitHashCalc()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var content = Encoding.UTF8.GetBytes("test content for hashing");

		var hash = await gitRepository.ObjectStore.WriteObjectAsync(GitObjectType.Blob, content);

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

		var obj = await gitRepository.ObjectStore.ReadObjectAsync(blobHash);

		Assert.Equal(GitObjectType.Blob, obj.Type);
		Assert.Equal("file content", Encoding.UTF8.GetString(obj.Content));
	}

	[Fact]
	public async Task ReadObjectAsync_ReadsCommitObject()
	{
		using var repo = GitTestRepository.Create();
		var commitHash = repo.Commit("Test commit", ("file.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var obj = await gitRepository.ObjectStore.ReadObjectAsync(commitHash);

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

		var obj = await gitRepository.ObjectStore.ReadObjectAsync(treeHash);

		Assert.Equal(GitObjectType.Tree, obj.Type);
	}

	[Fact]
	public async Task ReadObjectAsync_NonExistent_ThrowsFileNotFoundException()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var fakeHash = new GitHash("1234567890123456789012345678901234567890");

		await Assert.ThrowsAsync<FileNotFoundException>(() =>
			gitRepository.ObjectStore.ReadObjectAsync(fakeHash));
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

		var references = await gitRepository.ReferenceStore.GetReferencesAsync();

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

		var references = await gitRepository.ReferenceStore.GetReferencesAsync();

		Assert.Contains(references, r => r.Key == "refs/tags/v1.0");
		Assert.Contains(references, r => r.Key == "refs/tags/v2.0");
	}

	[Fact]
	public async Task GetReferencesAsync_IsReadOnly()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var references = await gitRepository.ReferenceStore.GetReferencesAsync();

		Assert.IsAssignableFrom<IReadOnlyDictionary<string, GitHash>>(references);
	}

	[Fact]
	public async Task GetReferencesAsync_ReturnsSnapshot()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var refs1 = await gitRepository.ReferenceStore.GetReferencesAsync();
		var countBefore = refs1.Count;

		repo.RunGit("branch new-branch");

		var refs2 = await gitRepository.ReferenceStore.GetReferencesAsync();

		Assert.Equal(countBefore, refs1.Count);
		Assert.Equal(countBefore, refs2.Count);

		gitRepository.InvalidateCaches();
		var refs3 = await gitRepository.ReferenceStore.GetReferencesAsync();

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
