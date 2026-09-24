using System.IO;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitReferenceStoreCycleAndConflictTests
{
    [Fact]
    public async Task TryResolveReferenceAsync_CircularSymbolicRef_TwoNodeCycle_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        var refADir = Path.Combine(repo.GitDirectory, "refs", "heads");
        Directory.CreateDirectory(refADir);
        File.WriteAllText(Path.Combine(refADir, "a"), "ref: refs/heads/b\n");
        File.WriteAllText(Path.Combine(refADir, "b"), "ref: refs/heads/a\n");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TryResolveReferenceAsync("refs/heads/a"));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryResolveReferenceAsync_CircularSymbolicRef_HeadToAToBToA_ThrowsInvalidOperationException()
    {
        // Testing HEAD -> refs/heads/a -> refs/heads/b -> refs/heads/a
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);

        var refADir = Path.Combine(repo.GitDirectory, "refs", "heads");
        Directory.CreateDirectory(refADir);
        File.WriteAllText(Path.Combine(refADir, "a"), "ref: refs/heads/b\n");
        File.WriteAllText(Path.Combine(refADir, "b"), "ref: refs/heads/a\n");
        File.WriteAllText(Path.Combine(repo.GitDirectory, "HEAD"), "ref: refs/heads/a\n");

        var exHead = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ResolveHeadAsync());
        Assert.Contains("cycle", exHead.Message, StringComparison.OrdinalIgnoreCase);

        var exRef = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TryResolveReferenceAsync("HEAD"));
        Assert.Contains("cycle", exRef.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReferenceAsync_DirectoryFileConflict_ChildUnderExistingFile_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var commit = repo.Head;

        await store.CreateReferenceAsync("refs/heads/feature", commit);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateReferenceAsync("refs/heads/feature/task", commit));
        Assert.Contains("feature", ex.Message);
    }

    [Fact]
    public async Task CreateReferenceAsync_DirectoryFileConflict_ParentOverExistingChild_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var commit = repo.Head;

        await store.CreateReferenceAsync("refs/heads/feature/task", commit);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateReferenceAsync("refs/heads/feature", commit));
        Assert.Contains("feature", ex.Message);
    }

    [Fact]
    public async Task CreateReferenceAsync_DirectoryFileConflict_WithPackedRefs_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("branch feature");
        repo.RunGit("gc --aggressive --prune=now");

        var store = new GitReferenceStore(repo.GitDirectory);
        var commit = repo.Head;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateReferenceAsync("refs/heads/feature/task", commit));
        Assert.Contains("feature", ex.Message);
    }

    [Fact]
    public async Task RenameBranchAsync_DirectoryFileConflict_ThrowsInvalidOperationException()
    {
        using var repo = GitTestRepository.Create();
        var store = new GitReferenceStore(repo.GitDirectory);
        var commit = repo.Head;

        await store.CreateReferenceAsync("refs/heads/feature", commit);
        await store.CreateReferenceAsync("refs/heads/temp", commit);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RenameBranchAsync("temp", "feature/sub"));
        Assert.Contains("feature", ex.Message);
    }
}
