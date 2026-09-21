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

        // Create merge commit with parents commitA and commitB
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file_b.txt"), "content B");
        await repo.StageAsync("file_b.txt");
        var mergeTree = await repo.IndexManager.Repository.WriteTreeAsync(await GitIndex.ReadAsync(repo.IndexManager.IndexPath));
        var mergePayload = GitRepository.BuildCommitPayload(mergeTree, new[] { commitA, commitB }, new GitCommitMetadata("Merge branch feature-b", TestSignature));
        var mergeCommit = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Commit, mergePayload);

        var currentBranch = await repo.ReferenceStore.GetCurrentBranchNameAsync();
        await repo.ReferenceStore.CreateReferenceAsync($"refs/heads/{currentBranch}", mergeCommit, overwrite: true);
        repo.InvalidateCaches();

        // Native git fsck must pass
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);

        // Native git rev-list --parents must show 2 parents for merge commit
        var revList = testRepo.RunGit("rev-list --parents -n 1 HEAD").Trim();
        var parts = revList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, parts.Length); // HEAD commitA commitB
        Assert.Equal(mergeCommit.ToString(), parts[0]);
        Assert.Equal(commitA.ToString(), parts[1]);
        Assert.Equal(commitB.ToString(), parts[2]);

        // Native git log --graph must succeed
        var logGraph = testRepo.RunGit("log --graph --oneline -n 4");
        Assert.Contains("Merge branch feature-b", logGraph);
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
}

