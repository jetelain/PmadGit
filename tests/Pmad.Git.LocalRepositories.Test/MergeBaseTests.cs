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

    [Fact]
    public async Task FindMergeBasesAsync_CrissCross_ReturnsAllBasesAndConstructsVirtualBase()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        // Initial base commit with file1.txt
        var file1 = Path.Combine(testRepo.WorkingDirectory, "file1.txt");
        await File.WriteAllTextAsync(file1, "base 1\n");
        testRepo.RunGit("add file1.txt");
        testRepo.RunGit("commit -m \"Base 1\"");
        var base1 = await repo.ReferenceStore.ResolveHeadAsync();

        // Branch A commits fileA.txt
        testRepo.RunGit("checkout -b branchA");
        var fileA = Path.Combine(testRepo.WorkingDirectory, "fileA.txt");
        await File.WriteAllTextAsync(fileA, "content A\n");
        testRepo.RunGit("add fileA.txt");
        testRepo.RunGit("commit -m \"Commit A\"");
        var commitA = await repo.ReferenceStore.ResolveHeadAsync();

        // Branch B from base1 commits fileB.txt
        testRepo.RunGit($"checkout -b branchB {base1}");
        var fileB = Path.Combine(testRepo.WorkingDirectory, "fileB.txt");
        await File.WriteAllTextAsync(fileB, "content B\n");
        testRepo.RunGit("add fileB.txt");
        testRepo.RunGit("commit -m \"Commit B\"");
        var commitB = new GitHash(testRepo.RunGit("rev-parse HEAD").Trim());

        // Criss-cross merge 1: branchA merges branchB -> M1
        testRepo.RunGit("checkout branchA");
        testRepo.RunGit("merge --no-edit branchB");
        var commitM1 = new GitHash(testRepo.RunGit("rev-parse HEAD").Trim());

        // Criss-cross merge 2: branchB merges commitA -> M2
        testRepo.RunGit("checkout branchB");
        testRepo.RunGit($"merge --no-edit {commitA}");
        var commitM2 = new GitHash(testRepo.RunGit("rev-parse HEAD").Trim());

        // Commit C on branchA
        testRepo.RunGit("checkout branchA");
        var fileC = Path.Combine(testRepo.WorkingDirectory, "fileC.txt");
        await File.WriteAllTextAsync(fileC, "content C\n");
        testRepo.RunGit("add fileC.txt");
        testRepo.RunGit("commit -m \"Commit C\"");
        var commitC = new GitHash(testRepo.RunGit("rev-parse HEAD").Trim());

        // Commit D on branchB
        testRepo.RunGit("checkout branchB");
        var fileD = Path.Combine(testRepo.WorkingDirectory, "fileD.txt");
        await File.WriteAllTextAsync(fileD, "content D\n");
        testRepo.RunGit("add fileD.txt");
        testRepo.RunGit("commit -m \"Commit D\"");
        var commitD = new GitHash(testRepo.RunGit("rev-parse HEAD").Trim());

        repo.InvalidateCaches();

        // Verify git CLI reports 2 merge bases
        var cliBases = testRepo.RunGit($"merge-base --all {commitC} {commitD}")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
        Assert.Equal(2, cliBases.Count);

        // FindMergeBasesAsync returns both commitA and commitB
        var allBases = await repo.FindMergeBasesAsync(commitC, commitD);
        Assert.Equal(2, allBases.Count);
        Assert.Contains(commitA, allBases);
        Assert.Contains(commitB, allBases);

        // FindMergeBaseAsync constructs a virtual merge base commit that cleanly merges the merge bases (commitA and commitB)
        var virtualBase = await repo.FindMergeBaseAsync(commitC, commitD);
        Assert.NotNull(virtualBase);

        // Virtual base commit has commitA and commitB as parents
        var virtualCommit = await repo.GetCommitAsync(virtualBase.Value.Value);
        Assert.Equal(2, virtualCommit.Parents.Count);
        Assert.Contains(commitA, virtualCommit.Parents);
        Assert.Contains(commitB, virtualCommit.Parents);

        // Virtual base tree contains file1.txt, fileA.txt, and fileB.txt
        var virtualLeaves = await repo.LoadLeafEntriesAsync(virtualCommit.Tree, default);
        Assert.True(virtualLeaves.ContainsKey("file1.txt"));
        Assert.True(virtualLeaves.ContainsKey("fileA.txt"));
        Assert.True(virtualLeaves.ContainsKey("fileB.txt"));

        // Repository merge using this virtual merge base succeeds cleanly without spurious conflicts
        testRepo.RunGit("checkout branchA");
        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var mergeResult = await workspace.MergeAsync("branchB");

        Assert.True(mergeResult.IsSuccess);
        Assert.Equal(GitMergeStatus.Merged, mergeResult.Status);
        Assert.False(await workspace.IsMergeInProgressAsync());
        Assert.True(File.Exists(fileA));
        Assert.True(File.Exists(fileB));
        Assert.True(File.Exists(fileC));
        Assert.True(File.Exists(fileD));
    }
}

