using System.IO;
using System.Text;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitWorkspaceCliInteropTests
{
    private static readonly GitCommitSignature TestSignature = new("Author", "author@example.com", DateTimeOffset.UtcNow);

    [Fact]
    public async Task GitFsck_AfterCommitAndAmend_ReportsNoCorruption()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Commit two files through managed repository
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file1.txt"), "hello file 1\n");
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "src"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "src", "file2.txt"), "hello file 2\n");

        await repo.StageAsync("file1.txt");
        await repo.StageAsync("src/file2.txt");

        var commit1 = await repo.CommitAsync("Feature 1", new GitCommitMetadata("Feature 1", TestSignature));
        Assert.NotEqual(GitHash.Zero, commit1);

        // Amend the commit with updated file1
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file1.txt"), "hello file 1 updated\n");
        await repo.StageAsync("file1.txt");

        var commit2 = await repo.CommitAmendAsync("Feature 1 Amended", new GitCommitMetadata("Feature 1 Amended", TestSignature));
        Assert.NotEqual(commit1, commit2);

        // Native git fsck --full --strict must pass cleanly without corruption
        var fsckOutput = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsckOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("broken link", fsckOutput, StringComparison.OrdinalIgnoreCase);

        // Verify git status is completely clean
        var status = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(status);

        // Verify git log sees amended message
        var log = testRepo.RunGit("log -1 --pretty=%B").Trim();
        Assert.Equal("Feature 1 Amended", log);
    }

    [Fact]
    public async Task GitFsck_AfterSquashRange_ReportsCleanRepository()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var baseCommit = await repo.GetCommitAsync("HEAD");

        // Create commit B
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "feat_b.txt"), "feat b content\n");
        await repo.StageAsync("feat_b.txt");
        await repo.CommitAsync("Commit B", new GitCommitMetadata("Commit B", TestSignature));

        // Create commit C
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "feat_c.txt"), "feat c content\n");
        await repo.StageAsync("feat_c.txt");
        await repo.CommitAsync("Commit C", new GitCommitMetadata("Commit C", TestSignature));

        // Squash range baseCommit..HEAD
        var squashedHash = await repo.SquashRangeAsync(baseCommit.Id, "Squashed B and C", new GitCommitMetadata("Squashed B and C", TestSignature));
        Assert.NotEqual(GitHash.Zero, squashedHash);

        // Native git fsck must pass
        var fsckOutput = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsckOutput, StringComparison.OrdinalIgnoreCase);

        // Native git diff against parent must contain both feat_b and feat_c
        var diff = testRepo.RunGit("diff --name-only HEAD~1..HEAD");
        Assert.Contains("feat_b.txt", diff);
        Assert.Contains("feat_c.txt", diff);

        // Verify git status is clean
        var status = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(status);
    }

    [Fact]
    public async Task GitFsck_AfterRevert_ReportsCleanRepository()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Create a commit adding a feature file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "to_revert.txt"), "temporary feature\n");
        await repo.StageAsync("to_revert.txt");
        var featureCommit = await repo.CommitAsync("Add feature to revert", new GitCommitMetadata("Add feature to revert", TestSignature));

        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "to_revert.txt")));

        // Revert the commit
        var revertCommit = await repo.RevertAsync(featureCommit);
        Assert.NotEqual(GitHash.Zero, revertCommit);

        // File must be deleted on disk
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "to_revert.txt")));

        // Native git fsck must pass
        var fsckOutput = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsckOutput, StringComparison.OrdinalIgnoreCase);

        // Native git status must be clean
        var status = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(status);
    }

    [Fact]
    public async Task InterleavedStaging_ManagedStage_VerifiedByGitCliDiff()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "sub"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "sub", "hello.txt"), "greetings from sub\n");

        // Stage via managed index manager
        await repo.StageAsync("sub/hello.txt");

        // Git CLI diff --cached must show sub/hello.txt
        var cachedDiff = testRepo.RunGit("diff --cached");
        Assert.Contains("diff --git a/sub/hello.txt b/sub/hello.txt", cachedDiff);
        Assert.Contains("+greetings from sub", cachedDiff);

        // Git CLI status must show staged addition
        var porcelain = testRepo.RunGit("status --porcelain");
        Assert.Contains("A  sub/hello.txt", porcelain);
    }

    [Fact]
    public async Task InterleavedStaging_GitCliAdd_UnstagedByManagedCode()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "cli1.txt"), "cli file 1\n");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "cli2.txt"), "cli file 2\n");

        // Stage via native git CLI
        testRepo.RunGit("add cli1.txt cli2.txt");

        var statusBefore = testRepo.RunGit("status --porcelain");
        Assert.Contains("A  cli1.txt", statusBefore);
        Assert.Contains("A  cli2.txt", statusBefore);

        // Unstage cli1.txt via managed code
        await repo.UnstageAsync("cli1.txt");

        // Git CLI status must now report cli1.txt as untracked and cli2.txt as staged
        var statusAfter = testRepo.RunGit("status --porcelain");
        Assert.Contains("?? cli1.txt", statusAfter);
        Assert.Contains("A  cli2.txt", statusAfter);
    }

    [Fact]
    public async Task ResetHard_RestoresFilesAndModes_VerifiedByGitStatus()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Initial commit with a normal file and an executable script
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "tracked.txt"), "original\n");
        var scriptPath = Path.Combine(testRepo.WorkingDirectory, "run.sh");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\necho hello\n");

        await repo.StageAsync("tracked.txt");
        await repo.StageAsync("run.sh");

        // Mark run.sh as executable (mode 100755 / 33261) in index and commit
        var initialIndex = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        var runEntry = initialIndex.FindEntry("run.sh")!;
        runEntry.FileMode = 33261;
        await initialIndex.WriteAsync(repo.IndexManager.IndexPath);

        var c1 = await repo.CommitAsync("Initial commit", new GitCommitMetadata("Initial commit", TestSignature));

        // Verify git ls-tree records run.sh as 100755
        var lsTree = testRepo.RunGit("ls-tree HEAD run.sh");
        Assert.StartsWith("100755", lsTree);

        // Create dirty modifications
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "tracked.txt"), "dirty modifications\n");
        await repo.StageAsync("tracked.txt");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "tracked.txt"), "even dirtier working tree\n");

        // Mutate executable script mode on disk (non-Windows) and in index
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        var dirtyIndex = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        var dirtyEntry = dirtyIndex.FindEntry("run.sh")!;
        dirtyEntry.FileMode = 33188;
        await dirtyIndex.WriteAsync(repo.IndexManager.IndexPath);

        // Hard reset back to c1
        await repo.ResetAsync(c1, GitResetMode.Hard);

        // Working tree file content must be restored
        var text = await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "tracked.txt"));
        Assert.Equal("original\n", text);

        // Executable mode must be restored on supported platforms
        if (!OperatingSystem.IsWindows())
        {
            var unixMode = File.GetUnixFileMode(scriptPath);
            Assert.True((unixMode & UnixFileMode.UserExecute) != 0, "Executable bit should be restored on Unix");
        }

        // Restored index must have mode 33261
        var restoredIndex = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        Assert.Equal(33261, restoredIndex.FindEntry("run.sh")!.FileMode);

        // Git CLI status must be completely clean
        var status = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(status);
    }

    [Fact]
    public async Task WorkspaceSync_BranchCommit_VerifiedByGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var currentBranch = await repo.ReferenceStore.GetCurrentBranchNameAsync();
        Assert.NotNull(currentBranch);

        // Create commit directly on current branch via object store
        var commitHash = await repo.CreateCommitAsync(
            currentBranch,
            new GitCommitOperation[]
            {
                new AddFileOperation("generated.txt", Encoding.UTF8.GetBytes("auto-generated content\n"))
            },
            new GitCommitMetadata("Direct commit on branch", TestSignature));

        // Workspace and index were synchronized
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "generated.txt")));
        Assert.Equal("auto-generated content\n", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "generated.txt")));

        // Git CLI agrees status is clean and HEAD contains the file
        var status = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(status);

        var showOutput = testRepo.RunGit("show HEAD:generated.txt");
        Assert.Equal("auto-generated content\n", showOutput);
    }

    [Fact]
    public async Task GitFsck_TreeWithDirectoryAndSimilarPrefixedFiles_ReportsNoCorruption()
    {
        // Tests the critical Git tree sorting rule where directory names compare with trailing '/'
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // In ASCII: '-' (45) < '.' (46) < '/' (47) < '0' (48) < '_' (95)
        // With directory "dir", Git compares "dir/" against "dir.txt", "dir-other.txt", etc.
        // Canonical order must be:
        // 1. "dir-other.txt"
        // 2. "dir.txt"
        // 3. "dir/" (directory)
        // 4. "dir0.txt"
        // 5. "dir_other.txt"
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "dir-other.txt"), "content -");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "dir.txt"), "content .");
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "dir"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "dir", "child.txt"), "child content");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "dir0.txt"), "content 0");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "dir_other.txt"), "content _");

        await repo.StageAllAsync();
        var commit = await repo.CommitAsync("Tree sorting test", new GitCommitMetadata("Tree sorting test", TestSignature));
        Assert.NotEqual(GitHash.Zero, commit);

        // Native git fsck --full --strict MUST pass cleanly without "contains unsorted entries" error
        var fsckOutput = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error in tree", fsckOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unsorted", fsckOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("broken link", fsckOutput, StringComparison.OrdinalIgnoreCase);

        // Verify git ls-tree outputs the entries in exact order
        var lsTree = testRepo.RunGit("ls-tree HEAD");
        var lines = lsTree.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t')[1].Trim())
            .ToList();

        Assert.Contains("dir-other.txt", lines);
        Assert.Contains("dir.txt", lines);
        Assert.Contains("dir", lines);
        Assert.Contains("dir0.txt", lines);
        Assert.Contains("dir_other.txt", lines);

        var idxDash = lines.IndexOf("dir-other.txt");
        var idxDot = lines.IndexOf("dir.txt");
        var idxDir = lines.IndexOf("dir");
        var idxZero = lines.IndexOf("dir0.txt");
        var idxUnder = lines.IndexOf("dir_other.txt");

        Assert.True(idxDash < idxDot, "dir-other.txt must come before dir.txt");
        Assert.True(idxDot < idxDir, "dir.txt must come before directory dir");
        Assert.True(idxDir < idxZero, "directory dir must come before dir0.txt");
        Assert.True(idxZero < idxUnder, "dir0.txt must come before dir_other.txt");
    }

    [Fact]
    public async Task GitMerge_MultiParentCommit_VerifiedByGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var c1 = await repo.GetCommitAsync("HEAD");

        // Branch 1: commit file_a
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file_a.txt"), "content A");
        await repo.StageAsync("file_a.txt");
        var commitA = await repo.CommitAsync("Feature A", new GitCommitMetadata("Feature A", TestSignature));

        // Switch to separate branch ref for Feature B
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file_b.txt"), "content B");
        await repo.StageAsync("file_b.txt");
        var treeB = await repo.IndexManager.Repository.WriteTreeAsync(await GitIndex.ReadAsync(repo.IndexManager.IndexPath));
        var payloadB = GitRepository.BuildCommitPayload(treeB, new[] { c1.Id }, new GitCommitMetadata("Feature B", TestSignature));
        var commitB = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, payloadB);
        await repo.ReferenceStore.CreateReferenceAsync("refs/heads/feature-b", commitB, overwrite: true);

        // Switch to separate branch ref for Feature C
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file_c.txt"), "content C");
        await repo.StageAsync("file_c.txt");
        var treeC = await repo.IndexManager.Repository.WriteTreeAsync(await GitIndex.ReadAsync(repo.IndexManager.IndexPath));
        var payloadC = GitRepository.BuildCommitPayload(treeC, new[] { c1.Id }, new GitCommitMetadata("Feature C", TestSignature));
        var commitC = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, payloadC);
        await repo.ReferenceStore.CreateReferenceAsync("refs/heads/feature-c", commitC, overwrite: true);

        // Create octopus merge commit with 3 parents: commitA, commitB, and commitC
        var mergeTree = await repo.IndexManager.Repository.WriteTreeAsync(await GitIndex.ReadAsync(repo.IndexManager.IndexPath));
        var mergePayload = GitRepository.BuildCommitPayload(mergeTree, new[] { commitA, commitB, commitC }, new GitCommitMetadata("Octopus merge feature-b and feature-c", TestSignature));
        var mergeCommit = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, mergePayload);

        var currentBranch = await repo.ReferenceStore.GetCurrentBranchNameAsync();
        await repo.ReferenceStore.CreateReferenceAsync($"refs/heads/{currentBranch}", mergeCommit, overwrite: true);
        repo.InvalidateCaches();

        // Native git fsck must pass
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);

        // Native git rev-list --parents must show 3 parents for octopus merge commit
        var revList = testRepo.RunGit("rev-list --parents -n 1 HEAD").Trim();
        var parts = revList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, parts.Length); // HEAD commitA commitB commitC
        Assert.Equal(mergeCommit.ToString(), parts[0]);
        Assert.Equal(commitA.ToString(), parts[1]);
        Assert.Equal(commitB.ToString(), parts[2]);
        Assert.Equal(commitC.ToString(), parts[3]);

        // Native git log --graph must succeed
        var logGraph = testRepo.RunGit("log --graph --oneline -n 5");
        Assert.Contains("Octopus merge feature-b and feature-c", logGraph);
    }

    [Fact]
    public async Task GitTag_CreateAndVerifyWithGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var head = await repo.ReferenceStore.ResolveHeadAsync();

        // Create a lightweight tag
        await repo.ReferenceStore.CreateReferenceAsync("refs/tags/v1.0.0", head);
        repo.InvalidateCaches();

        // Native git tag -l must list v1.0.0
        var tagList = testRepo.RunGit("tag -l").Trim();
        Assert.Contains("v1.0.0", tagList);

        // Native git rev-parse v1.0.0 must match head
        var parsedTag = testRepo.RunGit("rev-parse v1.0.0").Trim();
        Assert.Equal(head.ToString(), parsedTag);

        // Native git describe --tags must report v1.0.0
        var describe = testRepo.RunGit("describe --tags").Trim();
        Assert.Equal("v1.0.0", describe);
    }

    [Fact]
    public async Task GitCli_StagedAndUnstagedModifications_TwoStageDiffInterop()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "two_stage.txt");
        await File.WriteAllTextAsync(filePath, "line 1\n");
        await repo.StageAsync("two_stage.txt");
        await repo.CommitAsync("Initial two_stage", new GitCommitMetadata("Initial two_stage", TestSignature));

        // Modify and stage (staged change: v1 -> v2)
        await File.WriteAllTextAsync(filePath, "line 1\nline 2 (staged)\n");
        await repo.StageAsync("two_stage.txt");

        // Modify again in working tree without staging (unstaged change: v2 -> v3)
        await File.WriteAllTextAsync(filePath, "line 1\nline 2 (staged)\nline 3 (unstaged)\n");

        // Native git status must report MM (staged modified, working tree modified)
        var porcelain = testRepo.RunGit("status --porcelain");
        Assert.Contains("MM two_stage.txt", porcelain);

        // Native git diff --cached (staged diff vs HEAD)
        var cachedDiff = testRepo.RunGit("diff --cached two_stage.txt");
        Assert.Contains("+line 2 (staged)", cachedDiff);
        Assert.DoesNotContain("line 3 (unstaged)", cachedDiff);

        // Native git diff (unstaged diff: working tree vs index)
        var unstagedDiff = testRepo.RunGit("diff two_stage.txt");
        Assert.Contains("+line 3 (unstaged)", unstagedDiff);

        // Managed repository status inspection
        var status = await repo.GetStatusAsync();
        var entry = status.Entries.FirstOrDefault(e => e.Path == "two_stage.txt");
        Assert.NotNull(entry);
        Assert.Equal(GitFileStatus.StagedModified, entry.StagedStatus);
        Assert.Equal(GitFileStatus.Modified, entry.WorkingTreeStatus);

        // Commit remaining and verify clean git status
        await repo.StageAsync("two_stage.txt");
        await repo.CommitAsync("Commit full two_stage", new GitCommitMetadata("Commit full two_stage", TestSignature));

        var finalPorcelain = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(finalPorcelain);
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitCli_FileRemoval_InteropBetweenManagedAndNative()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Create two files
        var file1 = Path.Combine(testRepo.WorkingDirectory, "remove_managed.txt");
        var file2 = Path.Combine(testRepo.WorkingDirectory, "remove_native.txt");
        await File.WriteAllTextAsync(file1, "managed removal\n");
        await File.WriteAllTextAsync(file2, "native removal\n");
        await repo.StageAllAsync();
        await repo.CommitAsync("Add files to remove", new GitCommitMetadata("Add files to remove", TestSignature));

        // Part 1: Delete file1 from working tree and stage removal via managed code
        File.Delete(file1);
        await repo.StageAsync("remove_managed.txt");

        // Native git status must report staged deletion (D )
        var status1 = testRepo.RunGit("status --porcelain");
        Assert.Contains("D  remove_managed.txt", status1);

        // Commit managed deletion
        await repo.CommitAsync("Removed via managed code", new GitCommitMetadata("Removed via managed code", TestSignature));

        var logAfter1 = testRepo.RunGit("log -1 --name-status");
        Assert.Contains("D\tremove_managed.txt", logAfter1);

        // Part 2: Native git rm on file2
        testRepo.RunGit("rm remove_native.txt");

        // Managed repo status sees staged deletion
        var statusResult = await repo.GetStatusAsync();
        var entry2 = statusResult.Entries.FirstOrDefault(e => e.Path == "remove_native.txt");
        Assert.NotNull(entry2);
        Assert.Equal(GitFileStatus.StagedDeleted, entry2.StagedStatus);

        // Commit native deletion via managed repo
        await repo.CommitAsync("Removed via native git rm", new GitCommitMetadata("Removed via native git rm", TestSignature));

        // Native git status clean and fsck passes
        var finalStatus = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(finalStatus);
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitCli_BranchCheckoutAndSwitch_Interop()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var defaultBranch = await repo.ReferenceStore.GetCurrentBranchNameAsync();
        Assert.NotNull(defaultBranch);

        // Create feature branch pointing to current HEAD
        var headCommit = await repo.ReferenceStore.ResolveHeadAsync();
        await repo.ReferenceStore.CreateReferenceAsync("refs/heads/feature", headCommit);

        // Switch to feature branch by updating HEAD
        await File.WriteAllTextAsync(Path.Combine(testRepo.GitDirectory, "HEAD"), "ref: refs/heads/feature\n");
        repo.InvalidateCaches();

        // Native git CLI verifies current branch is feature
        var nativeCurrentBranch = testRepo.RunGit("branch --show-current").Trim();
        Assert.Equal("feature", nativeCurrentBranch);

        // Commit on feature branch
        var featFile = Path.Combine(testRepo.WorkingDirectory, "feature_only.txt");
        await File.WriteAllTextAsync(featFile, "feature branch content\n");
        await repo.StageAsync("feature_only.txt");
        await repo.CommitAsync("Feature commit", new GitCommitMetadata("Feature commit", TestSignature));

        Assert.True(File.Exists(featFile));

        // Switch back to master using native git CLI
        testRepo.RunGit($"checkout {defaultBranch} --quiet");

        // Managed repo observes defaultBranch
        repo.InvalidateCaches();
        var currentAfterNativeSwitch = await repo.ReferenceStore.GetCurrentBranchNameAsync();
        Assert.Equal(defaultBranch, currentAfterNativeSwitch);

        // Feature file does not exist on default branch
        Assert.False(File.Exists(featFile));

        // Native git fsck passes
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitCli_AnnotatedTag_ManagedCreation_VerifiedByNativeCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var head = await repo.ReferenceStore.ResolveHeadAsync();

        // Build native-format annotated tag object
        var tagMessage = "Release v2.0.0 annotated tag description";
        var tagPayload = Encoding.UTF8.GetBytes(
            $"object {head}\n" +
            "type commit\n" +
            "tag v2.0.0-annotated\n" +
            "tagger Test Author <author@example.com> 1700000000 +0000\n\n" +
            $"{tagMessage}\n");

        var tagHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Tag, tagPayload);
        await repo.ReferenceStore.CreateReferenceAsync("refs/tags/v2.0.0-annotated", tagHash);
        repo.InvalidateCaches();

        // Native git tag -l must list v2.0.0-annotated
        var tagList = testRepo.RunGit("tag -l").Trim();
        Assert.Contains("v2.0.0-annotated", tagList);

        // Native git cat-file -t must return "tag"
        var objType = testRepo.RunGit("cat-file -t v2.0.0-annotated").Trim();
        Assert.Equal("tag", objType);

        // Native git cat-file -p must show tag payload
        var objContent = testRepo.RunGit("cat-file -p v2.0.0-annotated");
        Assert.Contains(tagMessage, objContent);

        // Native git rev-parse with peel operator ^{commit} must match HEAD commit
        var peeledCommit = testRepo.RunGit("rev-parse v2.0.0-annotated^{commit}").Trim();
        Assert.Equal(head.ToString(), peeledCommit, StringComparer.OrdinalIgnoreCase);

        // Native git describe --tags must output the tag
        var describe = testRepo.RunGit("describe --tags").Trim();
        Assert.Equal("v2.0.0-annotated", describe);

        // Native git tag -n shows annotation
        var tagWithAnnotation = testRepo.RunGit("tag -n9 -l v2.0.0-annotated");
        Assert.Contains(tagMessage, tagWithAnnotation);

        // Native git fsck --full --strict must pass cleanly
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitCli_GitIgnore_Interop()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Create .gitignore
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "*.log\nignored/\n");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "normal.txt"), "normal content\n");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "app.log"), "log content\n");

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "ignored", "secret.txt"), "secret\n");

        // Native git status must only report .gitignore and normal.txt as untracked
        var nativeStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains(".gitignore", nativeStatus);
        Assert.Contains("normal.txt", nativeStatus);
        Assert.DoesNotContain("app.log", nativeStatus);
        Assert.DoesNotContain("ignored", nativeStatus);

        // Managed repository status inspection must also respect .gitignore
        var statusResult = await repo.GetStatusAsync();
        var paths = statusResult.Entries.Select(e => e.Path).ToList();
        Assert.Contains(".gitignore", paths);
        Assert.Contains("normal.txt", paths);
        Assert.DoesNotContain("app.log", paths);
        Assert.DoesNotContain("ignored/secret.txt", paths);

        // Stage all and commit via managed code
        await repo.StageAllAsync();
        await repo.CommitAsync("Commit with gitignore", new GitCommitMetadata("Commit with gitignore", TestSignature));

        // Native git ls-files must only have .gitignore, normal.txt, and README.md
        var lsFiles = testRepo.RunGit("ls-files");
        Assert.Contains(".gitignore", lsFiles);
        Assert.Contains("normal.txt", lsFiles);
        Assert.DoesNotContain("app.log", lsFiles);
        Assert.DoesNotContain("secret.txt", lsFiles);

        // Native git status is completely clean
        var finalStatus = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(finalStatus);

        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitCli_BinaryFileIntegrity_Interop()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Create a 64KB binary buffer containing every byte value 0..255 repeated
        var binaryData = new byte[65536];
        for (int i = 0; i < binaryData.Length; i++)
        {
            binaryData[i] = (byte)(i % 256);
        }

        var binaryPath = Path.Combine(testRepo.WorkingDirectory, "data.bin");
        await File.WriteAllBytesAsync(binaryPath, binaryData);

        await repo.StageAsync("data.bin");
        var commit = await repo.CommitAsync("Add binary file", new GitCommitMetadata("Add binary file", TestSignature));
        Assert.NotEqual(GitHash.Zero, commit);

        // Native git status must be clean
        var porcelain = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(porcelain);

        // Native git diff HEAD~1..HEAD should detect binary file
        var diff = testRepo.RunGit("diff HEAD~1..HEAD");
        Assert.Contains("Binary files", diff);

        // Verify native git fsck --full --strict
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);

        // Reset and checkout back with native git to ensure disk bytes match
        File.Delete(binaryPath);
        testRepo.RunGit("checkout HEAD -- data.bin");
        var readBack = await File.ReadAllBytesAsync(binaryPath);
        Assert.Equal(binaryData, readBack);
    }
}

