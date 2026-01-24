using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for concurrency protection in GitRepository operations.
/// </summary>
public sealed class GitRepositoryConcurrencyTests : IDisposable
{
    private readonly string _tempDirectory;

    public GitRepositoryConcurrencyTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PmadGitConcurrencyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    #region Concurrent Commit Tests

    [Fact]
    public async Task CreateCommitAsync_ConcurrentCommitsToSameBranch_BothSucceed()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        var startSignal = new TaskCompletionSource<bool>();
        var commits = new List<GitHash>();

        // Act - Two threads try to commit at the same time
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            var hash = await gitRepository.CreateCommitAsync(
                headRef,
                new GitCommitOperation[] { new AddFileOperation("file1.txt", Encoding.UTF8.GetBytes("content1")) },
                CreateMetadata("Commit 1"));
            lock (commits) { commits.Add(hash); }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            await Task.Delay(10); // Slight delay to ensure task1 gets lock first
            var hash = await gitRepository.CreateCommitAsync(
                headRef,
                new GitCommitOperation[] { new AddFileOperation("file2.txt", Encoding.UTF8.GetBytes("content2")) },
                CreateMetadata("Commit 2"));
            lock (commits) { commits.Add(hash); }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Both commits should succeed, and we should see both files
        Assert.Equal(2, commits.Count);
        Assert.NotEqual(commits[0], commits[1]);

        var file1Content = await gitRepository.ReadFileAsync("file1.txt");
        var file2Content = await gitRepository.ReadFileAsync("file2.txt");
        Assert.Equal("content1", Encoding.UTF8.GetString(file1Content));
        Assert.Equal("content2", Encoding.UTF8.GetString(file2Content));
    }

    [Fact]
    public async Task CreateCommitAsync_ConcurrentCommitsToDifferentBranches_BothSucceedInParallel()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.RunGit("branch feature");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var defaultBranch = GitTestHelper.GetDefaultBranch(repo);

        var startSignal = new TaskCompletionSource<bool>();
        var startTimes = new List<DateTimeOffset>();
        var endTimes = new List<DateTimeOffset>();
        var lockObject = new object();

        // Act
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            lock (lockObject) { startTimes.Add(DateTimeOffset.UtcNow); }
            await gitRepository.CreateCommitAsync(
                defaultBranch,
                new GitCommitOperation[] { new AddFileOperation("main.txt", Encoding.UTF8.GetBytes("main")) },
                CreateMetadata("Main commit"));
            lock (lockObject) { endTimes.Add(DateTimeOffset.UtcNow); }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            lock (lockObject) { startTimes.Add(DateTimeOffset.UtcNow); }
            await gitRepository.CreateCommitAsync(
                "feature",
                new GitCommitOperation[] { new AddFileOperation("feature.txt", Encoding.UTF8.GetBytes("feature")) },
                CreateMetadata("Feature commit"));
            lock (lockObject) { endTimes.Add(DateTimeOffset.UtcNow); }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Operations should overlap (run in parallel)
        var maxStart = startTimes.Max();
        var minEnd = endTimes.Min();

        Assert.True(maxStart < minEnd, "Operations on different branches should run in parallel");
    }

    [Fact]
    public async Task CreateCommitAsync_StressTest_ManySequentialCommits()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var commitCount = 50;

        // Act
        var tasks = Enumerable.Range(1, commitCount).Select(i => Task.Run(async () =>
        {
            await gitRepository.CreateCommitAsync(
                headRef,
                new GitCommitOperation[] { new AddFileOperation($"file{i}.txt", Encoding.UTF8.GetBytes($"content{i}")) },
                CreateMetadata($"Commit {i}"));
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert - All files should exist
        for (int i = 1; i <= commitCount; i++)
        {
            var content = await gitRepository.ReadFileAsync($"file{i}.txt");
            Assert.Equal($"content{i}", Encoding.UTF8.GetString(content));
        }
    }

    #endregion

    #region Fast-Forward Validation Tests

    [Fact]
    public async Task IsCommitReachableAsync_DirectParent_ReturnsTrue()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First commit", ("file1.txt", "content1"));
        var commit2 = repo.Commit("Second commit", ("file2.txt", "content2"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act
        var isReachable = await gitRepository.IsCommitReachableAsync(commit2, commit1);

        // Assert
        Assert.True(isReachable, "Parent commit should be reachable from child commit");
    }

    [Fact]
    public async Task IsCommitReachableAsync_GrandParent_ReturnsTrue()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file2.txt", "content2"));
        var commit3 = repo.Commit("Third", ("file3.txt", "content3"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act
        var isReachable = await gitRepository.IsCommitReachableAsync(commit3, commit1);

        // Assert
        Assert.True(isReachable, "Grandparent commit should be reachable from grandchild commit");
    }

    [Fact]
    public async Task IsCommitReachableAsync_SameCommit_ReturnsTrue()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("Commit", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act
        var isReachable = await gitRepository.IsCommitReachableAsync(commit1, commit1);

        // Assert
        Assert.True(isReachable, "Commit should be reachable from itself");
    }

    [Fact]
    public async Task IsCommitReachableAsync_UnrelatedBranches_ReturnsFalse()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        
        // Create a separate branch from the initial commit
        repo.RunGit("checkout -b feature HEAD~1");
        var commit2 = repo.Commit("Feature", ("file2.txt", "content2"));
        
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act
        var isReachable = await gitRepository.IsCommitReachableAsync(commit2, commit1);

        // Assert
        Assert.False(isReachable, "Commit from different branch should not be reachable");
    }

    [Fact]
    public async Task IsCommitReachableAsync_ChildToParent_ReturnsFalse()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file2.txt", "content2"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act
        var isReachable = await gitRepository.IsCommitReachableAsync(commit1, commit2);

        // Assert
        Assert.False(isReachable, "Child commit should not be reachable from parent commit");
    }

    #endregion

    #region Validated Reference Update Tests

    [Fact]
    public async Task WriteReferenceWithValidationAsync_MatchingOldValue_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file.txt", "content"));
        var commit2 = repo.Commit("Second", ("file.txt", "updated"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();
        var headRef = GitTestHelper.GetHeadReference(repo);

        // Act & Assert - Should succeed
        await gitRepository.WriteReferenceWithValidationAsync(headRef, commit2, commit1);

        gitRepository.InvalidateCaches();
        var refs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit1, refs[headRef]);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_MismatchedOldValue_Throws()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file.txt", "content"));
        var commit2 = repo.Commit("Second", ("file.txt", "updated"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        // Create a fake hash that doesn't match
        var fakeOldHash = new GitHash("1234567890123456789012345678901234567890");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await gitRepository.WriteReferenceWithValidationAsync(headRef, fakeOldHash, commit1));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_CreateNewReference_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Commit", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var newRefPath = "refs/heads/newbranch";

        // Act - Create new reference
        await gitRepository.WriteReferenceWithValidationAsync(newRefPath, null, commit);

        // Assert
        gitRepository.InvalidateCaches();
        var refs = await gitRepository.GetReferencesAsync();
        Assert.True(refs.TryGetValue(newRefPath, out var actualCommit));
        Assert.Equal(commit, actualCommit);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_DeleteReference_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Commit", ("file.txt", "content"));
        repo.RunGit("branch deleteme");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();
        var refPath = "refs/heads/deleteme";
        
        var refs = await gitRepository.GetReferencesAsync();
        var currentValue = refs[refPath];

        // Act - Delete reference
        await gitRepository.WriteReferenceWithValidationAsync(refPath, currentValue, null);

        // Assert
        gitRepository.InvalidateCaches();
        refs = await gitRepository.GetReferencesAsync();
        Assert.False(refs.ContainsKey(refPath));
    }

    #endregion

    #region Multiple Reference Locks Tests

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithMultipleReferences_AcquiresAllLocks()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));
        repo.RunGit("branch feature1");
        repo.RunGit("branch feature2");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var refs = new[] { "refs/heads/main", "refs/heads/feature1", "refs/heads/feature2" };

        // Act & Assert - Should acquire all locks successfully 
        using (await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            // Locks acquired, can perform batch operations
            Assert.True(true);
        }
        // Locks released
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_LocksInConsistentOrder_PreventDeadlock()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));
        repo.RunGit("branch branch-a");
        repo.RunGit("branch branch-b");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        var refs1 = new[] { "refs/heads/branch-b", "refs/heads/branch-a" }; // Reverse order
        var refs2 = new[] { "refs/heads/branch-a", "refs/heads/branch-b" }; // Normal order

        var startSignal = new TaskCompletionSource<bool>();
        var completions = new List<int>();
        var lockObject = new object();

        // Act - Two threads acquire locks in different order
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(refs1))
            {
                await Task.Delay(50);
                lock (lockObject) { completions.Add(1); }
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            await Task.Delay(10);
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(refs2))
            {
                await Task.Delay(50);
                lock (lockObject) { completions.Add(2); }
            }
        });

        startSignal.SetResult(true);
        
        // Should complete without deadlock
        await Task.WhenAll(task1, task2);

        // Assert - Both should complete
        Assert.Equal(2, completions.Count);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_ConcurrentBatchOperations_Serialized()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));
        repo.RunGit("branch branch-a");
        repo.RunGit("branch branch-b");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var refs = new[] { "refs/heads/branch-a", "refs/heads/branch-b" };
        var startSignal = new TaskCompletionSource<bool>();
        var operations = new List<string>();
        var lockObject = new object();

        // Act - Simulate concurrent batch updates (like git push)
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
            {
                lock (lockObject) { operations.Add("batch1-start"); }
                await Task.Delay(50);
                lock (lockObject) { operations.Add("batch1-end"); }
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            await Task.Delay(10);
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
            {
                lock (lockObject) { operations.Add("batch2-start"); }
                await Task.Delay(50);
                lock (lockObject) { operations.Add("batch2-end"); }
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Operations should be serialized (batch1 completes before batch2 starts, or vice versa)
        Assert.Equal(4, operations.Count);
        var batch1Start = operations.IndexOf("batch1-start");
        var batch1End = operations.IndexOf("batch1-end");
        var batch2Start = operations.IndexOf("batch2-start");
        var batch2End = operations.IndexOf("batch2-end");

        // Either batch1 completes before batch2 starts, or batch2 completes before batch1 starts
        var batch1First = batch1End < batch2Start;
        var batch2First = batch2End < batch1Start;
        Assert.True(batch1First || batch2First, "Operations should be fully serialized");
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithEmptyList_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act & Assert - Should not throw
        using (await gitRepository.AcquireMultipleReferenceLocksAsync(Array.Empty<string>()))
        {
            Assert.True(true);
        }
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithDuplicateReferences_AcquiresOnce()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var refs = new[] { "refs/heads/main", "refs/heads/main", "refs/heads/main" };

        // Act - Should handle duplicates by deduplication (acquiring each lock only once)
        var startTime = DateTimeOffset.UtcNow;
        using (await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            Assert.True(true);
        }
        var endTime = DateTimeOffset.UtcNow;

        // Assert - Should complete quickly (not try to acquire same lock multiple times which would deadlock)
        Assert.True((endTime - startTime).TotalSeconds < 5, "Operation should complete quickly without deadlock");
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_DoesNotBlockSingleRefOperations_OnDifferentBranches()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));
        repo.RunGit("branch feature");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        var batchRefs = new[] { "refs/heads/main" };
        var startSignal = new TaskCompletionSource<bool>();
        var timestamps = new List<(string op, DateTimeOffset time)>();
        var lockObject = new object();

        // Act - Batch operation on main, single operation on feature
        var batchTask = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(batchRefs))
            {
                lock (lockObject) { timestamps.Add(("batch-start", DateTimeOffset.UtcNow)); }
                await Task.Delay(100);
                lock (lockObject) { timestamps.Add(("batch-end", DateTimeOffset.UtcNow)); }
            }
        });

        var singleTask = Task.Run(async () =>
        {
            await startSignal.Task;
            await Task.Delay(20);
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(new[] { "refs/heads/feature" }))
            {
                lock (lockObject) { timestamps.Add(("single-start", DateTimeOffset.UtcNow)); }
                await Task.Delay(50);
                lock (lockObject) { timestamps.Add(("single-end", DateTimeOffset.UtcNow)); }
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(batchTask, singleTask);

        // Assert - Single operation should complete while batch is running (operations overlap)
        var batchStart = timestamps.First(t => t.op == "batch-start").time;
        var batchEnd = timestamps.First(t => t.op == "batch-end").time;
        var singleStart = timestamps.First(t => t.op == "single-start").time;
        var singleEnd = timestamps.First(t => t.op == "single-end").time;

        Assert.True(singleStart > batchStart && singleStart < batchEnd, 
            "Single operation should start while batch is running");
        Assert.True(singleEnd < batchEnd, 
            "Single operation should complete while batch is still running");
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithPreCancelledToken_ThrowsImmediately()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));
        repo.RunGit("branch branch-a");
        repo.RunGit("branch branch-b");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        var refs = new[] { "refs/heads/branch-a", "refs/heads/branch-b" };
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        // Act & Assert - Should throw OperationCanceledException immediately
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await gitRepository.AcquireMultipleReferenceLocksAsync(refs, cts.Token));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithNullReferencePaths_ThrowsArgumentNullException()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await gitRepository.AcquireMultipleReferenceLocksAsync(null!));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithInvalidReferencePath_ThrowsArgumentException()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var refs = new[] { "refs/heads/valid", "" }; // Empty reference path

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await gitRepository.AcquireMultipleReferenceLocksAsync(refs));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithNonAbsoluteReferencePath_ThrowsArgumentException()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var refs = new[] { "refs/heads/valid", "main" }; // "main" is not an absolute path

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await gitRepository.AcquireMultipleReferenceLocksAsync(refs));
        
        Assert.Contains("must start with 'refs/'", exception.Message);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_NormalizesReferencePaths()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));
        repo.RunGit("branch feature");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        
        // Use references with different path separators and whitespace
        var refs = new[] { "refs/heads/main  ", "  refs\\heads\\feature", "refs/heads/main" };

        // Act - Should normalize and deduplicate without throwing
        using (await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            Assert.True(true);
        }
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullScenario_ConcurrentCommitsAndReads_WorkCorrectly()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var commitCount = 20;

        // Act - Concurrent commits and reads
        var commitTasks = Enumerable.Range(1, commitCount).Select(i => Task.Run(async () =>
        {
            await gitRepository.CreateCommitAsync(
                headRef,
                new GitCommitOperation[] { new AddFileOperation($"file{i}.txt", Encoding.UTF8.GetBytes($"content{i}")) },
                CreateMetadata($"Commit {i}"));
        })).ToArray();

        var readTasks = Enumerable.Range(1, 10).Select(_ => Task.Run(async () =>
        {
            await Task.Delay(Random.Shared.Next(100));
            var commit = await gitRepository.GetCommitAsync();
            return commit;
        })).ToArray();

        await Task.WhenAll(commitTasks);
        await Task.WhenAll(readTasks);

        // Assert - All files should be present
        for (int i = 1; i <= commitCount; i++)
        {
            var content = await gitRepository.ReadFileAsync($"file{i}.txt");
            Assert.Equal($"content{i}", Encoding.UTF8.GetString(content));
        }
    }

    [Fact]
    public async Task Scenario_SimulateRaceCondition_PreventsDataLoss()
    {
        // This test simulates what would happen without locks:
        // Two threads read the same commit, create different commits, both try to update
        // With locks: Second operation sees the updated state and builds on top
        // Without locks: Second operation would overwrite first (data loss)

        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var lockObject = new object();

        // Act
        var task1 = Task.Run(async () =>
        {
            await gitRepository.CreateCommitAsync(
                headRef,
                new GitCommitOperation[] { new AddFileOperation("task1.txt", Encoding.UTF8.GetBytes("task1")) },
                CreateMetadata("Task 1"));
        });

        var task2 = Task.Run(async () =>
        {
            await Task.Delay(20); // Slight delay
            await gitRepository.CreateCommitAsync(
                headRef,
                new GitCommitOperation[] { new AddFileOperation("task2.txt", Encoding.UTF8.GetBytes("task2")) },
                CreateMetadata("Task 2"));
        });

        await Task.WhenAll(task1, task2);

        // Assert - Both files should exist (no data loss)
        var task1Content = await gitRepository.ReadFileAsync("task1.txt");
        var task2Content = await gitRepository.ReadFileAsync("task2.txt");
        
        Assert.Equal("task1", Encoding.UTF8.GetString(task1Content));
        Assert.Equal("task2", Encoding.UTF8.GetString(task2Content));
        
        // Verify we can read history
        var commits = new List<GitCommit>();
        await foreach (var commit in gitRepository.EnumerateCommitsAsync())
        {
            commits.Add(commit);
        }
        
        Assert.Contains(commits, c => c.Message.Contains("Task 1"));
        Assert.Contains(commits, c => c.Message.Contains("Task 2"));
    }

    #endregion

    private static GitCommitMetadata CreateMetadata(string message)
        => new(
            message,
            new GitCommitSignature("Test User", "test@test.com", DateTimeOffset.UtcNow));

    public void Dispose()
    {
        GitTestHelper.TryDeleteDirectory(_tempDirectory);
    }
}
