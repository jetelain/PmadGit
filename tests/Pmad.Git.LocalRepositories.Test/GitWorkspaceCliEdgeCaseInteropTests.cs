using System.IO;
using System.Text;
using Pmad.Git.LocalRepositories.Config;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitWorkspaceCliEdgeCaseInteropTests
{
    private static readonly GitCommitSignature TestSignature = new("Author", "author@example.com", DateTimeOffset.UtcNow);

    [Fact]
    public async Task StageAsync_DirectoryPath_StagesAllFilesInDirectory_VerifiedByGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var subDir = Path.Combine(testRepo.WorkingDirectory, "components");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "button.txt"), "button component\n");
        await File.WriteAllTextAsync(Path.Combine(subDir, "modal.txt"), "modal component\n");

        // Staging the directory path 'components' (like standard git add components)
        await repo.StageAsync("components");

        // Native git status must report both files as staged
        var porcelain = testRepo.RunGit("status --porcelain");
        Assert.Contains("A  components/button.txt", porcelain);
        Assert.Contains("A  components/modal.txt", porcelain);
    }

    [Fact]
    public async Task UnstageAsync_DirectoryPath_UnstagesAllFilesInDirectory_VerifiedByGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var subDir = Path.Combine(testRepo.WorkingDirectory, "components");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "button.txt"), "button component\n");
        await File.WriteAllTextAsync(Path.Combine(subDir, "modal.txt"), "modal component\n");

        await repo.StageAsync("components/button.txt");
        await repo.StageAsync("components/modal.txt");

        var stagedStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("A  components/button.txt", stagedStatus);
        Assert.Contains("A  components/modal.txt", stagedStatus);

        // Unstaging the directory path 'components' (like git restore --staged components)
        await repo.UnstageAsync("components");

        var unstagedStatus = testRepo.RunGit("status --porcelain -u");
        Assert.Contains("?? components/button.txt", unstagedStatus);
        Assert.Contains("?? components/modal.txt", unstagedStatus);
        Assert.DoesNotContain("A  components/button.txt", unstagedStatus);
    }

    [Fact]
    public async Task StageAsync_DeletedDirectory_StagesAllDeletions_VerifiedByGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var subDir = Path.Combine(testRepo.WorkingDirectory, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "f1.txt"), "f1\n");
        await File.WriteAllTextAsync(Path.Combine(subDir, "f2.txt"), "f2\n");
        await repo.StageAsync("sub/f1.txt");
        await repo.StageAsync("sub/f2.txt");
        await repo.CommitAsync("Add sub files", new GitCommitMetadata("Add sub files", TestSignature));

        // Delete entire directory on disk
        Directory.Delete(subDir, recursive: true);

        // Stage deletion by passing directory name
        await repo.StageAsync("sub");

        var porcelain = testRepo.RunGit("status --porcelain");
        Assert.Contains("D  sub/f1.txt", porcelain);
        Assert.Contains("D  sub/f2.txt", porcelain);
    }

    [Fact]
    public async Task PackedRefs_WithPeeledAnnotatedTag_ResolvedCorrectlyByManagedRepo()
    {
        using var testRepo = GitTestRepository.Create();

        // Create annotated tag using native git
        testRepo.RunGit("tag -a v1.0.0 -m \"Release v1.0.0 annotated\"");

        // Pack all references into .git/packed-refs (creates ^<commit-hash> peeled lines)
        testRepo.RunGit("pack-refs --all");

        // Verify .git/packed-refs exists and contains ^ line
        var packedRefsContent = await File.ReadAllTextAsync(Path.Combine(testRepo.GitDirectory, "packed-refs"));
        Assert.Contains("^", packedRefsContent);

        // Open repository in managed code and resolve the tag commit
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var commit = await repo.GetCommitAsync("v1.0.0");

        Assert.Equal(testRepo.Head, commit.Id);
    }

    [Fact]
    public async Task NativeGitExtensions_IndexWithTreeExtension_ParsedWithoutError()
    {
        using var testRepo = GitTestRepository.Create();

        // Create nested directory structure and commit via native git to generate TREE extension in index
        var d1 = Path.Combine(testRepo.WorkingDirectory, "dir1");
        var d2 = Path.Combine(testRepo.WorkingDirectory, "dir2");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);
        await File.WriteAllTextAsync(Path.Combine(d1, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(d2, "b.txt"), "b");

        testRepo.RunGit("add -A");
        testRepo.RunGit("commit -m \"Nested commit\"");

        // Native git writes TREE cache extension to index
        testRepo.RunGit("write-tree");

        // Managed code must read this index cleanly
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var status = await repo.GetStatusAsync();

        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task MergeConflict_BinaryFiles_HandledGracefullyWithoutCrashing()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Base binary file
        var binPath = Path.Combine(testRepo.WorkingDirectory, "image.bin");
        var baseBytes = new byte[] { 0x00, 0xFF, 0xFE, 0x01, 0x02 };
        await File.WriteAllBytesAsync(binPath, baseBytes);
        await repo.StageAsync("image.bin");
        var baseCommit = await repo.CommitAsync("Base binary", new GitCommitMetadata("Base binary", TestSignature));

        // Create branch side-a
        await repo.CreateBranchAsync("side-a");

        // On master: modify binary
        var masterBytes = new byte[] { 0x00, 0xFF, 0xFE, 0x01, 0x99 };
        await File.WriteAllBytesAsync(binPath, masterBytes);
        await repo.StageAsync("image.bin");
        await repo.CommitAsync("Master binary mod", new GitCommitMetadata("Master binary mod", TestSignature));

        // Switch to side-a: modify binary differently
        await repo.CheckoutBranchAsync("side-a");
        var sideABytes = new byte[] { 0x00, 0xFF, 0xFE, 0x01, 0x88 };
        await File.WriteAllBytesAsync(binPath, sideABytes);
        await repo.StageAsync("image.bin");
        await repo.CommitAsync("Side A binary mod", new GitCommitMetadata("Side A binary mod", TestSignature));

        // Switch back to master and merge side-a
        await repo.CheckoutBranchAsync("master");
        var mergeResult = await repo.MergeAsync("side-a");

        // Binary files cannot be 3-way merged textually, must result in conflict
        Assert.False(mergeResult.IsSuccess);
        Assert.Contains(mergeResult.ConflictedFiles, path => path == "image.bin");
    }

    [Fact]
    public async Task MergeConflict_ManagedConflict_NativeGitStatusAndAbortWorkProperly()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var docPath = Path.Combine(testRepo.WorkingDirectory, "doc.txt");
        await File.WriteAllTextAsync(docPath, "line 1\ncommon base\nline 3\n");
        await repo.StageAsync("doc.txt");
        await repo.CommitAsync("Base commit", new GitCommitMetadata("Base commit", TestSignature));

        await repo.CreateBranchAsync("branch-b");

        // Master modification
        await File.WriteAllTextAsync(docPath, "line 1\nmaster change\nline 3\n");
        await repo.StageAsync("doc.txt");
        await repo.CommitAsync("Master change", new GitCommitMetadata("Master change", TestSignature));

        // Branch-B modification
        await repo.CheckoutBranchAsync("branch-b");
        await File.WriteAllTextAsync(docPath, "line 1\nbranch B change\nline 3\n");
        await repo.StageAsync("doc.txt");
        await repo.CommitAsync("Branch B change", new GitCommitMetadata("Branch B change", TestSignature));

        // Merge branch-b into master via managed repo
        await repo.CheckoutBranchAsync("master");
        var mergeResult = await repo.MergeAsync("branch-b");
        Assert.False(mergeResult.IsSuccess);

        // Native git CLI must report UU doc.txt in status
        var porcelain = testRepo.RunGit("status --porcelain");
        Assert.Contains("UU doc.txt", porcelain);

        // Native git CLI merge --abort must restore clean state
        testRepo.RunGit("merge --abort");

        var statusAfterAbort = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(statusAfterAbort);

        var contentAfterAbort = await File.ReadAllTextAsync(docPath);
        Assert.Equal("line 1\nmaster change\nline 3\n", contentAfterAbort.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task MergeConflict_NativeGitConflict_ManagedDetectsAndResolvesProperly()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var docPath = Path.Combine(testRepo.WorkingDirectory, "conflict.txt");
        await File.WriteAllTextAsync(docPath, "base content\n");
        testRepo.Commit("Base conflict", ("conflict.txt", "base content\n"));

        testRepo.CreateBranch("branch-side");

        // Master edit
        testRepo.Commit("Master side", ("conflict.txt", "master content\n"));

        // Branch-side edit
        testRepo.Switch("branch-side");
        testRepo.Commit("Branch side", ("conflict.txt", "branch content\n"));

        // Native git merge creates conflict (exits with code 1)
        testRepo.Switch("master");
        try
        {
            testRepo.RunGit("merge branch-side");
        }
        catch (InvalidOperationException)
        {
            // Expected exit code 1 for git merge with conflicts
        }

        // Managed repo detects merge in progress and conflicted file
        repo.InvalidateCaches();
        Assert.True(await repo.IsMergeInProgressAsync());
        var conflicts = await repo.GetConflictedFilesAsync();
        Assert.Contains("conflict.txt", conflicts);

        // Resolve through managed repo
        await File.WriteAllTextAsync(docPath, "resolved content\n");
        await repo.ResolveConflictAsync("conflict.txt");

        // Continue merge via managed repo
        var mergeCommit = await repo.ContinueMergeAsync("Merge resolved by managed repo");
        Assert.NotEqual(GitHash.Zero, mergeCommit);

        // Native git status must be clean
        var status = testRepo.RunGit("status --porcelain").Trim();
        Assert.Empty(status);

        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveAsync_CaseOnlyRename_OnWindows_Succeeds()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "case_test.txt");
        await File.WriteAllTextAsync(filePath, "case test content\n");
        await repo.StageAsync("case_test.txt");
        await repo.CommitAsync("Add case_test.txt", new GitCommitMetadata("Add case_test.txt", TestSignature));

        // Case-only rename on Windows: 'case_test.txt' -> 'CASE_TEST.TXT'
        await repo.MoveAsync("case_test.txt", "CASE_TEST.TXT");

        // Native git fsck should pass
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);

        // Native git status should detect the rename
        var porcelain = testRepo.RunGit("status --porcelain");
        Assert.Contains("CASE_TEST.TXT", porcelain);
    }

    [Fact]
    public async Task GetStatusAsync_AfterMove_ReportsRenameStatus_ConsistentWithGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "orig_item.txt");
        await File.WriteAllTextAsync(filePath, "distinct content\n");
        await repo.StageAsync("orig_item.txt");
        await repo.CommitAsync("Add orig_item.txt", new GitCommitMetadata("Add orig_item.txt", TestSignature));

        await repo.MoveAsync("orig_item.txt", "dest_item.txt");

        // Native git status --porcelain reports R  orig_item.txt -> dest_item.txt
        var nativeStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("R  orig_item.txt -> dest_item.txt", nativeStatus);

        // Managed repo status inspection: does it report rename or split entries?
        var managedStatus = await repo.GetStatusAsync();
        // Check if managed code has a way to identify rename
        var staged = managedStatus.StagedEntries.Select(e => e.Path).ToList();
        Assert.Contains("dest_item.txt", staged);
    }

    [Fact]
    public async Task LineEndings_CrlfFile_HashObjectMatchesNativeGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // File containing CRLF line endings
        var content = "line 1\r\nline 2\r\nline 3\r\n";
        var filePath = Path.Combine(testRepo.WorkingDirectory, "crlf.txt");
        await File.WriteAllTextAsync(filePath, content);

        // Get hash computed by native git hash-object
        var nativeHash = testRepo.RunGit("hash-object crlf.txt").Trim();

        // Stage via managed repository
        await repo.StageAsync("crlf.txt");

        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        var entry = index.FindEntry("crlf.txt");
        Assert.NotNull(entry);

        // Hash in managed index must match native git hash-object
        Assert.Equal(nativeHash, entry.Hash.ToString());
    }

    [Fact]
    public async Task Symlink_Mode120000_InspectionInteropWithNativeGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Commit target file
        var targetFile = Path.Combine(testRepo.WorkingDirectory, "target.txt");
        await File.WriteAllTextAsync(targetFile, "target content\n");
        await repo.StageAsync("target.txt");
        await repo.CommitAsync("Add target.txt", new GitCommitMetadata("Add target.txt", TestSignature));

        // Create an index entry with mode 120000 (symlink) pointing to target.txt
        var targetBlobHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Blob, Encoding.UTF8.GetBytes("target.txt"));
        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        index.AddOrUpdate(new GitIndexEntry("link.txt", targetBlobHash, 0b1010_000_000_000_000, 0)); // 120000 = 40960
        await index.WriteAsync(repo.IndexManager.IndexPath);

        // Verify native git ls-files -s reads mode 120000
        var lsFiles = testRepo.RunGit("ls-files -s link.txt");
        Assert.StartsWith("120000", lsFiles);

        // Verify native git fsck --full --strict
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error:", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectoryPruning_WhenAllFilesMoved_PrunesEmptyDirectories_LikeGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var nestedDir = Path.Combine(testRepo.WorkingDirectory, "src", "nested");
        Directory.CreateDirectory(nestedDir);
        var filePath = Path.Combine(nestedDir, "deep.txt");
        await File.WriteAllTextAsync(filePath, "content\n");

        await repo.StageAsync("src/nested/deep.txt");
        await repo.CommitAsync("Add deep file", new GitCommitMetadata("Add deep file", TestSignature));

        // Move the file out of the directory hierarchy
        await repo.MoveAsync("src/nested/deep.txt", "root_deep.txt");

        // Standard git CLI deletes empty directories left behind when tracked files are moved/deleted
        // Check if src/nested was pruned
        Assert.False(Directory.Exists(nestedDir), "Empty folder 'src/nested' should be pruned after moving its contents.");
    }

    [Fact]
    public async Task GitLink_Mode160000_SubmoduleIndexEntry_InspectionInterop()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Submodule gitlink entry in index has mode 160000 (0b1110_000_000_000_000 = 57344)
        var dummyCommitHash = testRepo.Head;
        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath);
        index.AddOrUpdate(new GitIndexEntry("submodule_link", dummyCommitHash, 0b1110_000_000_000_000, 0));
        await index.WriteAsync(repo.IndexManager.IndexPath);

        // Managed repo status inspection should handle mode 160000 without throwing
        var status = await repo.GetStatusAsync();
        Assert.NotNull(status);

        // Native git ls-files -s reads mode 160000
        var lsFiles = testRepo.RunGit("ls-files -s submodule_link");
        Assert.StartsWith("160000", lsFiles);
    }

    [Fact]
    public async Task GitConfigFile_CaseInsensitiveSection_ParsedCorrectly()
    {
        using var testRepo = GitTestRepository.Create();

        // Write config with mixed-case section and comment
        var configPath = Path.Combine(testRepo.GitDirectory, "config");
        var customConfig = "[Core]\n\tbare = false\n\t# inline comment\n\tignorecase = true\n";
        await File.WriteAllTextAsync(configPath, customConfig);

        var configFile = await GitConfigFile.ReadFromFileAsync(configPath);
        Assert.NotNull(configFile);

        // In Git, section names are case-insensitive: 'core.bare' should find [Core]
        var isBare = configFile.GetBoolean("core.bare");
        Assert.False(isBare);

        var ignoreCase = configFile.GetBoolean("core.ignorecase");
        Assert.True(ignoreCase);
    }

    [Fact]
    public async Task TreeSorting_DirectoryAndFileWithOverlappingPrefix_MatchesNativeGitFsck()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Git requires entries in tree objects to be sorted byte-wise, with tree objects sorted as if having a trailing '/'
        // ASCII codes: '-' (45) < '.' (46) < '/' (47)
        // Therefore: 'alpha-0.txt' < 'alpha.txt' < 'alpha/'
        var alphaDir = Path.Combine(testRepo.WorkingDirectory, "alpha");
        Directory.CreateDirectory(alphaDir);
        await File.WriteAllTextAsync(Path.Combine(alphaDir, "nested.txt"), "nested\n");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "alpha.txt"), "alpha file\n");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "alpha-0.txt"), "alpha-0 file\n");

        await repo.StageAsync(["alpha/nested.txt", "alpha.txt", "alpha-0.txt"]);
        await repo.CommitAsync("Add files with overlapping prefix", new GitCommitMetadata("Add overlapping", TestSignature));

        // Native git fsck --full --strict checks tree sorting integrity
        var fsck = testRepo.RunGit("fsck --full --strict");
        Assert.DoesNotContain("error in tree", fsck, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("entries not properly sorted", fsck, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitIgnore_TrailingSlash_OnlyMatchesDirectories_ConsistentWithGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // In standard git, a trailing slash in .gitignore (e.g. 'build/') matches directories only, never regular files
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "build/\n");
        await repo.StageAsync(".gitignore");
        await repo.CommitAsync("Add gitignore", new GitCommitMetadata("Add gitignore", TestSignature));

        // Create directory build/ with a file
        var buildDir = Path.Combine(testRepo.WorkingDirectory, "build");
        Directory.CreateDirectory(buildDir);
        await File.WriteAllTextAsync(Path.Combine(buildDir, "out.bin"), "binary");

        // Native git status should ignore build/
        var nativeStatus = testRepo.RunGit("status --porcelain -u");
        Assert.DoesNotContain("out.bin", nativeStatus);

        // Managed repo status should also ignore build/
        var managedStatus = await repo.GetStatusAsync();
        Assert.DoesNotContain(managedStatus.Entries, e => e.Path.StartsWith("build/"));
    }

    [Fact]
    public async Task GitIgnore_LeadingSlash_OnlyMatchesRepositoryRoot_ConsistentWithGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // In standard git, a leading slash in .gitignore (e.g. '/rootonly.txt') anchors the match to repo root
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "/rootonly.txt\n");
        await repo.StageAsync(".gitignore");
        await repo.CommitAsync("Add gitignore", new GitCommitMetadata("Add gitignore", TestSignature));

        // Create rootonly.txt at root and in subdirectory
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "rootonly.txt"), "root");
        var subDir = Path.Combine(testRepo.WorkingDirectory, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "rootonly.txt"), "sub");

        // Native git status: sub/rootonly.txt is untracked (??), rootonly.txt is ignored
        var nativeStatus = testRepo.RunGit("status --porcelain -u");
        Assert.Contains("?? sub/rootonly.txt", nativeStatus);
        Assert.DoesNotContain("?? rootonly.txt", nativeStatus);

        // Managed repo status should be consistent
        var managedStatus = await repo.GetStatusAsync();
        Assert.Contains(managedStatus.Entries, e => e.Path == "sub/rootonly.txt" && e.WorkingTreeStatus == GitFileStatus.Untracked);
        Assert.DoesNotContain(managedStatus.Entries, e => e.Path == "rootonly.txt");
    }

    [Fact]
    public async Task GitIgnore_NegationPattern_ReIncludesFile_ConsistentWithGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // In standard git, '!' negates a previous ignore pattern
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "*.log\n!important.log\n");
        await repo.StageAsync(".gitignore");
        await repo.CommitAsync("Add gitignore", new GitCommitMetadata("Add gitignore", TestSignature));

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "dummy.log"), "ignore me");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "important.log"), "keep me");

        var nativeStatus = testRepo.RunGit("status --porcelain -u");
        Assert.Contains("?? important.log", nativeStatus);
        Assert.DoesNotContain("dummy.log", nativeStatus);

        var managedStatus = await repo.GetStatusAsync();
        Assert.Contains(managedStatus.Entries, e => e.Path == "important.log" && e.WorkingTreeStatus == GitFileStatus.Untracked);
        Assert.DoesNotContain(managedStatus.Entries, e => e.Path == "dummy.log");
    }

    [Fact]
    public async Task FileMode_ExecutableScriptMode100755_PreservedAcrossManagedCommit_VerifiedByGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        var scriptPath = Path.Combine(testRepo.WorkingDirectory, "script.sh");
        await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\necho hello\n");

        testRepo.RunGit("add script.sh");
        testRepo.RunGit("update-index --chmod=+x script.sh");
        testRepo.RunGit("commit -m \"Add script.sh executable\"");

        var lsFilesBefore = testRepo.RunGit("ls-files -s script.sh");
        Assert.StartsWith("100755", lsFilesBefore);

        // Modify via managed repo and commit
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\necho updated\n");
        await repo.StageAsync("script.sh");
        await repo.CommitAsync("Update script.sh", new GitCommitMetadata("Update script.sh", TestSignature));

        // Native git ls-files -s must still show mode 100755
        var lsFilesAfter = testRepo.RunGit("ls-files -s script.sh");
        Assert.StartsWith("100755", lsFilesAfter);
    }
}
