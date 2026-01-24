using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitRepositoryInitTests : IDisposable
{
	private readonly string _testRoot;

	public GitRepositoryInitTests()
	{
		_testRoot = Path.Combine(Path.GetTempPath(), "PmadGitInitTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_testRoot);
	}

	[Fact]
	public void Init_CreatesValidRepositoryStructure()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "test-repo");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert
		Assert.NotNull(repository);
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "objects")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "refs", "heads")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "refs", "tags")));
		Assert.True(File.Exists(Path.Combine(repoPath, ".git", "HEAD")));
		Assert.True(File.Exists(Path.Combine(repoPath, ".git", "config")));
		Assert.True(File.Exists(Path.Combine(repoPath, ".git", "description")));
	}

	[Fact]
	public void Init_CreatesHeadPointingToInitialBranch()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "test-head");

		// Act
		var repository = GitRepository.Init(repoPath, initialBranch: "develop");

		// Assert
		var headContent = File.ReadAllText(Path.Combine(repoPath, ".git", "HEAD"));
        Assert.NotNull(repository);
        Assert.Contains("ref: refs/heads/develop", headContent);
	}

	[Fact]
	public void Init_DefaultsToMainBranch()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "test-default-branch");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert
		var headContent = File.ReadAllText(Path.Combine(repoPath, ".git", "HEAD"));
        Assert.NotNull(repository);
        Assert.Contains("ref: refs/heads/main", headContent);
	}

	[Fact]
	public void Init_CreatesValidConfigFile()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "test-config");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert
		var configContent = File.ReadAllText(Path.Combine(repoPath, ".git", "config"));
        Assert.NotNull(repository);
        Assert.Contains("[core]", configContent);
		Assert.Contains("repositoryformatversion = 0", configContent);
		Assert.Contains("bare = false", configContent);
	}

	[Fact]
	public void Init_WithBareOption_CreatesBareRepository()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "bare-repo.git");

		// Act
		var repository = GitRepository.Init(repoPath, bare: true);

		// Assert
		var configContent = File.ReadAllText(Path.Combine(repoPath, "config"));
        Assert.NotNull(repository);
        Assert.Contains("bare = true", configContent);
		Assert.True(File.Exists(Path.Combine(repoPath, "HEAD")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, "objects")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, "refs")));
	}

	[Fact]
	public void Init_CanBeOpenedAgain()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "test-reopen");

		// Act
		var repository1 = GitRepository.Init(repoPath);
		var repository2 = GitRepository.Open(repoPath);

        // Assert
        Assert.NotNull(repository1);
        Assert.NotNull(repository2);
		Assert.Equal(repository1.RootPath, repository2.RootPath);
		Assert.Equal(repository1.GitDirectory, repository2.GitDirectory);
	}

	[Fact]
	public void Init_WithNullPath_ThrowsArgumentException()
	{
		// Act & Assert
		Assert.Throws<ArgumentException>(() => GitRepository.Init(null!));
	}

	[Fact]
	public void Init_WithEmptyPath_ThrowsArgumentException()
	{
		// Act & Assert
		Assert.Throws<ArgumentException>(() => GitRepository.Init(string.Empty));
	}

	[Fact]
	public void Init_WithWhitespacePath_ThrowsArgumentException()
	{
		// Act & Assert
		Assert.Throws<ArgumentException>(() => GitRepository.Init("   "));
	}

	[Fact]
	public void Init_WithEmptyInitialBranch_ThrowsArgumentException()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "test-empty-branch");

		// Act & Assert
		Assert.Throws<ArgumentException>(() => GitRepository.Init(repoPath, initialBranch: string.Empty));
	}

	[Fact]
	public void Init_InExistingNonEmptyDirectory_ThrowsInvalidOperationException()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "existing-repo");
		Directory.CreateDirectory(Path.Combine(repoPath, ".git"));
		File.WriteAllText(Path.Combine(repoPath, ".git", "somefile.txt"), "content");

		// Act & Assert
		Assert.Throws<InvalidOperationException>(() => GitRepository.Init(repoPath));
	}

	[Fact]
	public void Init_IsRecognizedByGitCli()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "git-cli-check");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert - Use git CLI to verify it's a valid repository
		var output = GitTestHelper.RunGit(repoPath, "rev-parse --git-dir");
        Assert.NotNull(repository);
        Assert.Contains(".git", output);
	}

	[Fact]
	public void Init_AllowsGitStatusCommand()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "git-status-check");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert - git status should work without errors
		var output = GitTestHelper.RunGit(repoPath, "status");
        Assert.NotNull(repository);
        Assert.Contains("No commits yet", output);
		Assert.Contains("nothing to commit", output);
	}

	[Fact]
	public void Init_AllowsCreatingInitialCommitWithGitCli()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "git-commit-check");
		var repository = GitRepository.Init(repoPath);

		// Act - Create a commit using git CLI
		File.WriteAllText(Path.Combine(repoPath, "test.txt"), "test content");
		GitTestHelper.RunGit(repoPath, "config user.name \"Test User\"");
		GitTestHelper.RunGit(repoPath, "config user.email test@example.com");
		GitTestHelper.RunGit(repoPath, "add test.txt");
        GitTestHelper.RunGit(repoPath, "commit -m \"Initial commit\"");

		// Assert
		var log = GitTestHelper.RunGit(repoPath, "log --oneline");
        Assert.NotNull(repository);
        Assert.Contains("Initial commit", log);
	}

	[Fact]
	public async Task Init_AllowsCreatingCommitWithLibrary()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "lib-commit-check");
		var repository = GitRepository.Init(repoPath);

		// First create initial commit with git CLI to establish the branch
		File.WriteAllText(Path.Combine(repoPath, "initial.txt"), "initial");
		GitTestHelper.RunGit(repoPath, "config user.name \"Test User\"");
		GitTestHelper.RunGit(repoPath, "config user.email test@example.com");
		GitTestHelper.RunGit(repoPath, "add initial.txt");
        GitTestHelper.RunGit(repoPath, "commit -m \"Initial commit\"");

		// Act - Create another commit using the library
		var metadata = new GitCommitMetadata(
			message: "Second commit",
			author: new GitCommitSignature("Library User", "lib@example.com", DateTimeOffset.UtcNow));

		var commitHash = await repository.CreateCommitAsync(
			"main",
			new GitCommitOperation[]
			{
				new AddFileOperation("second.txt", Encoding.UTF8.GetBytes("second content"))
			},
			metadata);

		// Assert
		var log = GitTestHelper.RunGit(repoPath, "log --oneline");
        Assert.NotNull(repository);
        Assert.Contains("Second commit", log);
		Assert.Contains("Initial commit", log);

		var showOutput = GitTestHelper.RunGit(repoPath, $"show {commitHash.Value}:second.txt");
		Assert.Equal("second content", showOutput.Trim());
	}

	[Fact]
	public void Init_BareRepository_IsRecognizedByGitCli()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "bare-cli-check.git");

		// Act
		var repository = GitRepository.Init(repoPath, bare: true);

		// Assert
		var output = GitTestHelper.RunGit(repoPath, "rev-parse --is-bare-repository");
        Assert.NotNull(repository);
        Assert.Equal("true", output.Trim());
	}

	[Fact]
	public void Init_WithCustomBranch_IsRecognizedByGitCli()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "custom-branch");

		// Act
		var repository = GitRepository.Init(repoPath, initialBranch: "feature");

		// Assert
		var output = GitTestHelper.RunGit(repoPath, "symbolic-ref HEAD");
        Assert.NotNull(repository);
        Assert.Contains("refs/heads/feature", output);
	}

	[Fact]
	public void Init_CreatesAllRequiredSubdirectories()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "subdirs-check");

		// Act
		var repository = GitRepository.Init(repoPath);

        // Assert
        Assert.NotNull(repository);
        Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "objects", "info")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "objects", "pack")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "refs", "heads")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "refs", "tags")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "hooks")));
		Assert.True(Directory.Exists(Path.Combine(repoPath, ".git", "info")));
	}

	[Fact]
	public void Init_CreatesInfoExcludeFile()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "info-exclude-check");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert
		var excludePath = Path.Combine(repoPath, ".git", "info", "exclude");
        Assert.NotNull(repository);
        Assert.True(File.Exists(excludePath));
		var content = File.ReadAllText(excludePath);
		Assert.Contains("# git ls-files --others --exclude-from=.git/info/exclude", content);
	}

	[Fact]
	public void Init_CreatesDescriptionFile()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "description-check");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert
		var descPath = Path.Combine(repoPath, ".git", "description");
        Assert.NotNull(repository);
        Assert.True(File.Exists(descPath));
		var content = File.ReadAllText(descPath);
		Assert.Contains("Unnamed repository", content);
	}

	[Fact]
	public void Init_RepositoryProperties_AreCorrect()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "properties-check");

		// Act
		var repository = GitRepository.Init(repoPath);

        // Assert
        Assert.NotNull(repository);
        Assert.Equal(Path.GetFullPath(repoPath), repository.RootPath);
		Assert.Equal(Path.GetFullPath(Path.Combine(repoPath, ".git")), repository.GitDirectory);
		Assert.Equal(20, repository.HashLengthBytes); // SHA-1 by default
	}

	[Fact]
	public void Init_BareRepository_Properties_AreCorrect()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "bare-properties.git");

		// Act
		var repository = GitRepository.Init(repoPath, bare: true);

        // Assert
        Assert.NotNull(repository);
        Assert.Equal(Path.GetFullPath(repoPath), repository.RootPath);
		Assert.Equal(Path.GetFullPath(repoPath), repository.GitDirectory);
	}

	[Fact]
	public void Init_CanInitializeMultipleRepositories()
	{
		// Arrange & Act
		var repo1 = GitRepository.Init(Path.Combine(_testRoot, "repo1"));
		var repo2 = GitRepository.Init(Path.Combine(_testRoot, "repo2"));
		var repo3 = GitRepository.Init(Path.Combine(_testRoot, "repo3"));

		// Assert
		Assert.NotEqual(repo1.RootPath, repo2.RootPath);
		Assert.NotEqual(repo2.RootPath, repo3.RootPath);
		Assert.True(Directory.Exists(Path.Combine(_testRoot, "repo1", ".git")));
		Assert.True(Directory.Exists(Path.Combine(_testRoot, "repo2", ".git")));
		Assert.True(Directory.Exists(Path.Combine(_testRoot, "repo3", ".git")));
	}

	[Fact]
	public void Init_VerifyWithGitFsck()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "fsck-check");

		// Act
		var repository = GitRepository.Init(repoPath);

		// Assert - git fsck should pass
		var output = GitTestHelper.RunGit(repoPath, "fsck");
        Assert.NotNull(repository);
        // fsck on empty repo should not report errors
        Assert.DoesNotContain("error:", output.ToLowerInvariant());
	}

	[Fact]
	public async Task Init_RepositoryCanAcceptFirstCommit()
	{
		// Arrange
		var repoPath = Path.Combine(_testRoot, "first-commit");
		var repository = GitRepository.Init(repoPath);

		// Create initial commit with git CLI
		File.WriteAllText(Path.Combine(repoPath, "README.md"), "# Test Repository");
		GitTestHelper.RunGit(repoPath, "config user.name \"Test User\"");
		GitTestHelper.RunGit(repoPath, "config user.email test@example.com");
		GitTestHelper.RunGit(repoPath, "add README.md");
        GitTestHelper.RunGit(repoPath, "commit -m \"Initial commit\"");

		// Act - Read the commit using the library
		var commit = await repository.GetCommitAsync();

		// Assert
		Assert.NotNull(commit);
		Assert.Equal("Initial commit", commit.Message.Trim());
	}

	[Fact]
	public void Init_WithRelativePath_CreatesRepositoryCorrectly()
	{
		// Arrange
		var currentDir = Environment.CurrentDirectory;
		try
		{
			Environment.CurrentDirectory = _testRoot;
			var relativePath = "relative-repo";

			// Act
			var repository = GitRepository.Init(relativePath);

			// Assert
			Assert.True(Directory.Exists(Path.Combine(_testRoot, relativePath, ".git")));
			Assert.True(Path.IsPathFullyQualified(repository.RootPath));
		}
		finally
		{
			Environment.CurrentDirectory = currentDir;
		}
	}

	public void Dispose()
	{
        GitTestHelper.TryDeleteDirectory(_testRoot);
    }
}
