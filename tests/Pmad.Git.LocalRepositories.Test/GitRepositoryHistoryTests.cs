using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitRepository history and enumeration operations.
/// </summary>
public sealed class GitRepositoryHistoryTests
{
	#region EnumerateCommitsAsync

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
	public async Task EnumerateCommitsAsync_WithMergeCommit_WalksAllParents()
	{
		using var repo = GitTestRepository.Create();
		var defaultBranch = GitTestHelper.GetDefaultBranch(repo);
		repo.Commit("Base", ("base.txt", "base"));
		
		repo.RunGit("checkout -b feature");
		repo.Commit("Feature work", ("feature.txt", "feature"));
		
		repo.RunGit($"checkout {defaultBranch}");
		repo.Commit("Main work", ("main.txt", "main"));
		
		repo.RunGit("merge feature --no-edit");
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var commits = new List<GitCommit>();
		await foreach (var commit in gitRepository.EnumerateCommitsAsync())
		{
			commits.Add(commit);
		}

		Assert.True(commits.Count >= 5);
		Assert.Contains(commits, c => c.Message.Contains("Feature work"));
		Assert.Contains(commits, c => c.Message.Contains("Main work"));
	}

	[Fact]
	public async Task EnumerateCommitsAsync_WithTag_StartsFromTaggedCommit()
	{
		using var repo = GitTestRepository.Create();
		var tagged = repo.Commit("Tagged version", ("file.txt", "v1"));
		repo.RunGit("tag v1.0");
		repo.Commit("After tag", ("file.txt", "v2"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var commits = new List<GitCommit>();
		await foreach (var commit in gitRepository.EnumerateCommitsAsync("v1.0"))
		{
			commits.Add(commit);
		}

		Assert.DoesNotContain(commits, c => c.Message.Contains("After tag"));
		Assert.Contains(commits, c => c.Message.Contains("Tagged version"));
	}

	[Fact]
	public async Task EnumerateCommitsAsync_AvoidsDuplicates()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Commit 1", ("file1.txt", "v1"));
		repo.Commit("Commit 2", ("file2.txt", "v2"));
		repo.Commit("Commit 3", ("file3.txt", "v3"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var commits = new List<GitCommit>();
		var hashes = new HashSet<string>();
		await foreach (var commit in gitRepository.EnumerateCommitsAsync())
		{
			commits.Add(commit);
			hashes.Add(commit.Id.Value);
		}

		Assert.Equal(commits.Count, hashes.Count);
	}

	[Fact]
	public async Task EnumerateCommitsAsync_RespectsCancellationToken()
	{
		using var repo = GitTestRepository.Create();
		for (int i = 0; i < 5; i++)
		{
			repo.Commit($"Commit {i}", ($"file{i}.txt", $"content{i}"));
		}
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);
		var cts = new CancellationTokenSource();
		
		var count = 0;
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await foreach (var commit in gitRepository.EnumerateCommitsAsync(cancellationToken: cts.Token))
			{
				count++;
				if (count == 2)
				{
					cts.Cancel();
				}
			}
		});
	}

	#endregion

	#region GetFileHistoryAsync

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
	public async Task GetFileHistoryAsync_FileNeverChanged_ReturnsSingleCommit()
	{
		using var repo = GitTestRepository.Create();
		var addCommit = repo.Commit("Add file", ("static.txt", "unchanged"));
		repo.Commit("Other change", ("other.txt", "content"));
		repo.Commit("Another change", ("another.txt", "data"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var history = new List<GitCommit>();
		await foreach (var commit in gitRepository.GetFileHistoryAsync("static.txt"))
		{
			history.Add(commit);
		}

		Assert.Single(history);
		Assert.Contains("Add file", history[0].Message);
	}

	[Fact]
	public async Task GetFileHistoryAsync_FileDeletedAndRecreated_ReturnsAllVersions()
	{
		using var repo = GitTestRepository.Create();
		var c1 = repo.Commit("Add file", ("file.txt", "v1"));
		repo.RunGit("rm file.txt");
		repo.RunGit("commit -m \"Remove file\"");
		var c3 = repo.Commit("Re-add file", ("file.txt", "v2"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var history = new List<GitCommit>();
		await foreach (var commit in gitRepository.GetFileHistoryAsync("file.txt"))
		{
			history.Add(commit);
		}

		Assert.Equal(2, history.Count);
	}

	[Fact]
	public async Task GetFileHistoryAsync_WithStartingCommit_LimitsHistory()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("V1", ("file.txt", "v1"));
		var startCommit = repo.Commit("V2", ("file.txt", "v2"));
		repo.Commit("V3", ("file.txt", "v3"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var history = new List<GitCommit>();
		await foreach (var commit in gitRepository.GetFileHistoryAsync("file.txt", startCommit.Value))
		{
			history.Add(commit);
		}

		Assert.Equal(2, history.Count);
	}

	#endregion

	#region EnumerateCommitTreeAsync

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
	public async Task EnumerateCommitTreeAsync_WithoutPath_EnumeratesAllFiles()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Complex tree", 
			("a/file1.txt", "content1"),
			("a/b/file2.txt", "content2"),
			("c/file3.txt", "content3"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var items = new List<GitTreeItem>();
		await foreach (var item in gitRepository.EnumerateCommitTreeAsync())
		{
			items.Add(item);
		}

		var paths = items.Select(i => i.Path).ToList();
		Assert.Contains("README.md", paths);
		Assert.Contains("a/file1.txt", paths);
		Assert.Contains("a/b/file2.txt", paths);
		Assert.Contains("c/file3.txt", paths);
	}

	[Fact]
	public async Task EnumerateCommitTreeAsync_WithSpecificCommit_EnumeratesCorrectTree()
	{
		using var repo = GitTestRepository.Create();
		var commit1 = repo.Commit("Commit 1", ("file1.txt", "v1"));
		var commit2 = repo.Commit("Commit 2", ("file2.txt", "v2"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var items = new List<GitTreeItem>();
		await foreach (var item in gitRepository.EnumerateCommitTreeAsync(commit1.Value))
		{
			items.Add(item);
		}

		var paths = items.Select(i => i.Path).ToList();
		Assert.Contains("file1.txt", paths);
		Assert.DoesNotContain("file2.txt", paths);
	}

	[Fact]
	public async Task EnumerateCommitTreeAsync_WithSingleFilePath_ReturnsFile()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Add file", ("dir/file.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var items = new List<GitTreeItem>();
		await foreach (var item in gitRepository.EnumerateCommitTreeAsync(path: "dir/file.txt"))
		{
			items.Add(item);
		}

		Assert.Single(items);
		Assert.Equal("dir/file.txt", items[0].Path);
		Assert.Equal(GitTreeEntryKind.Blob, items[0].Entry.Kind);
	}

	[Fact]
	public async Task EnumerateCommitTreeAsync_WithNonExistentPath_ThrowsDirectoryNotFoundException()
	{
		using var repo = GitTestRepository.Create();
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
		{
			await foreach (var item in gitRepository.EnumerateCommitTreeAsync(path: "non/existent"))
			{
			}
		});
	}

	[Fact]
	public async Task EnumerateCommitTreeAsync_IncludesTreeEntries()
	{
		using var repo = GitTestRepository.Create();
		repo.Commit("Nested dirs", ("a/b/c/file.txt", "content"));
		var gitRepository = GitRepository.Open(repo.WorkingDirectory);

		var items = new List<GitTreeItem>();
		await foreach (var item in gitRepository.EnumerateCommitTreeAsync())
		{
			items.Add(item);
		}

		Assert.Contains(items, i => i.Entry.Kind == GitTreeEntryKind.Tree);
		Assert.Contains(items, i => i.Entry.Kind == GitTreeEntryKind.Blob);
	}

	#endregion
}
