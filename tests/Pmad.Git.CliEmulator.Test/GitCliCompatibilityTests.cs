using System.IO;
using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class GitCliCompatibilityTests
{
    [Fact]
    public async Task Add_DirectoryPath_StagesFilesInDirectory_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var subDir = Path.Combine(testRepo.WorkingDirectory, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "nested content");

        // In standard git: 'git add subdir' or 'git add subdir/' stages subdir/nested.txt
        var response = await emulator.InvokeAsync(["add", "subdir"], approval);
        Assert.Equal(0, response.ExitCode);

        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        // Should show 'A  subdir/nested.txt'
        Assert.Contains("A  subdir/nested.txt", status.StdOut);
    }

    [Fact]
    public async Task Add_MultipleArgsWithDot_StagesAll_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "fileA.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "fileB.txt"), "B");

        // In standard git: 'git add . fileA.txt' stages all
        var response = await emulator.InvokeAsync(["add", ".", "fileA.txt"], approval);
        Assert.Equal(0, response.ExitCode);

        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("A  fileA.txt", status.StdOut);
        Assert.Contains("A  fileB.txt", status.StdOut);
    }

    [Fact]
    public async Task Commit_WhenClean_FailsWithNothingToCommit_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Working tree and index are clean.
        // Standard git: 'git commit -m "no changes"' exits with code 1 and prints "nothing to commit"
        var response = await emulator.InvokeAsync(["commit", "-m", "no changes"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("nothing to commit", response.StdOut + response.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Branch_ShowCurrent_OutputsCurrentBranchName_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git branch --show-current' returns 'master'
        var response = await emulator.InvokeAsync(["branch", "--show-current"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("master", response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_AbbrevRefHead_OutputsBranchName_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git rev-parse --abbrev-ref HEAD' returns 'master'
        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("master", response.StdOut.Trim());
    }

    [Fact]
    public async Task Tag_ListFlag_ListsTags_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["tag", "v1.0.0"], approval);

        // Standard git: 'git tag -l' or 'git tag --list' lists tags
        var response = await emulator.InvokeAsync(["tag", "-l"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("v1.0.0", response.StdOut);
    }

    [Fact]
    public async Task Reset_NoArgs_UnstagesChanges_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var stagedFile = Path.Combine(testRepo.WorkingDirectory, "staged.txt");
        await File.WriteAllTextAsync(stagedFile, "content");
        await emulator.InvokeAsync(["add", "staged.txt"], approval);

        // Standard git: 'git reset' (no arguments) mixed-resets HEAD, unstaging files
        var response = await emulator.InvokeAsync(["reset"], approval);

        Assert.Equal(0, response.ExitCode);
        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.DoesNotContain("A  staged.txt", status.StdOut);
        Assert.Contains("?? staged.txt", status.StdOut);
    }

    [Fact]
    public async Task Reset_Path_UnstagesSpecificFile_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file1.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file2.txt"), "2");
        await emulator.InvokeAsync(["add", "file1.txt", "file2.txt"], approval);

        // Standard git: 'git reset file1.txt' unstages file1.txt while leaving file2.txt staged
        var response = await emulator.InvokeAsync(["reset", "file1.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("?? file1.txt", status.StdOut);
        Assert.Contains("A  file2.txt", status.StdOut);
    }

    [Fact]
    public async Task Diff_FilePathWithoutPathFlag_DiffsFile_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var readme = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.AppendAllTextAsync(readme, "\nappended line");

        // Standard git: 'git diff README.md' or 'git diff -- README.md' diffs that file
        var response = await emulator.InvokeAsync(["diff", "README.md"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("+appended line", response.StdOut);
    }

    [Fact]
    public async Task Switch_Dash_SwitchesToPreviousBranch_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["switch", "-c", "feat-branch"], approval);
        Assert.Equal("feat-branch", await repo.GetCurrentBranchNameAsync());

        await emulator.InvokeAsync(["switch", "master"], approval);
        Assert.Equal("master", await repo.GetCurrentBranchNameAsync());

        // Standard git: 'git switch -' switches back to feat-branch
        var response = await emulator.InvokeAsync(["switch", "-"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("feat-branch", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Checkout_ExistingBranchWithB_Fails_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git checkout -b master' fails because master already exists
        var response = await emulator.InvokeAsync(["checkout", "-b", "master"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("already exists", response.StdErr + response.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_WithLeadingGitToken_ExecutesCommand_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // When user/AI passes 'git status', emulator should handle the leading 'git' gracefully
        var response = await emulator.InvokeAsync(["git", "status"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("nothing to commit", response.StdOut);
    }

    [Fact]
    public async Task Log_TwoDotRange_OutputsCommitsInRange_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        testRepo.Commit("Commit 3", ("f3.txt", "3"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git log HEAD~1..HEAD' only outputs Commit 3
        var response = await emulator.InvokeAsync(["log", "--oneline", "HEAD~1..HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Commit 3", response.StdOut);
        Assert.DoesNotContain("Commit 2", response.StdOut);
    }

    [Fact]
    public async Task Diff_TwoDotRange_OutputsDiff_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "content f2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git diff HEAD~1..HEAD' outputs diff between parent and HEAD
        var response = await emulator.InvokeAsync(["diff", "HEAD~1..HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("+content f2", response.StdOut);
    }

    [Fact]
    public async Task Mv_CaseOnlyRename_OnWindows_Succeeds()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var path = Path.Combine(testRepo.WorkingDirectory, "case_test.txt");
        await File.WriteAllTextAsync(path, "content");
        await emulator.InvokeAsync(["add", "case_test.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add case_test.txt"], approval);

        // Standard git: 'git mv case_test.txt CASE_TEST.TXT' succeeds on Windows
        var response = await emulator.InvokeAsync(["mv", "case_test.txt", "CASE_TEST.TXT"], approval);

        Assert.Equal(0, response.ExitCode);
    }

    [Fact]
    public async Task Status_RenamedFile_DetectsRenameInPorcelain()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var path = Path.Combine(testRepo.WorkingDirectory, "orig.txt");
        await File.WriteAllTextAsync(path, "unique content for rename detection");
        await emulator.InvokeAsync(["add", "orig.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add orig.txt"], approval);

        await emulator.InvokeAsync(["mv", "orig.txt", "dest.txt"], approval);

        // Standard git: 'git status --porcelain' outputs 'R  orig.txt -> dest.txt'
        var response = await emulator.InvokeAsync(["status", "--porcelain"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("R  orig.txt -> dest.txt", response.StdOut);
    }

    [Fact]
    public async Task RevParse_UpstreamShorthand_ResolvesUpstream()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Create remote tracking ref
        testRepo.RunGit("remote add origin https://example.com/repo.git");
        var headHash = testRepo.Head.ToString();
        var remoteRefDir = Path.Combine(testRepo.WorkingDirectory, ".git", "refs", "remotes", "origin");
        Directory.CreateDirectory(remoteRefDir);
        await File.WriteAllTextAsync(Path.Combine(remoteRefDir, "master"), headHash + "\n");

        // Set upstream in git config
        testRepo.RunGit("branch --set-upstream-to=origin/master master");

        // Standard git: 'git rev-parse @{u}' or 'git rev-parse @{upstream}' resolves to commit hash
        var response = await emulator.InvokeAsync(["rev-parse", "@{u}"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(headHash, response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_ParentCombinations_ResolvesCommit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        testRepo.Commit("Commit 3", ("f3.txt", "3"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git rev-parse HEAD~1^1' resolves to Commit 2
        var response = await emulator.InvokeAsync(["rev-parse", "HEAD~1^1"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.NotEmpty(response.StdOut.Trim());
    }

    [Fact]
    public async Task Invoke_WhenIndexLocked_ReportsClearError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Simulate external process holding .git/index.lock
        var lockPath = Path.Combine(testRepo.GitDirectory, "index.lock");
        await File.WriteAllTextAsync(lockPath, "external lock holder");

        try
        {
            var newFile = Path.Combine(testRepo.WorkingDirectory, "locked.txt");
            await File.WriteAllTextAsync(newFile, "hello");

            // In standard git, attempting to stage when index.lock exists fails with:
            // "fatal: Unable to create '.../.git/index.lock': File exists."
            var response = await emulator.InvokeAsync(["add", "locked.txt"], approval);

            Assert.NotEqual(0, response.ExitCode);
            Assert.Contains("index.lock", response.StdErr + response.StdOut, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
    }

    [Fact]
    public async Task Checkout_OursDuringConflict_ChecksOutOursVersion_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var conflictFile = Path.Combine(testRepo.WorkingDirectory, "conflict.txt");
        await File.WriteAllTextAsync(conflictFile, "base\n");
        await emulator.InvokeAsync(["add", "conflict.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Base"], approval);

        await emulator.InvokeAsync(["branch", "side"], approval);

        // Modify on master
        await File.WriteAllTextAsync(conflictFile, "ours content\n");
        await emulator.InvokeAsync(["add", "conflict.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Ours"], approval);

        // Modify on side
        await emulator.InvokeAsync(["switch", "side"], approval);
        await File.WriteAllTextAsync(conflictFile, "theirs content\n");
        await emulator.InvokeAsync(["add", "conflict.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Theirs"], approval);

        // Merge side into master to create conflict
        await emulator.InvokeAsync(["switch", "master"], approval);
        await emulator.InvokeAsync(["merge", "side"], approval);

        // Standard git: 'git checkout --ours conflict.txt' checks out stage 2 version
        var response = await emulator.InvokeAsync(["checkout", "--ours", "conflict.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        var content = await File.ReadAllTextAsync(conflictFile);
        Assert.Equal("ours content\n", content);
    }

    [Fact]
    public async Task Checkout_TheirsDuringConflict_ChecksOutTheirsVersion_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var conflictFile = Path.Combine(testRepo.WorkingDirectory, "conflict.txt");
        await File.WriteAllTextAsync(conflictFile, "base\n");
        await emulator.InvokeAsync(["add", "conflict.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Base"], approval);

        await emulator.InvokeAsync(["branch", "side"], approval);

        // Modify on master
        await File.WriteAllTextAsync(conflictFile, "ours content\n");
        await emulator.InvokeAsync(["add", "conflict.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Ours"], approval);

        // Modify on side
        await emulator.InvokeAsync(["switch", "side"], approval);
        await File.WriteAllTextAsync(conflictFile, "theirs content\n");
        await emulator.InvokeAsync(["add", "conflict.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Theirs"], approval);

        // Merge side into master to create conflict
        await emulator.InvokeAsync(["switch", "master"], approval);
        await emulator.InvokeAsync(["merge", "side"], approval);

        // Standard git: 'git checkout --theirs conflict.txt' checks out stage 3 version
        var response = await emulator.InvokeAsync(["checkout", "--theirs", "conflict.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        var content = await File.ReadAllTextAsync(conflictFile);
        Assert.Equal("theirs content\n", content);
    }

    [Fact]
    public async Task Add_QuotedWildcard_StagesMatchingFiles_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "sample1.txt"), "sample 1");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "sample2.txt"), "sample 2");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "other.dat"), "binary");

        // Standard git: 'git add "*.txt"' matches sample1.txt and sample2.txt without staging other.dat
        var response = await emulator.InvokeAsync(["add", "*.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("A  sample1.txt", status.StdOut);
        Assert.Contains("A  sample2.txt", status.StdOut);
        Assert.Contains("?? other.dat", status.StdOut);
    }

    [Fact]
    public async Task Commit_OnDetachedHead_UpdatesHeadDirectly_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Detach HEAD
        var initialCommit = await repo.GetCommitAsync("HEAD");
        await emulator.InvokeAsync(["checkout", "--detach", initialCommit.Id.ToString()], approval);

        var statusBefore = await emulator.InvokeAsync(["status"], approval);
        Assert.Contains("HEAD detached", statusBefore.StdOut);

        // Commit on detached HEAD
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "detached_file.txt"), "detached content");
        await emulator.InvokeAsync(["add", "detached_file.txt"], approval);
        var commitResponse = await emulator.InvokeAsync(["commit", "-m", "Commit on detached HEAD"], approval);

        Assert.Equal(0, commitResponse.ExitCode);

        // HEAD must point to new commit directly, status must still report HEAD detached
        var headAfter = await repo.ReferenceStore.ResolveHeadAsync();
        Assert.NotEqual(initialCommit.Id, headAfter);

        var statusAfter = await emulator.InvokeAsync(["status"], approval);
        Assert.Contains("HEAD detached", statusAfter.StdOut);
    }

    [Fact]
    public async Task Add_NonExistentFile_FailsWithExitCode_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git add nonexistent.txt' exits with code 128 (fatal: pathspec did not match any files)
        var response = await emulator.InvokeAsync(["add", "nonexistent.txt"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("nonexistent.txt", response.StdErr + response.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Add_UpdateFlag_StagesModifiedAndDeletedOnly_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var file1 = Path.Combine(testRepo.WorkingDirectory, "file1.txt");
        await File.WriteAllTextAsync(file1, "original");
        await emulator.InvokeAsync(["add", "file1.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add file1"], approval);

        // Modify tracked file1
        await File.WriteAllTextAsync(file1, "modified");

        // Create new untracked file2
        var file2 = Path.Combine(testRepo.WorkingDirectory, "untracked.txt");
        await File.WriteAllTextAsync(file2, "untracked");

        // Standard git: 'git add -u' stages modified/deleted tracked files, ignoring untracked files
        var response = await emulator.InvokeAsync(["add", "-u"], approval);
        Assert.Equal(0, response.ExitCode);

        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("M  file1.txt", status.StdOut);
        Assert.Contains("?? untracked.txt", status.StdOut);
        Assert.DoesNotContain("A  untracked.txt", status.StdOut);
    }

    [Fact]
    public async Task Commit_AllFlag_DoesNotCommitUntrackedFiles_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var file1 = Path.Combine(testRepo.WorkingDirectory, "tracked.txt");
        await File.WriteAllTextAsync(file1, "v1");
        await emulator.InvokeAsync(["add", "tracked.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Commit v1"], approval);

        // Modify tracked file
        await File.WriteAllTextAsync(file1, "v2");

        // Create untracked file
        var file2 = Path.Combine(testRepo.WorkingDirectory, "untracked.txt");
        await File.WriteAllTextAsync(file2, "untracked content");

        // Standard git: 'git commit -a -m "..."' stages and commits tracked modifications, leaving untracked files untouched
        var response = await emulator.InvokeAsync(["commit", "-a", "-m", "Commit with -a"], approval);
        Assert.Equal(0, response.ExitCode);

        // untracked.txt must still be untracked in status
        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("?? untracked.txt", status.StdOut);

        // untracked.txt must NOT be in the newly committed tree
        var pathTypeInHead = await repo.GetPathTypeAsync("untracked.txt", "HEAD");
        Assert.Null(pathTypeInHead);
    }

    [Fact]
    public async Task Mv_FileToExistingDirectory_MovesFileInsideDirectory_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var srcFile = Path.Combine(testRepo.WorkingDirectory, "item.txt");
        await File.WriteAllTextAsync(srcFile, "item content");
        await emulator.InvokeAsync(["add", "item.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add item.txt"], approval);

        var targetDir = Path.Combine(testRepo.WorkingDirectory, "target_dir");
        Directory.CreateDirectory(targetDir);

        // Standard git: 'git mv item.txt target_dir' places the file at target_dir/item.txt
        var response = await emulator.InvokeAsync(["mv", "item.txt", "target_dir"], approval);
        Assert.Equal(0, response.ExitCode);

        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("target_dir/item.txt", status.StdOut);
        Assert.True(File.Exists(Path.Combine(targetDir, "item.txt")));
    }

    [Fact]
    public async Task Mv_MultipleFilesToExistingDirectory_MovesAllInsideDirectory_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "B");
        await emulator.InvokeAsync(["add", "a.txt", "b.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add a and b"], approval);

        var targetDir = Path.Combine(testRepo.WorkingDirectory, "dest");
        Directory.CreateDirectory(targetDir);

        // Standard git: 'git mv a.txt b.txt dest' moves both files into dest/
        var response = await emulator.InvokeAsync(["mv", "a.txt", "b.txt", "dest"], approval);
        Assert.Equal(0, response.ExitCode);

        var status = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("dest/a.txt", status.StdOut);
        Assert.Contains("dest/b.txt", status.StdOut);
    }

    [Fact]
    public async Task Restore_FileInWorkingTree_DiscardsUnstagedModifications_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var filePath = Path.Combine(testRepo.WorkingDirectory, "restore_test.txt");
        await File.WriteAllTextAsync(filePath, "committed content\n");
        await emulator.InvokeAsync(["add", "restore_test.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add restore_test.txt"], approval);

        // Modify in working tree
        await File.WriteAllTextAsync(filePath, "dirty modification\n");

        // Standard git: 'git restore restore_test.txt' discards unstaged changes
        var response = await emulator.InvokeAsync(["restore", "restore_test.txt"], approval);
        Assert.Equal(0, response.ExitCode);

        var content = await File.ReadAllTextAsync(filePath);
        Assert.Equal("committed content\n", content);
    }

    [Fact]
    public async Task Restore_Staged_UnstagesFileFromIndex_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var filePath = Path.Combine(testRepo.WorkingDirectory, "staged_file.txt");
        await File.WriteAllTextAsync(filePath, "staged content\n");
        await emulator.InvokeAsync(["add", "staged_file.txt"], approval);

        // Verify staged
        var statusBefore = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("A  staged_file.txt", statusBefore.StdOut);

        // Standard git: 'git restore --staged staged_file.txt' unstages file
        var response = await emulator.InvokeAsync(["restore", "--staged", "staged_file.txt"], approval);
        Assert.Equal(0, response.ExitCode);

        var statusAfter = await emulator.InvokeAsync(["status", "--porcelain"], approval);
        Assert.Contains("?? staged_file.txt", statusAfter.StdOut);
        Assert.DoesNotContain("A  staged_file.txt", statusAfter.StdOut);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task Restore_Source_RestoresFileFromCommit_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var filePath = Path.Combine(testRepo.WorkingDirectory, "hist.txt");
        await File.WriteAllTextAsync(filePath, "v1");
        await emulator.InvokeAsync(["add", "hist.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "v1"], approval);

        await File.WriteAllTextAsync(filePath, "v2");
        await emulator.InvokeAsync(["add", "hist.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "v2"], approval);

        // Standard git: 'git restore --source=HEAD~1 hist.txt' restores hist.txt to v1 content
        var response = await emulator.InvokeAsync(["restore", "--source=HEAD~1", "hist.txt"], approval);
        Assert.Equal(0, response.ExitCode);

        var content = await File.ReadAllTextAsync(filePath);
        Assert.Equal("v1", content);
    }

    [Fact]
    public async Task Checkout_DoubleDash_DiscardsUnstagedModifications_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var filePath = Path.Combine(testRepo.WorkingDirectory, "checkout_file.txt");
        await File.WriteAllTextAsync(filePath, "clean content\n");
        await emulator.InvokeAsync(["add", "checkout_file.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add checkout_file.txt"], approval);

        await File.WriteAllTextAsync(filePath, "modified content\n");

        // Standard git: 'git checkout -- checkout_file.txt' restores clean content
        var response = await emulator.InvokeAsync(["checkout", "--", "checkout_file.txt"], approval);
        Assert.Equal(0, response.ExitCode);

        var content = await File.ReadAllTextAsync(filePath);
        Assert.Equal("clean content\n", content);
    }

    [Fact]
    public async Task Checkout_Dash_SwitchesToPreviousBranch_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["checkout", "-b", "feature-x"], approval);
        Assert.Equal("feature-x", await repo.GetCurrentBranchNameAsync());

        await emulator.InvokeAsync(["checkout", "master"], approval);
        Assert.Equal("master", await repo.GetCurrentBranchNameAsync());

        // Standard git: 'git checkout -' switches back to feature-x
        var response = await emulator.InvokeAsync(["checkout", "-"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.Equal("feature-x", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Checkout_CreateBranchWithStartPoint_CreatesFromSpecifiedCommit_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        var parentHash = testRepo.Head;
        testRepo.Commit("Commit 3", ("f3.txt", "3"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git checkout -b <branch> <start-point>' creates branch at start-point
        var response = await emulator.InvokeAsync(["checkout", "-b", "branched-from-parent", "HEAD~1"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.Equal("branched-from-parent", await repo.GetCurrentBranchNameAsync());

        var currentCommit = await repo.GetCommitAsync("HEAD");
        Assert.Equal(parentHash, currentCommit.Id);
    }

    [Fact]
    public async Task Branch_RenameCurrentBranch_UpdatesHead_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["switch", "-c", "my-topic"], approval);
        Assert.Equal("my-topic", await repo.GetCurrentBranchNameAsync());

        // Standard git: 'git branch -m my-renamed-topic' renames current branch
        var response = await emulator.InvokeAsync(["branch", "-m", "my-renamed-topic"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.Equal("my-renamed-topic", await repo.GetCurrentBranchNameAsync());

        var showCurrent = await emulator.InvokeAsync(["branch", "--show-current"], approval);
        Assert.Equal("my-renamed-topic", showCurrent.StdOut.Trim());
    }

    [Fact]
    public async Task Branch_DeleteUnmergedBranchWithoutForce_Fails_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["switch", "-c", "unmerged-branch"], approval);
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "unmerged.txt"), "unmerged");
        await emulator.InvokeAsync(["add", "unmerged.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Unmerged commit"], approval);

        await emulator.InvokeAsync(["switch", "master"], approval);

        // Standard git: 'git branch -d unmerged-branch' fails because it is not merged
        var response = await emulator.InvokeAsync(["branch", "-d", "unmerged-branch"], approval);
        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("not fully merged", response.StdErr + response.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Switch_ConflictingWorkingTreeFile_AbortsAndPreservesChanges_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var sharedFile = Path.Combine(testRepo.WorkingDirectory, "shared.txt");
        await File.WriteAllTextAsync(sharedFile, "master content\n");
        await emulator.InvokeAsync(["add", "shared.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Master shared"], approval);

        // Create feature branch with different content in shared.txt
        await emulator.InvokeAsync(["switch", "-c", "feature-diff"], approval);
        await File.WriteAllTextAsync(sharedFile, "feature content\n");
        await emulator.InvokeAsync(["add", "shared.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Feature shared"], approval);

        // Switch back to master
        await emulator.InvokeAsync(["switch", "master"], approval);

        // Modify shared.txt locally in worktree without committing
        await File.WriteAllTextAsync(sharedFile, "local dirty content\n");

        // Standard git: 'git switch feature-diff' MUST abort because local changes would be overwritten
        var response = await emulator.InvokeAsync(["switch", "feature-diff"], approval);

        Assert.NotEqual(0, response.ExitCode);

        // Local dirty content must be preserved!
        var contentAfter = await File.ReadAllTextAsync(sharedFile);
        Assert.Equal("local dirty content\n", contentAfter);
    }

    [Fact]
    public async Task Status_ShortFlag_ProducesShortPorcelainFormat_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var newFile = Path.Combine(testRepo.WorkingDirectory, "newfile.txt");
        await File.WriteAllTextAsync(newFile, "content");
        await emulator.InvokeAsync(["add", "newfile.txt"], approval);

        // Standard git: 'git status -s' gives short status format
        var response = await emulator.InvokeAsync(["status", "-s"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.Contains("A  newfile.txt", response.StdOut);
    }

    [Fact]
    public async Task Diff_CachedFlag_ShowsStagedDifferences_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var readme = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.AppendAllTextAsync(readme, "\nstaged change");
        await emulator.InvokeAsync(["add", "README.md"], approval);

        // Standard git: 'git diff --cached' shows staged changes against HEAD
        var response = await emulator.InvokeAsync(["diff", "--cached"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.Contains("+staged change", response.StdOut);
    }

    [Fact]
    public async Task Log_HyphenNumberShorthand_LimitsCommits_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        testRepo.Commit("Commit 3", ("f3.txt", "3"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git log -1' is standard shorthand for 'git log -n 1'
        var response = await emulator.InvokeAsync(["log", "--oneline", "-1"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Commit 3", response.StdOut);
        Assert.DoesNotContain("Commit 2", response.StdOut);
    }

    [Fact]
    public async Task Checkout_ForceB_ResetsExistingBranch_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        var commit2Hash = testRepo.Head;
        testRepo.Commit("Commit 3", ("f3.txt", "3"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git checkout -B master HEAD~1' force-resets master to HEAD~1
        var response = await emulator.InvokeAsync(["checkout", "-B", "master", "HEAD~1"], approval);
        Assert.Equal(0, response.ExitCode);

        var currentHead = await repo.GetCommitAsync("HEAD");
        Assert.Equal(commit2Hash, currentHead.Id);
    }

    [Fact]
    public async Task Switch_ForceC_ResetsExistingBranch_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        var commit2Hash = testRepo.Head;
        testRepo.Commit("Commit 3", ("f3.txt", "3"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git switch -C master HEAD~1' force-resets master to HEAD~1
        var response = await emulator.InvokeAsync(["switch", "-C", "master", "HEAD~1"], approval);
        Assert.Equal(0, response.ExitCode);

        var currentHead = await repo.GetCommitAsync("HEAD");
        Assert.Equal(commit2Hash, currentHead.Id);
    }

    [Fact]
    public async Task Reset_Hard_LeavesUntrackedFilesIntact_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var untracked = Path.Combine(testRepo.WorkingDirectory, "untracked_reset.txt");
        await File.WriteAllTextAsync(untracked, "preserve me\n");

        // Standard git: 'git reset --hard' resets tracked files, but leaves untracked files in the working directory
        var response = await emulator.InvokeAsync(["reset", "--hard", "HEAD"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.True(File.Exists(untracked), "Untracked files should not be deleted by git reset --hard");
    }

    [Fact]
    public async Task Tag_DeleteNonExistentTag_Fails_LikeStandardGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Standard git: 'git tag -d non_existent_tag' fails with exit code 1 (error: tag '...' not found)
        var response = await emulator.InvokeAsync(["tag", "-d", "non_existent_tag"], approval);
        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("not found", response.StdErr + response.StdOut, StringComparison.OrdinalIgnoreCase);
    }
}
