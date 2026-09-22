using Pmad.Git.Tests.Infrastructure;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class MergeBaseTests
{
    [Fact]
    public async Task FindMergeBaseAsync_SameCommit_ReturnsSame()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var head = await repo.ReferenceStore.ResolveHeadAsync();

        var mergeBase = await repo.FindMergeBaseAsync(head, head);

        Assert.Equal(head, mergeBase);
    }

    [Fact]
    public async Task FindMergeBaseAsync_LinearHistory_ReturnsAncestor()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var commitA = await repo.ReferenceStore.ResolveHeadAsync();

        testRepo.RunGit("commit --allow-empty -m \"commit B\"");
        var commitB = await repo.ReferenceStore.ResolveHeadAsync();

        testRepo.RunGit("commit --allow-empty -m \"commit C\"");
        var commitC = await repo.ReferenceStore.ResolveHeadAsync();

        var baseAC = await repo.FindMergeBaseAsync(commitA, commitC);
        Assert.Equal(commitA, baseAC);

        var baseBC = await repo.FindMergeBaseAsync(commitB, commitC);
        Assert.Equal(commitB, baseBC);
    }

    [Fact]
    public async Task FindMergeBaseAsync_BranchingHistory_MatchesGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var baseCommit = await repo.ReferenceStore.ResolveHeadAsync();

        // Branch 1
        testRepo.RunGit("checkout -b branch1");
        testRepo.RunGit("commit --allow-empty -m \"branch1 commit 1\"");
        testRepo.RunGit("commit --allow-empty -m \"branch1 commit 2\"");
        var branch1Commit = (await repo.GetCommitAsync("branch1")).Id;

        // Branch 2
        testRepo.RunGit($"checkout -b branch2 {baseCommit}");
        testRepo.RunGit("commit --allow-empty -m \"branch2 commit 1\"");
        testRepo.RunGit("commit --allow-empty -m \"branch2 commit 2\"");
        var branch2Commit = (await repo.GetCommitAsync("branch2")).Id;

        var managedBase = await repo.FindMergeBaseAsync(branch1Commit, branch2Commit);
        var cliBase = testRepo.RunGit($"merge-base {branch1Commit} {branch2Commit}").Trim();

        Assert.NotNull(managedBase);
        Assert.Equal(cliBase, managedBase.Value.ToString());
        Assert.Equal(baseCommit, managedBase.Value);
    }
}
