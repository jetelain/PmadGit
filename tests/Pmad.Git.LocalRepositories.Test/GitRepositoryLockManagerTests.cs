using System.Diagnostics;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitRepositoryLockManager to verify lock behavior and concurrency protection.
/// </summary>
public sealed class GitRepositoryLockManagerTests
{
    [Fact]
    public async Task AcquireReferenceLockAsync_CanAcquireAndRelease()
    {
        // Arrange
        var lockManager = CreateLockManager();

        // Act
        var lockHandle = await lockManager.AcquireReferenceLockAsync("refs/heads/main");

        // Assert
        Assert.NotNull(lockHandle);
        lockHandle.Dispose();
    }

    [Fact]
    public async Task AcquireReferenceLockAsync_SerializesAccessToSameReference()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var executionOrder = new List<int>();
        var startSignal = new TaskCompletionSource<bool>();

        // Act
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await lockManager.AcquireReferenceLockAsync("refs/heads/main"))
            {
                executionOrder.Add(1);
                await Task.Delay(50);
                executionOrder.Add(1);
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            await Task.Delay(10); // Ensure task1 gets lock first
            using (await lockManager.AcquireReferenceLockAsync("refs/heads/main"))
            {
                executionOrder.Add(2);
                await Task.Delay(50);
                executionOrder.Add(2);
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Task 1 should complete entirely before Task 2 starts
        Assert.Equal(new[] { 1, 1, 2, 2 }, executionOrder);
    }

    [Fact]
    public async Task AcquireReferenceLockAsync_AllowsParallelAccessToDifferentReferences()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var concurrentAccess = new List<(int thread, DateTimeOffset time)>();
        var startSignal = new TaskCompletionSource<bool>();
        var lockObject = new object();

        // Act
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await lockManager.AcquireReferenceLockAsync("refs/heads/main"))
            {
                lock (lockObject) { concurrentAccess.Add((1, DateTimeOffset.UtcNow)); }
                await Task.Delay(100);
                lock (lockObject) { concurrentAccess.Add((1, DateTimeOffset.UtcNow)); }
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await lockManager.AcquireReferenceLockAsync("refs/heads/feature"))
            {
                lock (lockObject) { concurrentAccess.Add((2, DateTimeOffset.UtcNow)); }
                await Task.Delay(100);
                lock (lockObject) { concurrentAccess.Add((2, DateTimeOffset.UtcNow)); }
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Both threads should have started before either finished
        var thread1Start = concurrentAccess.First(x => x.thread == 1).time;
        var thread2Start = concurrentAccess.First(x => x.thread == 2).time;
        var thread1End = concurrentAccess.Last(x => x.thread == 1).time;
        var thread2End = concurrentAccess.Last(x => x.thread == 2).time;

        var overlap = (thread1Start < thread2End && thread2Start < thread1End);
        Assert.True(overlap, "Different references should allow parallel access");
    }

    [Fact]
    public async Task AcquireReferenceLockAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var lockManager = CreateLockManager();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await lockManager.AcquireReferenceLockAsync("refs/heads/main", cts.Token));
    }

    [Fact]
    public async Task AcquireReferenceLockAsync_MultipleTimes_WorksCorrectly()
    {
        // Arrange
        var lockManager = CreateLockManager();

        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            using (await lockManager.AcquireReferenceLockAsync("refs/heads/main"))
            {
                // Lock acquired successfully
            }
            // Lock released
        }
    }

    [Fact]
    public async Task LockHandles_AreReusable()
    {
        // Arrange
        var lockManager = CreateLockManager();

        // Act
        var handle1 = await lockManager.AcquireReferenceLockAsync("refs/heads/main");
        handle1.Dispose();

        var handle2 = await lockManager.AcquireReferenceLockAsync("refs/heads/main");
        handle2.Dispose();

        // Assert - No exception means locks work correctly
        Assert.True(true);
    }

    [Fact]
    public async Task MultipleDispose_IsSafe()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var handle = await lockManager.AcquireReferenceLockAsync("refs/heads/main");

        // Act & Assert - Multiple dispose should not throw
        handle.Dispose();
        handle.Dispose();
        handle.Dispose();
    }

    [Fact]
    public async Task StressTest_ManyThreadsOnSameReference()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var counter = 0;
        var iterations = 100;
        var concurrentTasks = 10;

        // Act
        var tasks = Enumerable.Range(0, concurrentTasks).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                using (await lockManager.AcquireReferenceLockAsync("refs/heads/main"))
                {
                    Interlocked.Increment(ref counter);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(iterations * concurrentTasks, counter);
    }

    [Fact]
    public async Task StressTest_ManyThreadsOnDifferentReferences()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var counters = new int[10];
        var iterations = 100;

        // Act
        var tasks = Enumerable.Range(0, 10).Select(branchIndex => Task.Run(async () =>
        {
            var refName = $"refs/heads/branch{branchIndex}";
            for (int i = 0; i < iterations; i++)
            {
                using (await lockManager.AcquireReferenceLockAsync(refName))
                {
                    Interlocked.Increment(ref counters[branchIndex]);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(counters, c => Assert.Equal(iterations, c));
    }

    #region Multiple Reference Locks Tests

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithSingleReference_AcquiresLock()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var refs = new[] { "refs/heads/main" };

        // Act
        var lockHandle = await lockManager.AcquireMultipleReferenceLocksAsync(refs);

        // Assert
        Assert.NotNull(lockHandle);
        lockHandle.Dispose();
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithMultipleReferences_AcquiresAllLocks()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var refs = new[] { "refs/heads/main", "refs/heads/feature", "refs/heads/develop" };

        // Act
        using var lockHandle = await lockManager.AcquireMultipleReferenceLocksAsync(refs);

        // Assert - Should acquire successfully
        Assert.NotNull(lockHandle);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithDuplicates_DeduplicatesAndAcquiresOnce()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var refs = new[] { "refs/heads/main", "refs/heads/feature", "refs/heads/main", "refs/heads/feature" };

        // Act - Should not deadlock
        using var lockHandle = await lockManager.AcquireMultipleReferenceLocksAsync(refs);

        // Assert - Should complete without deadlock
        Assert.NotNull(lockHandle);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithEmptyList_ReturnsValidHandle()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var refs = Array.Empty<string>();

        // Act
        var lockHandle = await lockManager.AcquireMultipleReferenceLocksAsync(refs);

        // Assert
        Assert.NotNull(lockHandle);
        lockHandle.Dispose(); // Should not throw
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_AcquiresInSortedOrder()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var acquisitionOrder = new List<string>();
        var lockObject = new object();
        
        // Create a scenario where we can observe lock acquisition order
        var refs1 = new[] { "refs/heads/zebra", "refs/heads/alpha", "refs/heads/mike" };
        var refs2 = new[] { "refs/heads/mike", "refs/heads/zebra", "refs/heads/alpha" };
        
        var startSignal = new TaskCompletionSource<bool>();

        // Act - Both should complete without deadlock due to consistent ordering
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await lockManager.AcquireMultipleReferenceLocksAsync(refs1))
            {
                lock (lockObject) { acquisitionOrder.Add("task1"); }
                await Task.Delay(50);
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            await Task.Delay(10);
            using (await lockManager.AcquireMultipleReferenceLocksAsync(refs2))
            {
                lock (lockObject) { acquisitionOrder.Add("task2"); }
                await Task.Delay(50);
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Both tasks completed (no deadlock)
        Assert.Equal(2, acquisitionOrder.Count);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_SerializesOverlappingLockSets()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var executionOrder = new List<string>();
        var lockObject = new object();
        var startSignal = new TaskCompletionSource<bool>();

        // Two lock sets that overlap
        var refs1 = new[] { "refs/heads/main", "refs/heads/feature1" };
        var refs2 = new[] { "refs/heads/main", "refs/heads/feature2" };

        // Act
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await lockManager.AcquireMultipleReferenceLocksAsync(refs1))
            {
                lock (lockObject) { executionOrder.Add("task1-start"); }
                await Task.Delay(50);
                lock (lockObject) { executionOrder.Add("task1-end"); }
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            await Task.Delay(10);
            using (await lockManager.AcquireMultipleReferenceLocksAsync(refs2))
            {
                lock (lockObject) { executionOrder.Add("task2-start"); }
                await Task.Delay(50);
                lock (lockObject) { executionOrder.Add("task2-end"); }
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Operations should be serialized because they share "main"
        Assert.Equal(4, executionOrder.Count);
        var task1StartIdx = executionOrder.IndexOf("task1-start");
        var task1EndIdx = executionOrder.IndexOf("task1-end");
        var task2StartIdx = executionOrder.IndexOf("task2-start");
        var task2EndIdx = executionOrder.IndexOf("task2-end");

        // One task must complete before the other starts
        var task1First = task1EndIdx < task2StartIdx;
        var task2First = task2EndIdx < task1StartIdx;
        Assert.True(task1First || task2First, "Operations with overlapping locks should be serialized");
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_AllowsParallelNonOverlappingLockSets()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var timestamps = new List<(string task, DateTimeOffset time)>();
        var lockObject = new object();
        var startSignal = new TaskCompletionSource<bool>();

        // Two lock sets that don't overlap
        var refs1 = new[] { "refs/heads/feature1", "refs/heads/feature2" };
        var refs2 = new[] { "refs/heads/feature3", "refs/heads/feature4" };

        // Act
        var task1 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await lockManager.AcquireMultipleReferenceLocksAsync(refs1))
            {
                lock (lockObject) { timestamps.Add(("task1-start", DateTimeOffset.UtcNow)); }
                await Task.Delay(100);
                lock (lockObject) { timestamps.Add(("task1-end", DateTimeOffset.UtcNow)); }
            }
        });

        var task2 = Task.Run(async () =>
        {
            await startSignal.Task;
            using (await lockManager.AcquireMultipleReferenceLocksAsync(refs2))
            {
                lock (lockObject) { timestamps.Add(("task2-start", DateTimeOffset.UtcNow)); }
                await Task.Delay(100);
                lock (lockObject) { timestamps.Add(("task2-end", DateTimeOffset.UtcNow)); }
            }
        });

        startSignal.SetResult(true);
        await Task.WhenAll(task1, task2);

        // Assert - Operations should overlap (run in parallel)
        var task1Start = timestamps.First(t => t.task == "task1-start").time;
        var task1End = timestamps.First(t => t.task == "task1-end").time;
        var task2Start = timestamps.First(t => t.task == "task2-start").time;
        var task2End = timestamps.First(t => t.task == "task2-end").time;

        var overlap = (task1Start < task2End && task2Start < task1End);
        Assert.True(overlap, "Non-overlapping lock sets should allow parallel execution");
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var lockManager = CreateLockManager();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var refs = new[] { "refs/heads/main", "refs/heads/feature" };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await lockManager.AcquireMultipleReferenceLocksAsync(refs, cts.Token));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_CancellationDuringAcquisition_ReleasesAcquiredLocks()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var refs = new[] { "refs/heads/ref1", "refs/heads/ref2" };
        
        // Hold one of the locks to force blocking
        using var blockingLock = await lockManager.AcquireReferenceLockAsync("refs/heads/ref2");
        
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Cancel after 100ms
        
        // Act - Try to acquire all locks, should eventually be cancelled
        var lockTask = lockManager.AcquireMultipleReferenceLocksAsync(refs, cts.Token);
        
        // Should throw due to cancellation
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await lockTask);
        
        // Release the blocking lock
        blockingLock.Dispose();
        
        // Assert - Verify locks were released by successfully acquiring them
        using var verifyLock = await lockManager.AcquireMultipleReferenceLocksAsync(refs);
        Assert.NotNull(verifyLock);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_MultipleDispose_IsSafe()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var refs = new[] { "refs/heads/main", "refs/heads/feature" };
        var handle = await lockManager.AcquireMultipleReferenceLocksAsync(refs);

        // Act & Assert - Multiple dispose should not throw
        handle.Dispose();
        handle.Dispose();
        handle.Dispose();
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_StressTest_ManyConcurrentBatchOperations()
    {
        // Arrange
        var lockManager = CreateLockManager();
        var sharedCounter = 0;
        var iterations = 20;
        var concurrentBatches = 10;
        var refs = new[] { "refs/heads/shared1", "refs/heads/shared2", "refs/heads/shared3" };

        // Act - Multiple threads trying to acquire same batch of locks
        var tasks = Enumerable.Range(0, concurrentBatches).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < iterations; i++)
            {
                using (await lockManager.AcquireMultipleReferenceLocksAsync(refs))
                {
                    // Critical section - increment counter
                    var temp = sharedCounter;
                    await Task.Yield(); // Force context switch
                    sharedCounter = temp + 1;
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert - Counter should match expected value (proves serialization)
        Assert.Equal(iterations * concurrentBatches, sharedCounter);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithNullReferencePaths_ThrowsArgumentNullException()
    {
        // Arrange
        var lockManager = CreateLockManager();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await lockManager.AcquireMultipleReferenceLocksAsync(null!));
    }

    #endregion

    private static GitRepositoryLockManager CreateLockManager()
    {
        // Use reflection to create an instance since the class is internal
        var type = typeof(GitRepository).Assembly.GetType("Pmad.Git.LocalRepositories.GitRepositoryLockManager");
        Assert.NotNull(type);
        return (GitRepositoryLockManager)Activator.CreateInstance(type, true)!;
    }
}
