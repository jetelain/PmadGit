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

    [Fact]
    public async Task GetCommitDiffAsync_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("config core.autocrlf false");
        var commit1 = testRepo.Commit("Base commit",
            ("modified.txt", "line1\nline2\nline3\n"),
            ("deleted.txt", "to be deleted\n"));

        var commit2 = testRepo.Commit("Second commit",
            ("modified.txt", "line1\nline2 changed\nline3\nline4\n"),
            ("added.txt", "new file content\n"));
        testRepo.RunGit("rm deleted.txt");
        testRepo.RunGit("commit --amend -m \"Second commit with delete\" --quiet");
        var headCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var managedDiff = await repo.GetCommitDiffAsync(headCommit);
        var cliDiff = testRepo.RunGit($"-c core.abbrev=7 diff HEAD~1 HEAD");

        Assert.Equal(Normalize(cliDiff), Normalize(managedDiff));
    }

    [Fact]
    public async Task GetCommitStatAsync_MatchesGitCliShortStat()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("config core.autocrlf false");
        testRepo.Commit("Base",
            ("a.txt", "alpha\n"),
            ("b.txt", "beta\nline2\n"));

        var commit2 = testRepo.Commit("Changes",
            ("a.txt", "alpha\nalpha2\nalpha3\n"),
            ("b.txt", "beta changed\n"),
            ("c.txt", "gamma\n"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var stat = await repo.GetCommitStatAsync(commit2.Value);
        var cliShortStat = testRepo.RunGit("diff --shortstat HEAD~1 HEAD").Trim();

        Assert.Equal(cliShortStat, stat.ToShortStat());
    }

    [Fact]
    public async Task GetDiffAsync_WithSubpathFilter_MatchesGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("config core.autocrlf false");
        var commit1 = testRepo.Commit("Base",
            ("src/module1/file1.txt", "m1 original\n"),
            ("src/module2/file2.txt", "m2 original\n"),
            ("docs/readme.txt", "readme\n"));

        var commit2 = testRepo.Commit("Update all",
            ("src/module1/file1.txt", "m1 updated\n"),
            ("src/module2/file2.txt", "m2 updated\n"),
            ("docs/readme.txt", "readme updated\n"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        // Path filter to src/module1
        var managedDiff = await repo.GetDiffAsync(commit1.Value, commit2.Value, path: "src/module1");
        var cliDiff = testRepo.RunGit($"-c core.abbrev=7 diff {commit1.Value} {commit2.Value} -- src/module1");

        Assert.Equal(Normalize(cliDiff), Normalize(managedDiff));
    }

    [Fact]
    public async Task GetUnstagedDiffAsync_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("config core.autocrlf false");
        testRepo.Commit("Base",
            ("file1.txt", "first\nsecond\nthird\n"),
            ("file2.txt", "delete me\n"));

        // Make modifications in working tree
        var file1Path = Path.Combine(testRepo.WorkingDirectory, "file1.txt");
        var file2Path = Path.Combine(testRepo.WorkingDirectory, "file2.txt");
        await File.WriteAllTextAsync(file1Path, "first\nsecond modified\nthird\nfourth\n");
        File.Delete(file2Path);

        using var workspaceRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var managedDiff = await workspaceRepo.GetUnstagedDiffAsync();
        var cliDiff = testRepo.RunGit("-c core.abbrev=7 diff");

        Assert.Equal(Normalize(cliDiff), Normalize(managedDiff));
    }

    [Fact]
    public async Task GetStagedDiffAsync_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("config core.autocrlf false");
        testRepo.Commit("Base",
            ("file1.txt", "initial line\n"));

        using var workspaceRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var file1Path = Path.Combine(testRepo.WorkingDirectory, "file1.txt");
        var newFilePath = Path.Combine(testRepo.WorkingDirectory, "newfile.txt");
        await File.WriteAllTextAsync(file1Path, "initial line modified\n");
        await File.WriteAllTextAsync(newFilePath, "staged new file\n");

        await workspaceRepo.StageAsync("file1.txt");
        await workspaceRepo.StageAsync("newfile.txt");

        var managedDiff = await workspaceRepo.GetStagedDiffAsync();
        var cliDiff = testRepo.RunGit("-c core.abbrev=7 diff --cached");

        Assert.Equal(Normalize(cliDiff), Normalize(managedDiff));
    }

    [Fact]
    public async Task GetCommitDiffAsync_BinaryFiles_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        var bin1 = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var bin2 = new byte[] { 0x00, 0x01, 0x04, 0x05 };

        var binPath = Path.Combine(testRepo.WorkingDirectory, "data.bin");
        await File.WriteAllBytesAsync(binPath, bin1);
        testRepo.RunGit("add data.bin");
        testRepo.RunGit("commit -m \"Add binary\"");

        await File.WriteAllBytesAsync(binPath, bin2);
        testRepo.RunGit("add data.bin");
        testRepo.RunGit("commit -m \"Update binary\"");

        var headCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var managedDiff = await repo.GetCommitDiffAsync(headCommit);
        var cliDiff = testRepo.RunGit("-c core.abbrev=7 diff HEAD~1 HEAD");

        Assert.Equal(Normalize(cliDiff), Normalize(managedDiff));
    }

    [Fact]
    public async Task GetCommitDiffAsync_Submodule_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        var subHash1 = "1111111111111111111111111111111111111111";
        var subHash2 = "2222222222222222222222222222222222222222";

        // Add submodule
        testRepo.RunGit($"update-index --add --cacheinfo 160000,{subHash1},sub");
        testRepo.RunGit("commit -m \"Add submodule\"");
        var addCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var addDiffManaged = await repo.GetCommitDiffAsync(addCommit);
        var addDiffCli = testRepo.RunGit("-c core.abbrev=7 diff HEAD~1 HEAD");
        Assert.Equal(Normalize(addDiffCli), Normalize(addDiffManaged));

        var addStat = await repo.GetCommitStatAsync(addCommit);
        var addStatCli = testRepo.RunGit("diff --shortstat HEAD~1 HEAD").Trim();
        Assert.Equal(addStatCli, addStat.ToShortStat());

        // Update submodule
        testRepo.RunGit($"update-index --add --cacheinfo 160000,{subHash2},sub");
        testRepo.RunGit("commit -m \"Update submodule\"");
        var updateCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        var updateDiffManaged = await repo.GetCommitDiffAsync(updateCommit);
        var updateDiffCli = testRepo.RunGit("-c core.abbrev=7 diff HEAD~1 HEAD");
        Assert.Equal(Normalize(updateDiffCli), Normalize(updateDiffManaged));

        var updateStat = await repo.GetCommitStatAsync(updateCommit);
        var updateStatCli = testRepo.RunGit("diff --shortstat HEAD~1 HEAD").Trim();
        Assert.Equal(updateStatCli, updateStat.ToShortStat());

        // Delete submodule
        testRepo.RunGit("update-index --force-remove sub");
        testRepo.RunGit("commit -m \"Delete submodule\"");
        var deleteCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        var deleteDiffManaged = await repo.GetCommitDiffAsync(deleteCommit);
        var deleteDiffCli = testRepo.RunGit("-c core.abbrev=7 diff HEAD~1 HEAD");
        Assert.Equal(Normalize(deleteDiffCli), Normalize(deleteDiffManaged));

        var deleteStat = await repo.GetCommitStatAsync(deleteCommit);
        var deleteStatCli = testRepo.RunGit("diff --shortstat HEAD~1 HEAD").Trim();
        Assert.Equal(deleteStatCli, deleteStat.ToShortStat());
    }

    [Fact]
    public async Task GetUnstagedDiffAsync_Submodule_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        var subDir = Path.Combine(testRepo.WorkingDirectory, "sub");
        TestHelper.RunGit(testRepo.WorkingDirectory, "init sub");
        TestHelper.RunGit(subDir, "config user.name test");
        TestHelper.RunGit(subDir, "config user.email test@test.com");
        TestHelper.RunGit(subDir, "commit --allow-empty -m \"sub1\"");
        var subHash1 = TestHelper.RunGit(subDir, "rev-parse HEAD").Trim();
        TestHelper.RunGit(subDir, "commit --allow-empty -m \"sub2\"");
        var subHash2 = TestHelper.RunGit(subDir, "rev-parse HEAD").Trim();
        TestHelper.RunGit(subDir, $"checkout {subHash1}");

        testRepo.RunGit($"update-index --add --cacheinfo 160000,{subHash1},sub");
        testRepo.RunGit("commit -m \"Add submodule\"");

        using var workspaceRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // When submodule HEAD matches index, unstaged diff is empty
        var cleanManagedDiff = await workspaceRepo.GetUnstagedDiffAsync();
        Assert.Empty(cleanManagedDiff);

        // When submodule HEAD is updated without staging
        TestHelper.RunGit(subDir, $"checkout {subHash2}");

        var updatedManagedDiff = await workspaceRepo.GetUnstagedDiffAsync();
        var updatedCliDiff = testRepo.RunGit("-c core.abbrev=7 diff");
        Assert.Equal(Normalize(updatedCliDiff), Normalize(updatedManagedDiff));

        // Also test gitdir file pointer scenario (.git file pointing to gitdir)
        var externalGitDir = Path.Combine(testRepo.WorkingDirectory, ".git", "modules", "sub");
        Directory.CreateDirectory(externalGitDir);
        await File.WriteAllTextAsync(Path.Combine(externalGitDir, "HEAD"), subHash2 + "\n");
        TestHelper.TryDeleteDirectory(Path.Combine(subDir, ".git"));
        await File.WriteAllTextAsync(Path.Combine(subDir, ".git"), "gitdir: ../.git/modules/sub\n");

        var gitdirManagedDiff = await workspaceRepo.GetUnstagedDiffAsync();
        Assert.Equal(Normalize(updatedCliDiff), Normalize(gitdirManagedDiff));

        // When submodule folder is removed from workspace, unstaged diff reports deleted submodule
        TestHelper.TryDeleteDirectory(subDir);
        var deletedManagedDiff = await workspaceRepo.GetUnstagedDiffAsync();
        var deletedCliDiff = testRepo.RunGit("-c core.abbrev=7 diff");
        Assert.Equal(Normalize(deletedCliDiff), Normalize(deletedManagedDiff));
    }

    [Fact]
    public async Task GetCommitDiffAsync_FinalNewlineChange_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("config core.autocrlf false");
        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");

        await File.WriteAllTextAsync(filePath, "abc");
        testRepo.RunGit("add file.txt");
        testRepo.RunGit("commit -m \"No newline\"");

        await File.WriteAllTextAsync(filePath, "abc\n");
        testRepo.RunGit("add file.txt");
        testRepo.RunGit("commit -m \"Added newline\"");

        var headCommit = testRepo.RunGit("rev-parse HEAD").Trim();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var managedDiff = await repo.GetCommitDiffAsync(headCommit);
        var cliDiff = testRepo.RunGit("-c core.abbrev=7 diff HEAD~1 HEAD");
        Assert.Equal(Normalize(cliDiff), Normalize(managedDiff));

        var stat = await repo.GetCommitStatAsync(headCommit);
        var cliStat = testRepo.RunGit("diff --shortstat HEAD~1 HEAD").Trim();
        Assert.Equal(cliStat, stat.ToShortStat());
    }

    [Fact]
    public async Task GetCommitDiffAsync_LineEndingChangeLfToCrlf_MatchesGitCliOutput()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.RunGit("config core.autocrlf false");
        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");

        await File.WriteAllTextAsync(filePath, "line1\nline2\n");
        testRepo.RunGit("add file.txt");
        testRepo.RunGit("commit -m \"LF line endings\"");

        await File.WriteAllTextAsync(filePath, "line1\r\nline2\r\n");
        testRepo.RunGit("add file.txt");
        testRepo.RunGit("commit -m \"CRLF line endings\"");

        var headCommit = testRepo.RunGit("rev-parse HEAD").Trim();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var managedDiff = await repo.GetCommitDiffAsync(headCommit);
        var cliDiff = testRepo.RunGit("-c core.abbrev=7 diff HEAD~1 HEAD");
        Assert.Equal(Normalize(cliDiff), Normalize(managedDiff));

        var stat = await repo.GetCommitStatAsync(headCommit);
        var cliStat = testRepo.RunGit("diff --shortstat HEAD~1 HEAD").Trim();
        Assert.Equal(cliStat, stat.ToShortStat());
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Trim();
}

