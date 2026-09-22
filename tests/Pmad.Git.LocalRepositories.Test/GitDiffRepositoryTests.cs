using System;
using System.IO;
using System.Threading.Tasks;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Diff;
using Pmad.Git.Tests.Infrastructure;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitDiffRepositoryTests
{
    [Fact]
    public async Task GetCommitDiffAsync_ModifiedFile_ProducesAccurateDiff()
    {
        using var testRepo = GitTestRepository.Create();
        var commit1 = testRepo.Commit("First", ("file.txt", "line1\nline2\n"));
        var commit2 = testRepo.Commit("Second", ("file.txt", "line1\nline2 modified\nline3\n"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var diff = await repo.GetCommitDiffAsync(commit2.Value);

        Assert.Contains("diff --git a/file.txt b/file.txt", diff);
        Assert.Contains("--- a/file.txt", diff);
        Assert.Contains("+++ b/file.txt", diff);
        Assert.Contains("-line2", diff);
        Assert.Contains("+line2 modified", diff);
        Assert.Contains("+line3", diff);
    }

    [Fact]
    public async Task GetCommitDiffAsync_AddedFile_ProducesNewFileDiff()
    {
        using var testRepo = GitTestRepository.Create();
        var commit = testRepo.Commit("Add new file", ("newfile.txt", "hello world\n"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var diff = await repo.GetCommitDiffAsync(commit.Value);

        Assert.Contains("diff --git a/newfile.txt b/newfile.txt", diff);
        Assert.Contains("new file mode 100644", diff);
        Assert.Contains("--- /dev/null", diff);
        Assert.Contains("+++ b/newfile.txt", diff);
        Assert.Contains("+hello world", diff);
    }

    [Fact]
    public async Task GetCommitDiffAsync_DeletedFile_ProducesDeletedFileDiff()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Add file to delete", ("todelete.txt", "will be removed\n"));
        testRepo.RunGit("rm todelete.txt");
        testRepo.RunGit("commit -m \"Remove file\"");
        var deleteCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var diff = await repo.GetCommitDiffAsync(deleteCommit);

        Assert.Contains("diff --git a/todelete.txt b/todelete.txt", diff);
        Assert.Contains("deleted file mode 100644", diff);
        Assert.Contains("--- a/todelete.txt", diff);
        Assert.Contains("+++ /dev/null", diff);
        Assert.Contains("-will be removed", diff);
    }

    [Fact]
    public async Task GetCommitStatAsync_CalculatesCorrectStatistics()
    {
        using var testRepo = GitTestRepository.Create();
        var commit = testRepo.Commit("Multi file change",
            ("fileA.txt", "line1\nline2\n"),
            ("fileB.txt", "hello\n"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var stat = await repo.GetCommitStatAsync(commit.Value);

        Assert.Equal(2, stat.FilesChanged);
        Assert.Equal(3, stat.Insertions);
        Assert.Equal(0, stat.Deletions);
        Assert.Equal("2 files changed, 3 insertions(+)", stat.ToShortStat());
    }

    [Fact]
    public async Task GetDiffAsync_BetweenCommits_SupportsPathFilter()
    {
        using var testRepo = GitTestRepository.Create();
        var commit1 = testRepo.Commit("Base", ("dir/a.txt", "a\n"), ("dir/b.txt", "b\n"));
        var commit2 = testRepo.Commit("Modify both", ("dir/a.txt", "a modified\n"), ("dir/b.txt", "b modified\n"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        // Filtered to only dir/a.txt
        var diffA = await repo.GetDiffAsync(commit1.Value, commit2.Value, path: "dir/a.txt");
        Assert.Contains("dir/a.txt", diffA);
        Assert.DoesNotContain("dir/b.txt", diffA);

        // Unfiltered diff
        var diffAll = await repo.GetDiffAsync(commit1.Value, commit2.Value);
        Assert.Contains("dir/a.txt", diffAll);
        Assert.Contains("dir/b.txt", diffAll);
    }

    [Fact]
    public async Task GetUnstagedDiffAsync_DetectsWorkingTreeModifications()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit file", ("file.txt", "original text\n"));

        // Modify file in working tree without staging
        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "modified text\n");

        using var workspaceRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var diff = await workspaceRepo.GetUnstagedDiffAsync();

        Assert.Contains("diff --git a/file.txt b/file.txt", diff);
        Assert.Contains("-original text", diff);
        Assert.Contains("+modified text", diff);
    }

    [Fact]
    public async Task GetStagedDiffAsync_DetectsStagedChanges()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "v1\n"));

        using var workspaceRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "v2\n");
        await workspaceRepo.StageAsync("file.txt");

        var stagedDiff = await workspaceRepo.GetStagedDiffAsync();
        Assert.Contains("diff --git a/file.txt b/file.txt", stagedDiff);
        Assert.Contains("-v1", stagedDiff);
        Assert.Contains("+v2", stagedDiff);

        // Working tree is clean relative to index
        var unstagedDiff = await workspaceRepo.GetUnstagedDiffAsync();
        Assert.Empty(unstagedDiff);
    }
}
