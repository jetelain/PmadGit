using System.Text;
using Pmad.Git.LocalRepositories.Test.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for GitReferenceStore covering reference resolution, reads, writes, cache management,
/// locking, and packed-refs support.
/// </summary>
public sealed class GitReferenceStoreTests
{
    #region GetReferencesAsync

    [Fact]
    public async Task GetReferencesAsync_AfterInit_ReturnsHeadBranch()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        var refs = await store.GetReferencesAsync();

        var headRef = GitTestHelper.GetHeadReference(repo);
        Assert.True(refs.ContainsKey(headRef));
        Assert.Equal(repo.Head, refs[headRef]);
    }

    [Fact]
    public async Task GetReferencesAsync_WithTag_IncludesTag()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("tag v1.0");
        var store = new GitReferenceStore(repo.GitDirectory);

        var refs = await store.GetReferencesAsync();

        Assert.True(refs.ContainsKey("refs/tags/v1.0"));
        Assert.Equal(repo.Head, refs["refs/tags/v1.0"]);
    }

    [Fact]
    public async Task GetReferencesAsync_WithMultipleBranches_ReturnsAll()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("branch feature-a");
        repo.RunGit("branch feature-b");
        var store = new GitReferenceStore(repo.GitDirectory);

        var refs = await store.GetReferencesAsync();

        Assert.True(refs.ContainsKey("refs/heads/feature-a"));
        Assert.True(refs.ContainsKey("refs/heads/feature-b"));
    }

    [Fact]
    public async Task GetReferencesAsync_ReturnsSnapshot_NotAffectedByLaterChanges()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        var refs1 = await store.GetReferencesAsync();
        repo.RunGit("branch new-branch");
        var refs2 = await store.GetReferencesAsync(); // cache not yet invalidated

        Assert.False(refs1.ContainsKey("refs/heads/new-branch"));
        Assert.False(refs2.ContainsKey("refs/heads/new-branch")); // still cached
    }

    [Fact]
    public async Task GetReferencesAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.GetReferencesAsync(cts.Token));
    }

    [Fact]
    public async Task GetReferencesAsync_WithPackedRefs_ReturnsPackedReference()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("gc --aggressive --prune=now");
        var store = new GitReferenceStore(repo.GitDirectory);

        var refs = await store.GetReferencesAsync();

        var headRef = GitTestHelper.GetHeadReference(repo);
        Assert.True(refs.ContainsKey(headRef));
        Assert.Equal(repo.Head, refs[headRef]);
    }

    #endregion

    #region TryResolveReferenceAsync

    [Fact]
    public async Task TryResolveReferenceAsync_WithExistingRef_ReturnsHash()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        var result = await store.TryResolveReferenceAsync(headRef);

        Assert.NotNull(result);
        Assert.Equal(repo.Head, result.Value);
    }

    [Fact]
    public async Task TryResolveReferenceAsync_WithNonExistentRef_ReturnsNull()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        var result = await store.TryResolveReferenceAsync("refs/heads/does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveReferenceAsync_WithBackslashes_NormalizesAndResolves()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var withBackslashes = headRef.Replace('/', '\\');

        var result = await store.TryResolveReferenceAsync(withBackslashes);

        Assert.NotNull(result);
        Assert.Equal(repo.Head, result.Value);
    }

    [Fact]
    public async Task TryResolveReferenceAsync_WithTag_ReturnsTagHash()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("tag v2.0");
        var store = new GitReferenceStore(repo.GitDirectory);

        var result = await store.TryResolveReferenceAsync("refs/tags/v2.0");

        Assert.NotNull(result);
        Assert.Equal(repo.Head, result.Value);
    }

    [Fact]
    public async Task TryResolveReferenceAsync_AfterInvalidateAndExternalChange_ReturnsUpdatedHash()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);

        var before = await store.TryResolveReferenceAsync(headRef);

        var newCommit = repo.Commit("Extra commit", ("extra.txt", "data"));
        store.InvalidateCaches();

        var after = await store.TryResolveReferenceAsync(headRef);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before.Value, after.Value);
        Assert.Equal(newCommit, after.Value);
    }

    #endregion

    #region ResolveHeadAsync

    [Fact]
    public async Task ResolveHeadAsync_WithSymbolicRef_ReturnsCommitHash()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        var head = await store.ResolveHeadAsync();

        Assert.Equal(repo.Head, head);
    }

    [Fact]
    public async Task ResolveHeadAsync_WithDetachedHead_ReturnsCommitHash()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit($"checkout --detach {repo.Head.Value}");
        var store = new GitReferenceStore(repo.GitDirectory);

        var head = await store.ResolveHeadAsync();

        Assert.Equal(repo.Head, head);
    }

    [Fact]
    public async Task ResolveHeadAsync_WithMissingHeadFile_ThrowsFileNotFoundException()
    {
        using var repo = GitTestRepository.Create();
        var headPath = Path.Combine(repo.GitDirectory, "HEAD");
        File.Delete(headPath);
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => store.ResolveHeadAsync());
    }

    [Fact]
    public async Task ResolveHeadAsync_WithInvalidHeadContent_ThrowsInvalidDataException()
    {
        using var repo = GitTestRepository.Create();
        var headPath = Path.Combine(repo.GitDirectory, "HEAD");
        File.WriteAllText(headPath, "not-a-valid-hash-or-ref");
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.ResolveHeadAsync());
    }

    [Fact]
    public async Task ResolveHeadAsync_WithSymrefToMissingBranch_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var headPath = Path.Combine(repo.GitDirectory, "HEAD");
        File.WriteAllText(headPath, "ref: refs/heads/nonexistent\n");
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ResolveHeadAsync());
    }

    #endregion

    #region WriteReferenceWithValidationAsync

    [Fact]
    public async Task WriteReferenceWithValidationAsync_CreateNew_Succeeds()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var commit = repo.Head;

        await store.WriteReferenceWithValidationAsync("refs/heads/new-branch", null, commit);

        store.InvalidateCaches();
        var result = await store.TryResolveReferenceAsync("refs/heads/new-branch");
        Assert.NotNull(result);
        Assert.Equal(commit, result.Value);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_UpdateExisting_Succeeds()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Head;
        var commit2 = repo.Commit("Second", ("file2.txt", "content2"));
        var headRef = GitTestHelper.GetHeadReference(repo);
        var store = new GitReferenceStore(repo.GitDirectory);
        store.InvalidateCaches();

        await store.WriteReferenceWithValidationAsync(headRef, commit2, commit1);

        store.InvalidateCaches();
        var result = await store.TryResolveReferenceAsync(headRef);
        Assert.NotNull(result);
        Assert.Equal(commit1, result.Value);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_DeleteExisting_Succeeds()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("branch to-delete");
        var store = new GitReferenceStore(repo.GitDirectory);
        store.InvalidateCaches();
        var currentValue = await store.TryResolveReferenceAsync("refs/heads/to-delete");

        await store.WriteReferenceWithValidationAsync("refs/heads/to-delete", currentValue, null);

        store.InvalidateCaches();
        var result = await store.TryResolveReferenceAsync("refs/heads/to-delete");
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_CreateAlreadyExisting_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var headRef = GitTestHelper.GetHeadReference(repo);
        var currentValue = await store.TryResolveReferenceAsync(headRef);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.WriteReferenceWithValidationAsync(headRef, null, currentValue));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_UpdateWithWrongExpected_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Head;
        var commit2 = repo.Commit("Other", ("other.txt", "x"));
        var headRef = GitTestHelper.GetHeadReference(repo);
        var store = new GitReferenceStore(repo.GitDirectory);
        store.InvalidateCaches();

        // Provide commit1 as expected old value, but HEAD is now commit2
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.WriteReferenceWithValidationAsync(headRef, commit1, commit1));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_DeleteNonExistent_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.WriteReferenceWithValidationAsync("refs/heads/ghost", repo.Head, null));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_InvalidatesCache()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var commit = repo.Head;

        // Prime the cache
        await store.GetReferencesAsync();

        await store.WriteReferenceWithValidationAsync("refs/heads/cache-test", null, commit);

        // No explicit InvalidateCaches call — WriteReferenceWithValidationAsync should have done it
        var refs = await store.GetReferencesAsync();
        Assert.True(refs.ContainsKey("refs/heads/cache-test"));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_WithRelativePath_ThrowsArgumentException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteReferenceWithValidationAsync("heads/main", null, repo.Head));
    }

    [Fact]
    public async Task WriteReferenceWithValidationAsync_WithEmptyPath_ThrowsArgumentException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteReferenceWithValidationAsync("", null, repo.Head));
    }

    #endregion

    #region AcquireMultipleReferenceLocksAsync

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_AllowsWriteOnLockedRef()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Head;
        var commit2 = repo.Commit("Commit2", ("b.txt", "b"));
        var headRef = GitTestHelper.GetHeadReference(repo);
        var store = new GitReferenceStore(repo.GitDirectory);
        store.InvalidateCaches();

        using (var locks = await store.AcquireMultipleReferenceLocksAsync(new[] { headRef }))
        {
            await locks.WriteReferenceWithValidationAsync(headRef, commit2, commit1);
        }

        store.InvalidateCaches();
        var result = await store.TryResolveReferenceAsync(headRef);
        Assert.Equal(commit1, result);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_RejectsWriteOnUnlockedRef()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("branch other");
        var headRef = GitTestHelper.GetHeadReference(repo);
        var store = new GitReferenceStore(repo.GitDirectory);

        using var locks = await store.AcquireMultipleReferenceLocksAsync(new[] { headRef });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => locks.WriteReferenceWithValidationAsync("refs/heads/other", null, repo.Head));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithNullPaths_ThrowsArgumentNullException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.AcquireMultipleReferenceLocksAsync(null!));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithRelativePath_ThrowsArgumentException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.AcquireMultipleReferenceLocksAsync(new[] { "heads/main" }));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var repo = GitTestRepository.Create();
        var headRef = GitTestHelper.GetHeadReference(repo);
        var store = new GitReferenceStore(repo.GitDirectory);

        var locks = await store.AcquireMultipleReferenceLocksAsync(new[] { headRef });
        locks.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => locks.WriteReferenceWithValidationAsync(headRef, null, repo.Head));
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_MultipleLocks_AllowsAllLockedRefs()
    {
        using var repo = GitTestRepository.Create();
        var commit = repo.Head;
        repo.RunGit("branch branch-a");
        repo.RunGit("branch branch-b");
        var store = new GitReferenceStore(repo.GitDirectory);
        store.InvalidateCaches();

        using (var locks = await store.AcquireMultipleReferenceLocksAsync(
                   new[] { "refs/heads/branch-a", "refs/heads/branch-b" }))
        {
            var currentA = await store.TryResolveReferenceAsync("refs/heads/branch-a");
            var currentB = await store.TryResolveReferenceAsync("refs/heads/branch-b");

            await locks.WriteReferenceWithValidationAsync("refs/heads/branch-a", currentA, commit);
            await locks.WriteReferenceWithValidationAsync("refs/heads/branch-b", currentB, commit);
        }

        store.InvalidateCaches();
        var refs = await store.GetReferencesAsync();
        Assert.Equal(commit, refs["refs/heads/branch-a"]);
        Assert.Equal(commit, refs["refs/heads/branch-b"]);
    }

    [Fact]
    public async Task AcquireMultipleReferenceLocksAsync_WithCancellationBeforeAcquire_ThrowsOperationCanceledException()
    {
        using var repo = GitTestRepository.Create();
        var headRef = GitTestHelper.GetHeadReference(repo);
        var store = new GitReferenceStore(repo.GitDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.AcquireMultipleReferenceLocksAsync(new[] { headRef }, cts.Token));
    }

    #endregion

    #region InvalidateCaches

    [Fact]
    public async Task InvalidateCaches_AfterExternalChange_ReflectsNewState()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        // Prime the cache
        var refsBefore = await store.GetReferencesAsync();
        Assert.False(refsBefore.ContainsKey("refs/heads/externally-added"));

        // Simulate an external change
        repo.RunGit("branch externally-added");

        store.InvalidateCaches();
        var refsAfter = await store.GetReferencesAsync();

        Assert.True(refsAfter.ContainsKey("refs/heads/externally-added"));
    }

    [Fact]
    public async Task InvalidateCaches_IsIdempotent()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        store.InvalidateCaches();
        store.InvalidateCaches();
        store.InvalidateCaches();

        var refs = await store.GetReferencesAsync();
        var headRef = GitTestHelper.GetHeadReference(repo);
        Assert.True(refs.ContainsKey(headRef));
    }

    #endregion

    #region NormalizeAbsoluteReferencePath

    [Theory]
    [InlineData("refs/heads/main", "refs/heads/main")]
    [InlineData("refs/heads/main  ", "refs/heads/main")]
    [InlineData("  refs/heads/main", "refs/heads/main")]
    [InlineData("refs\\heads\\main", "refs/heads/main")]
    [InlineData("refs/tags/v1.0", "refs/tags/v1.0")]
    public void NormalizeAbsoluteReferencePath_ValidInput_ReturnsNormalized(string input, string expected)
    {
        var result = GitReferenceStore.NormalizeAbsoluteReferencePath(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeAbsoluteReferencePath_EmptyOrWhitespace_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(
            () => GitReferenceStore.NormalizeAbsoluteReferencePath(input));
    }

    [Theory]
    [InlineData("heads/main")]
    [InlineData("main")]
    [InlineData("HEAD")]
    public void NormalizeAbsoluteReferencePath_WithoutRefsPrefix_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(
            () => GitReferenceStore.NormalizeAbsoluteReferencePath(input));
    }

    #endregion

    #region Packed-refs

    [Fact]
    public async Task GetReferencesAsync_PackedRefOverwritesLooseRefInCache()
    {
        using var repo = GitTestRepository.Create();
        var commit1 = repo.Head;
        var commit2 = repo.Commit("Second", ("second.txt", "y"));
        var headRef = GitTestHelper.GetHeadReference(repo);

        // Pack all refs (will include commit2 as head)
        repo.RunGit("gc --aggressive --prune=now");

        // Write a loose ref pointing to commit1
        var refPath = Path.Combine(repo.GitDirectory, headRef.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(refPath)!);
        File.WriteAllText(refPath, commit1.Value + "\n");

        var store = new GitReferenceStore(repo.GitDirectory);
        var refs = await store.GetReferencesAsync();

        // The ref must be present (either loose or packed value)
        Assert.True(refs.ContainsKey(headRef));
    }

    [Fact]
    public async Task GetReferencesAsync_WithAnnotatedTagInPackedRefs_SkipsPeeledLine()
    {
        using var repo = GitTestRepository.Create();
        // Create an annotated tag (produces a ^{} peeled line in packed-refs)
        repo.RunGit("tag -a v3.0 -m \"annotated tag\"");
        repo.RunGit("gc --aggressive --prune=now");

        var store = new GitReferenceStore(repo.GitDirectory);
        var refs = await store.GetReferencesAsync();

        // The tag itself should resolve; the peeled ^{} line should be ignored
        Assert.True(refs.ContainsKey("refs/tags/v3.0"));
    }

    #endregion
}
