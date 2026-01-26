using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitMultipleReferenceLocks to verify batch reference operations
/// and lock behavior during multi-reference updates.
/// </summary>
public sealed class GitMultipleReferenceLocksTests : IDisposable
{
    private readonly string _tempDirectory;

    public GitMultipleReferenceLocksTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PmadGitMultipleLockTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    #region Basic Functionality Tests

    [Fact]
    public async Task WriteReferenceWithValidationAsync_WithLockedReference_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file.txt", "content2"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var headRef = GitTestHelper.GetHeadReference(repo);
        var refs = new[] { headRef };

        // Act
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync(headRef, commit2, commit1);
        }

        // Assert
        gitRepository.InvalidateCaches();
        var currentRefs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit1, currentRefs[headRef]);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_WithNonLockedReference_ThrowsInvalidOperationException()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Commit", ("file.txt", "content"));
        repo.RunGit("branch feature");
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var headRef = GitTestHelper.GetHeadReference(repo);
        var featureRef = "refs/heads/feature";
        var lockedRefs = new[] { headRef };

        // Act & Assert
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(lockedRefs))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await locks.WriteReferenceWithValidationAsync(featureRef, null, commit));

            Assert.Contains("not locked", exception.Message);
        }
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Commit", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var refs = new[] { headRef };

        var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs);
        locks.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await locks.WriteReferenceWithValidationAsync(headRef, null, commit));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_MultipleReferences_AllSucceed()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        repo.Commit("Second", ("file2.txt", "content2"));
        repo.RunGit("branch branch-a");
        repo.RunGit("branch branch-b");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var refs = new[] { "refs/heads/branch-a", "refs/heads/branch-b" };
        var currentRefs = await gitRepository.GetReferencesAsync();
        var currentA = currentRefs["refs/heads/branch-a"];
        var currentB = currentRefs["refs/heads/branch-b"];

        // Act
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync("refs/heads/branch-a", currentA, commit1);
            await locks.WriteReferenceWithValidationAsync("refs/heads/branch-b", currentB, commit1);
        }

        // Assert
        gitRepository.InvalidateCaches();
        var updatedRefs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit1, updatedRefs["refs/heads/branch-a"]);
        Assert.Equal(commit1, updatedRefs["refs/heads/branch-b"]);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_CreateNewReference_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Commit", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var newRef = "refs/heads/newbranch";
        var refs = new[] { newRef };

        // Act
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync(newRef, null, commit);
        }

        // Assert
        gitRepository.InvalidateCaches();
        var updatedRefs = await gitRepository.GetReferencesAsync();
        Assert.True(updatedRefs.TryGetValue(newRef, out var actualCommit));
        Assert.Equal(commit, actualCommit);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_DeleteReference_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Commit", ("file.txt", "content"));
        repo.RunGit("branch todelete");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var deleteRef = "refs/heads/todelete";
        var refs = new[] { deleteRef };

        var currentRefs = await gitRepository.GetReferencesAsync();
        var currentValue = currentRefs[deleteRef];

        // Act
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync(deleteRef, currentValue, null);
        }

        // Assert
        gitRepository.InvalidateCaches();
        var updatedRefs = await gitRepository.GetReferencesAsync();
        Assert.False(updatedRefs.ContainsKey(deleteRef));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_WithMismatchedOldValue_ThrowsInvalidOperationException()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file.txt", "content1"));
        repo.Commit("Second", ("file.txt", "content2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var refs = new[] { headRef };

        var fakeOldHash = new GitHash("1234567890123456789012345678901234567890");

        // Act & Assert
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await locks.WriteReferenceWithValidationAsync(headRef, fakeOldHash, commit1));
        }
    }

    #endregion

    #region Batch Operations Tests

    [Fact]
    public async Task BatchUpdate_SimulateGitPush_AllReferencesUpdated()
    {
        // Arrange - Simulate a git push with multiple branch updates
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file2.txt", "content2"));
        var commit3 = repo.Commit("Third", ("file3.txt", "content3"));

        repo.RunGit("branch feature-a");
        repo.RunGit("branch feature-b");
        repo.RunGit("branch feature-c");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var refs = new[]
        {
            "refs/heads/feature-a",
            "refs/heads/feature-b",
            "refs/heads/feature-c"
        };

        var currentRefs = await gitRepository.GetReferencesAsync();

        // Act - Batch update all references
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync("refs/heads/feature-a", currentRefs["refs/heads/feature-a"], commit1);
            await locks.WriteReferenceWithValidationAsync("refs/heads/feature-b", currentRefs["refs/heads/feature-b"], commit2);
            await locks.WriteReferenceWithValidationAsync("refs/heads/feature-c", currentRefs["refs/heads/feature-c"], commit3);
        }

        // Assert
        gitRepository.InvalidateCaches();
        var updatedRefs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit1, updatedRefs["refs/heads/feature-a"]);
        Assert.Equal(commit2, updatedRefs["refs/heads/feature-b"]);
        Assert.Equal(commit3, updatedRefs["refs/heads/feature-c"]);
    }

    [Fact]
    public async Task BatchUpdate_MixedOperations_AllSucceed()
    {
        // Arrange - Test create, update, and delete in one batch
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file2.txt", "content2"));

        repo.RunGit("branch to-update");
        repo.RunGit("branch to-delete");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var refs = new[]
        {
            "refs/heads/to-update",
            "refs/heads/to-delete",
            "refs/heads/to-create"
        };

        var currentRefs = await gitRepository.GetReferencesAsync();

        // Act
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            // Update existing branch
            await locks.WriteReferenceWithValidationAsync("refs/heads/to-update", currentRefs["refs/heads/to-update"], commit1);

            // Delete existing branch
            await locks.WriteReferenceWithValidationAsync("refs/heads/to-delete", currentRefs["refs/heads/to-delete"], null);

            // Create new branch
            await locks.WriteReferenceWithValidationAsync("refs/heads/to-create", null, commit2);
        }

        // Assert
        gitRepository.InvalidateCaches();
        var updatedRefs = await gitRepository.GetReferencesAsync();

        Assert.Equal(commit1, updatedRefs["refs/heads/to-update"]);
        Assert.False(updatedRefs.ContainsKey("refs/heads/to-delete"));
        Assert.Equal(commit2, updatedRefs["refs/heads/to-create"]);
    }

    [Fact]
    public async Task BatchUpdate_PartialFailure_OnlySuccessfulUpdatesApplied()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file2.txt", "content2"));

        repo.RunGit("branch branch-a");
        repo.RunGit("branch branch-b");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var refs = new[] { "refs/heads/branch-a", "refs/heads/branch-b" };
        var currentRefs = await gitRepository.GetReferencesAsync();
        var fakeOldHash = new GitHash("1234567890123456789012345678901234567890");

        // Act - First update succeeds, second fails
        Exception? caughtException = null;
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            // This should succeed
            await locks.WriteReferenceWithValidationAsync("refs/heads/branch-a", currentRefs["refs/heads/branch-a"], commit1);

            try
            {
                // This should fail due to mismatched old value
                await locks.WriteReferenceWithValidationAsync("refs/heads/branch-b", fakeOldHash, commit2);
            }
            catch (InvalidOperationException ex)
            {
                caughtException = ex;
            }
        }

        // Assert
        Assert.NotNull(caughtException);

        gitRepository.InvalidateCaches();
        var updatedRefs = await gitRepository.GetReferencesAsync();

        // First update should have been applied
        Assert.Equal(commit1, updatedRefs["refs/heads/branch-a"]);

        // Second update should not have been applied
        Assert.Equal(currentRefs["refs/heads/branch-b"], updatedRefs["refs/heads/branch-b"]);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentBatchUpdates_NonOverlapping_BothSucceed()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Commit", ("file.txt", "content"));
        repo.RunGit("branch group1-a");
        repo.RunGit("branch group1-b");
        repo.RunGit("branch group2-a");
        repo.RunGit("branch group2-b");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var group1Refs = new[] { "refs/heads/group1-a", "refs/heads/group1-b" };
        var group2Refs = new[] { "refs/heads/group2-a", "refs/heads/group2-b" };

        var startSignal = new TaskCompletionSource<bool>();
        var results = new List<string>();
        var lockObject = new object();

        // Act - Two non-overlapping batch operations
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(group1Refs))
            {
                lock (lockObject) { results.Add("task1-start"); }
                await Task.Delay(50);
                lock (lockObject) { results.Add("task1-end"); }
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(group2Refs))
            {
                lock (lockObject) { results.Add("task2-start"); }
                await Task.Delay(50);
                lock (lockObject) { results.Add("task2-end"); }
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Both should complete without waiting for each other
        Assert.Equal(4, results.Count);
    }

    [Fact]
    public async Task ConcurrentBatchUpdates_Overlapping_Serialized()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Commit", ("file.txt", "content"));
        repo.RunGit("branch shared");
        repo.RunGit("branch exclusive1");
        repo.RunGit("branch exclusive2");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        var batch1Refs = new[] { "refs/heads/shared", "refs/heads/exclusive1" };
        var batch2Refs = new[] { "refs/heads/shared", "refs/heads/exclusive2" };

        var startSignal = new TaskCompletionSource<bool>();
        var operations = new List<string>();
        var lockObject = new object();

        // Act - Both batches try to lock "shared"
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(batch1Refs))
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
            using (await gitRepository.AcquireMultipleReferenceLocksAsync(batch2Refs))
            {
                lock (lockObject) { operations.Add("batch2-start"); }
                await Task.Delay(50);
                lock (lockObject) { operations.Add("batch2-end"); }
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - One batch must complete before the other starts
        Assert.Equal(4, operations.Count);
        var batch1Start = operations.IndexOf("batch1-start");
        var batch1End = operations.IndexOf("batch1-end");
        var batch2Start = operations.IndexOf("batch2-start");
        var batch2End = operations.IndexOf("batch2-end");

        var batch1First = batch1End < batch2Start;
        var batch2First = batch2End < batch1Start;
        Assert.True(batch1First || batch2First, "Overlapping batches should be serialized");
    }

    [Fact]
    public async Task StressTest_ManyConcurrentBatchUpdates()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Initial", ("file.txt", "content"));

        // Create 10 branches
        for (int i = 0; i < 10; i++)
        {
            repo.RunGit($"branch branch-{i}");
        }

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var iterations = 20;
        var concurrentTasks = 5;

        var allRefs = Enumerable.Range(0, 10).Select(i => $"refs/heads/branch-{i}").ToArray();

        // Act - Multiple threads doing batch operations
        var tasks = Enumerable.Range(0, concurrentTasks).Select(taskId => Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                // Each iteration locks a random subset of branches
                var subset = allRefs.OrderBy(_ => Random.Shared.Next()).Take(3).ToArray();

                using (await gitRepository.AcquireMultipleReferenceLocksAsync(subset))
                {
                    // Simulate some work
                    await Task.Delay(1);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert - Should complete without deadlock
        Assert.True(true);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task MultipleDispose_IsSafe()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        repo.Commit("Commit", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var refs = new[] { headRef };

        var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs);

        // Act & Assert - Multiple dispose should not throw
        locks.Dispose();
        locks.Dispose();
        locks.Dispose();
    }

    [Fact]
    public async Task EmptyLockSet_Succeeds()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);

        // Act & Assert - Should not throw
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(Array.Empty<string>()))
        {
            Assert.NotNull(locks);
        }
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_NormalizesReferencePath()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Commit", ("file.txt", "content"));
        repo.RunGit("branch feature");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        // Lock with normalized path
        var normalizedRef = "refs/heads/feature";
        var refs = new[] { normalizedRef };

        var currentRefs = await gitRepository.GetReferencesAsync();
        var currentValue = currentRefs[normalizedRef];

        // Act - Write with path that has extra whitespace
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync("  refs/heads/feature  ", currentValue, commit);
        }

        // Assert
        gitRepository.InvalidateCaches();
        var updatedRefs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit, updatedRefs[normalizedRef]);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_WithInvalidReferencePath_ThrowsArgumentException()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit = repo.Commit("Commit", ("file.txt", "content"));
        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var refs = new[] { headRef };

        // Act & Assert
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await locks.WriteReferenceWithValidationAsync("main", null, commit)); // Not a fully qualified ref
        }
    }

    #endregion

    #region Cache Invalidation Tests

    [Fact]
    public async Task WriteReferenceWithValidationAsync_InvalidatesCache()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file.txt", "content2"));

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var headRef = GitTestHelper.GetHeadReference(repo);
        var refs = new[] { headRef };

        // Cache current references
        var beforeRefs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit2, beforeRefs[headRef]);

        // Act
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync(headRef, commit2, commit1);
        }

        // Assert - Should see updated value without manual invalidation
        var afterRefs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit1, afterRefs[headRef]);
    }

    [Fact]
    public async Task BatchUpdate_EachUpdateInvalidatesCache()
    {
        // Arrange
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Commit("First", ("file1.txt", "content1"));
        var commit2 = repo.Commit("Second", ("file2.txt", "content2"));

        repo.RunGit("branch branch-a");
        repo.RunGit("branch branch-b");

        var gitRepository = GitRepository.Open(repo.WorkingDirectory);
        gitRepository.InvalidateCaches();

        var refs = new[] { "refs/heads/branch-a", "refs/heads/branch-b" };
        var currentRefs = await gitRepository.GetReferencesAsync();

        // Act
        using (var locks = await gitRepository.AcquireMultipleReferenceLocksAsync(refs))
        {
            await locks.WriteReferenceWithValidationAsync("refs/heads/branch-a", currentRefs["refs/heads/branch-a"], commit1);

            // Read references after first update - should see the change
            var midRefs = await gitRepository.GetReferencesAsync();
            Assert.Equal(commit1, midRefs["refs/heads/branch-a"]);

            await locks.WriteReferenceWithValidationAsync("refs/heads/branch-b", currentRefs["refs/heads/branch-b"], commit2);
        }

        // Assert - Final state
        var finalRefs = await gitRepository.GetReferencesAsync();
        Assert.Equal(commit1, finalRefs["refs/heads/branch-a"]);
        Assert.Equal(commit2, finalRefs["refs/heads/branch-b"]);
    }

    #endregion

    public void Dispose()
    {
        GitTestHelper.TryDeleteDirectory(_tempDirectory);
    }
}
