using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitRepository write operations: creating commits, writing objects, and managing references.
/// </summary>
public sealed class GitRepositoryWriteOperationsTests
{
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

	#region WriteReferenceAsync

	[Fact]
	public async Task WriteReferenceAsync_CreatesNewBranch()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headCommit = await gitRepository.GetCommitAsync();

		await gitRepository.WriteReferenceAsync("refs/heads/feature", headCommit.Id);

		var output = repo.RunGit("branch --list");
		Assert.Contains("feature", output);
	}

	[Fact]
	public async Task WriteReferenceAsync_UpdatesExistingBranch()
	{
		using var repo = GitTestRepository.Create();
		var commit1 = repo.Commit("Commit 1", ("file1.txt", "content1"));
		var commit2 = repo.Commit("Commit 2", ("file2.txt", "content2"));
		repo.RunGit("checkout -b test-branch HEAD~1");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await gitRepository.WriteReferenceAsync("refs/heads/test-branch", commit2);

		var branchCommit = repo.RunGit("rev-parse test-branch").Trim();
		Assert.Equal(commit2.Value, branchCommit);
	}

	[Fact]
	public async Task WriteReferenceAsync_CreatesTag()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headCommit = await gitRepository.GetCommitAsync();

		await gitRepository.WriteReferenceAsync("refs/tags/v1.0", headCommit.Id);

		var output = repo.RunGit("tag --list");
		Assert.Contains("v1.0", output);
	}

	[Fact]
	public async Task WriteReferenceAsync_OverwritesExistingReference()
	{
		using var repo = GitTestRepository.Create();
		var commit1 = repo.Commit("Commit 1", ("file1.txt", "content1"));
		var commit2 = repo.Commit("Commit 2", ("file2.txt", "content2"));
		repo.RunGit("tag v1.0 HEAD~1");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await gitRepository.WriteReferenceAsync("refs/tags/v1.0", commit2);

		var tagCommit = repo.RunGit("rev-parse v1.0").Trim();
		Assert.Equal(commit2.Value, tagCommit);
	}

	[Fact]
	public async Task WriteReferenceAsync_WithInvalidPath_ThrowsArgumentException()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var headCommit = await gitRepository.GetCommitAsync();

		await Assert.ThrowsAsync<ArgumentException>(() =>
			gitRepository.WriteReferenceAsync("", headCommit.Id));
	}

	#endregion

	#region DeleteReferenceAsync

	[Fact]
	public async Task DeleteReferenceAsync_RemovesBranch()
	{
		using var repo = GitTestRepository.Create();
		repo.RunGit("branch to-delete");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await gitRepository.DeleteReferenceAsync("refs/heads/to-delete");

		var output = repo.RunGit("branch --list");
		Assert.DoesNotContain("to-delete", output);
	}

	[Fact]
	public async Task DeleteReferenceAsync_RemovesTag()
	{
		using var repo = GitTestRepository.Create();
		repo.RunGit("tag v1.0");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await gitRepository.DeleteReferenceAsync("refs/tags/v1.0");

		var output = repo.RunGit("tag --list");
		Assert.DoesNotContain("v1.0", output);
	}

	[Fact]
	public async Task DeleteReferenceAsync_NonExistentReference_DoesNotThrow()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await gitRepository.DeleteReferenceAsync("refs/heads/non-existent");
	}

	[Fact]
	public async Task DeleteReferenceAsync_WithInvalidPath_ThrowsArgumentException()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await Assert.ThrowsAsync<ArgumentException>(() =>
			gitRepository.DeleteReferenceAsync(""));
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
			try
			{
				if (Directory.Exists(repoPath))
				{
					Directory.Delete(repoPath, recursive: true);
				}
			}
			catch
			{
				// Ignore cleanup failures
			}
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
