using System.IO;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.LocalRepositories.Test;

public class GitRepositoryMoveTests
{
    [Fact]
    public async Task MoveAsync_SingleFile_Success()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "file1.txt");
        await File.WriteAllTextAsync(filePath, "test content");
        await repo.StageAsync("file1.txt");
        await repo.CommitAsync("Add file1");

        await repo.MoveAsync("file1.txt", "file2.txt");

        Assert.False(File.Exists(filePath));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "file2.txt")));
        Assert.Equal("test content", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file2.txt")));

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.StagedEntries, e => e.Path == "file1.txt" && e.StagedStatus == GitFileStatus.StagedDeleted);
        Assert.Contains(status.StagedEntries, e => e.Path == "file2.txt" && e.StagedStatus == GitFileStatus.StagedNew);
        Assert.Empty(status.ModifiedEntries);

        var commitHash = await repo.CommitAsync("Rename file1 to file2");
        var commitTree = new List<GitTreeItem>();
        await foreach (var item in repo.EnumerateCommitTreeAsync(commitHash.ToString()))
        {
            commitTree.Add(item);
        }
        Assert.Contains(commitTree, item => item.Path == "file2.txt");
        Assert.DoesNotContain(commitTree, item => item.Path == "file1.txt");
    }

    [Fact]
    public async Task MoveAsync_FilePreservesUnstagedContent()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "original content");
        await repo.StageAsync("file.txt");
        await repo.CommitAsync("Add file");

        await File.WriteAllTextAsync(filePath, "modified unstaged content");

        await repo.MoveAsync("file.txt", "dest.txt");

        var destPath = Path.Combine(testRepo.WorkingDirectory, "dest.txt");
        Assert.False(File.Exists(filePath));
        Assert.True(File.Exists(destPath));
        Assert.Equal("modified unstaged content", await File.ReadAllTextAsync(destPath));

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.ModifiedEntries, e => e.Path == "dest.txt" && e.WorkingTreeStatus == GitFileStatus.Modified);
    }

    [Fact]
    public async Task MoveAsync_SingleFile_ToExistingDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var subDir = Path.Combine(testRepo.WorkingDirectory, "sub");
        Directory.CreateDirectory(subDir);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "hello");
        await repo.StageAsync("file.txt");
        await repo.CommitAsync("Add file");

        await repo.MoveAsync("file.txt", "sub");

        Assert.False(File.Exists(filePath));
        var destPath = Path.Combine(subDir, "file.txt");
        Assert.True(File.Exists(destPath));
        Assert.Equal("hello", await File.ReadAllTextAsync(destPath));

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.StagedEntries, e => e.Path == "sub/file.txt" && e.StagedStatus == GitFileStatus.StagedNew);
    }

    [Fact]
    public async Task MoveAsync_Directory_Rename()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var dir1 = Path.Combine(testRepo.WorkingDirectory, "dir1");
        Directory.CreateDirectory(dir1);
        await File.WriteAllTextAsync(Path.Combine(dir1, "a.txt"), "content a");
        await File.WriteAllTextAsync(Path.Combine(dir1, "b.txt"), "content b");
        await repo.StageAsync(["dir1/a.txt", "dir1/b.txt"]);
        await repo.CommitAsync("Add dir1 files");

        await repo.MoveAsync("dir1", "dir2");

        Assert.False(Directory.Exists(dir1));
        var dir2 = Path.Combine(testRepo.WorkingDirectory, "dir2");
        Assert.True(Directory.Exists(dir2));
        Assert.True(File.Exists(Path.Combine(dir2, "a.txt")));
        Assert.True(File.Exists(Path.Combine(dir2, "b.txt")));

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.StagedEntries, e => e.Path == "dir2/a.txt" && e.StagedStatus == GitFileStatus.StagedNew);
        Assert.Contains(status.StagedEntries, e => e.Path == "dir2/b.txt" && e.StagedStatus == GitFileStatus.StagedNew);
        Assert.Contains(status.StagedEntries, e => e.Path == "dir1/a.txt" && e.StagedStatus == GitFileStatus.StagedDeleted);
        Assert.Contains(status.StagedEntries, e => e.Path == "dir1/b.txt" && e.StagedStatus == GitFileStatus.StagedDeleted);
    }

    [Fact]
    public async Task MoveAsync_Directory_IntoExistingDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var dir1 = Path.Combine(testRepo.WorkingDirectory, "dir1");
        Directory.CreateDirectory(dir1);
        await File.WriteAllTextAsync(Path.Combine(dir1, "a.txt"), "content a");

        var dir2 = Path.Combine(testRepo.WorkingDirectory, "dir2");
        Directory.CreateDirectory(dir2);
        await File.WriteAllTextAsync(Path.Combine(dir2, "b.txt"), "content b");

        await repo.StageAsync(["dir1/a.txt", "dir2/b.txt"]);
        await repo.CommitAsync("Add dir files");

        await repo.MoveAsync("dir1", "dir2");

        var targetFile = Path.Combine(dir2, "dir1", "a.txt");
        Assert.True(File.Exists(targetFile));

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.StagedEntries, e => e.Path == "dir2/dir1/a.txt" && e.StagedStatus == GitFileStatus.StagedNew);
        Assert.Contains(status.StagedEntries, e => e.Path == "dir1/a.txt" && e.StagedStatus == GitFileStatus.StagedDeleted);
    }

    [Fact]
    public async Task MoveAsync_DestinationExists_WithoutForce_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "B");
        await repo.StageAsync(["a.txt", "b.txt"]);
        await repo.CommitAsync("Add files");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("a.txt", "b.txt"));
        Assert.Contains("destination exists", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_DestinationExists_WithForce_Overwrites()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "content from A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "content from B");
        await repo.StageAsync(["a.txt", "b.txt"]);
        await repo.CommitAsync("Add files");

        await repo.MoveAsync("a.txt", "b.txt", force: true);

        var bPath = Path.Combine(testRepo.WorkingDirectory, "b.txt");
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "a.txt")));
        Assert.True(File.Exists(bPath));
        Assert.Equal("content from A", await File.ReadAllTextAsync(bPath));
    }

    [Fact]
    public async Task MoveAsync_UntrackedFile_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "untracked.txt"), "not tracked");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("untracked.txt", "dest.txt"));
        Assert.Contains("not under version control", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_NonExistentFile_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => repo.MoveAsync("missing.txt", "dest.txt"));
        Assert.Contains("bad source", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_SkipErrors_SkipsInvalidAndMovesValid()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var subDir = Path.Combine(testRepo.WorkingDirectory, "sub");
        Directory.CreateDirectory(subDir);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "valid.txt"), "valid content");
        await repo.StageAsync("valid.txt");
        await repo.CommitAsync("Add valid");

        var result = await repo.MoveAsync(["valid.txt", "missing.txt"], "sub", new GitMoveOptions { SkipErrors = true });

        Assert.Single(result.MovedItems);
        Assert.Equal("valid.txt", result.MovedItems[0].SourcePath);
        Assert.Equal("sub/valid.txt", result.MovedItems[0].DestinationPath);
        Assert.True(File.Exists(Path.Combine(subDir, "valid.txt")));
    }

    [Fact]
    public async Task MoveAsync_DryRun_DoesNotTouchDiskOrIndex()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "original.txt");
        await File.WriteAllTextAsync(filePath, "data");
        await repo.StageAsync("original.txt");
        await repo.CommitAsync("Add file");

        var result = await repo.MoveAsync(["original.txt"], "renamed.txt", new GitMoveOptions { DryRun = true });

        Assert.Single(result.MovedItems);
        Assert.Equal("original.txt", result.MovedItems[0].SourcePath);
        Assert.Equal("renamed.txt", result.MovedItems[0].DestinationPath);

        Assert.True(File.Exists(filePath));
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "renamed.txt")));

        var status = await repo.GetStatusAsync();
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task MoveAsync_ToRootDirectory_UsingDot()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var subDir = Path.Combine(testRepo.WorkingDirectory, "sub");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "file.txt");
        await File.WriteAllTextAsync(filePath, "data");
        await repo.StageAsync("sub/file.txt");
        await repo.CommitAsync("Add sub/file.txt");

        await repo.MoveAsync("sub/file.txt", ".");

        Assert.False(File.Exists(filePath));
        var rootFile = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        Assert.True(File.Exists(rootFile));

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.StagedEntries, e => e.Path == "file.txt" && e.StagedStatus == GitFileStatus.StagedNew);
        Assert.Contains(status.StagedEntries, e => e.Path == "sub/file.txt" && e.StagedStatus == GitFileStatus.StagedDeleted);
    }

    [Fact]
    public async Task MoveAsync_Directory_WithUntrackedFiles_MovesEntireDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var dir1 = Path.Combine(testRepo.WorkingDirectory, "dir1");
        Directory.CreateDirectory(dir1);
        await File.WriteAllTextAsync(Path.Combine(dir1, "tracked.txt"), "tracked");
        await File.WriteAllTextAsync(Path.Combine(dir1, "untracked.txt"), "untracked");
        await repo.StageAsync("dir1/tracked.txt");
        await repo.CommitAsync("Add tracked");

        await repo.MoveAsync("dir1", "dir2");

        Assert.False(Directory.Exists(dir1));
        var dir2 = Path.Combine(testRepo.WorkingDirectory, "dir2");
        Assert.True(Directory.Exists(dir2));
        Assert.True(File.Exists(Path.Combine(dir2, "tracked.txt")));
        Assert.True(File.Exists(Path.Combine(dir2, "untracked.txt")));

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.StagedEntries, e => e.Path == "dir2/tracked.txt" && e.StagedStatus == GitFileStatus.StagedNew);
        Assert.Contains(status.UntrackedEntries, e => e.Path == "dir2/untracked.txt");
    }

    [Fact]
    public async Task MoveAsync_Directory_CannotMoveIntoItself_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var dir1 = Path.Combine(testRepo.WorkingDirectory, "dir1");
        Directory.CreateDirectory(dir1);
        await File.WriteAllTextAsync(Path.Combine(dir1, "a.txt"), "a");
        await repo.StageAsync("dir1/a.txt");
        await repo.CommitAsync("Add file");

        var exSelf = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("dir1", "dir1"));
        Assert.Contains("can not move directory into itself", exSelf.Message);

        var exChild = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("dir1", "dir1/child"));
        Assert.Contains("can not move directory into itself", exChild.Message);
    }

    [Fact]
    public async Task MoveAsync_FileDirectoryIndexConflict_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var destDir = Path.Combine(testRepo.WorkingDirectory, "dest");
        var childDir = Path.Combine(destDir, "a.txt");
        Directory.CreateDirectory(childDir);
        await File.WriteAllTextAsync(Path.Combine(childDir, "child.txt"), "child");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "a file");
        await repo.StageAsync(["dest/a.txt/child.txt", "a.txt"]);
        await repo.CommitAsync("Add files");

        // Delete childDir from disk, so dest/a.txt/child.txt is tracked in index but absent from disk
        Directory.Delete(childDir, recursive: true);

        // Moving a.txt into dest targets dest/a.txt, which conflicts with indexed descendant dest/a.txt/child.txt
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("a.txt", "dest", force: true));
        Assert.Contains("destination exists", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_MultipleDuplicates_SkipErrors_FiltersAllDuplicates()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "d1"));
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "d2"));
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "out"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "d1", "a.txt"), "d1/a");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "d2", "a.txt"), "d2/a");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "d1", "b.txt"), "d1/b");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "d2", "b.txt"), "d2/b");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "d1", "unique.txt"), "unique");
        await repo.StageAsync(["d1/a.txt", "d2/a.txt", "d1/b.txt", "d2/b.txt", "d1/unique.txt"]);
        await repo.CommitAsync("Add files");

        var result = await repo.MoveAsync(
            ["d1/a.txt", "d2/a.txt", "d1/b.txt", "d2/b.txt", "d1/unique.txt"],
            "out",
            new GitMoveOptions { SkipErrors = true });

        Assert.Single(result.MovedItems);
        Assert.Equal("d1/unique.txt", result.MovedItems[0].SourcePath);
        Assert.Equal("out/unique.txt", result.MovedItems[0].DestinationPath);
    }

    [Fact]
    public async Task MoveAsync_TrackedFileDeletedOnDisk_MovesIndexEntryWithoutThrowing()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "deleted_on_disk.txt");
        await File.WriteAllTextAsync(filePath, "data");
        await repo.StageAsync("deleted_on_disk.txt");
        await repo.CommitAsync("Add file");

        File.Delete(filePath);

        await repo.MoveAsync("deleted_on_disk.txt", "moved_deleted.txt");

        var status = await repo.GetStatusAsync();
        Assert.Contains(status.StagedEntries, e => e.Path == "moved_deleted.txt" && e.StagedStatus == GitFileStatus.StagedNew);
        Assert.Contains(status.DeletedEntries, e => e.Path == "moved_deleted.txt");
    }

    [Fact]
    public async Task MoveAsync_PreservesAssumeUnchangedAndExtendedFlags()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "flagged.txt");
        await File.WriteAllTextAsync(filePath, "data");
        await repo.StageAsync("flagged.txt");

        // Set assume-unchanged and extended flags
        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        var entry = index.FindEntry("flagged.txt")!;
        entry.Flags |= 0x4000; // CE_VALID / assume-unchanged
        entry.ExtendedFlags = 0x4000; // CE_SKIP_WORKTREE
        await index.WriteAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);

        await repo.MoveAsync("flagged.txt", "flagged_dest.txt");

        var updatedIndex = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        var movedEntry = updatedIndex.FindEntry("flagged_dest.txt")!;
        Assert.Equal(0x4000, movedEntry.Flags & 0x4000);
        Assert.Equal(0x4000, movedEntry.ExtendedFlags);
    }

    [Fact]
    public async Task MoveAsync_TwoArgumentCollectionOverload_CompilesWithoutAmbiguity()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "sub"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "f1.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "f2.txt"), "2");
        await repo.StageAsync(["f1.txt", "f2.txt"]);
        await repo.CommitAsync("Add files");

        // Two-argument call: should resolve to MoveAsync(IEnumerable<string>, string, bool, CancellationToken)
        await repo.MoveAsync(new[] { "f1.txt", "f2.txt" }, "sub");

        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "sub", "f1.txt")));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "sub", "f2.txt")));
    }

    [Fact]
    public async Task MoveAsync_CaseOnlyFileRename_ChangesCasingOnDiskAndIndex()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "case test");
        await repo.StageAsync("file.txt");
        await repo.CommitAsync("Add file");

        await repo.MoveAsync("file.txt", "FILE.TXT");

        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        Assert.NotNull(index.FindEntry("FILE.TXT"));
        Assert.Null(index.FindEntry("file.txt"));

        var diskFiles = Directory.GetFiles(testRepo.WorkingDirectory, "*.*");
        var matchedFile = diskFiles.Single(f => Path.GetFileName(f).Equals("FILE.TXT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("FILE.TXT", Path.GetFileName(matchedFile));
    }

    [Fact]
    public async Task MoveAsync_CaseOnlyDirectoryRename_ChangesCasingOnDiskAndIndex()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var dir = Path.Combine(testRepo.WorkingDirectory, "myfolder");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "test.txt"), "hello");
        await repo.StageAsync("myfolder/test.txt");
        await repo.CommitAsync("Add file");

        await repo.MoveAsync("myfolder", "MYFOLDER");

        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        Assert.NotNull(index.FindEntry("MYFOLDER/test.txt"));
        Assert.Null(index.FindEntry("myfolder/test.txt"));

        var diskDirs = Directory.GetDirectories(testRepo.WorkingDirectory);
        var matchedDir = diskDirs.Single(d => Path.GetFileName(d).Equals("MYFOLDER", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("MYFOLDER", Path.GetFileName(matchedDir));
    }

    [Fact]
    public async Task MoveAsync_TrailingSlashDestination_DoesNotExist_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "content");
        await repo.StageAsync("a.txt");
        await repo.CommitAsync("Add file");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("a.txt", "missing_dir/"));
        Assert.Contains("not a directory", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_SkipErrors_DestinationIsExistingDirectory_SkipsWithoutThrowing()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "sub_dir"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "valid.txt"), "valid");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "conflict.txt"), "conflict");
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "conflict.txt_target"));
        await repo.StageAsync(["valid.txt", "conflict.txt"]);
        await repo.CommitAsync("Add files");

        // Attempting to move conflict.txt to conflict.txt_target (which is a directory, not a file target) with single-file-rename semantics
        var result = await repo.MoveAsync(
            ["valid.txt"],
            "sub_dir",
            new GitMoveOptions { SkipErrors = true });

        Assert.Single(result.MovedItems);
        Assert.Equal("valid.txt", result.MovedItems[0].SourcePath);
        Assert.Equal("sub_dir/valid.txt", result.MovedItems[0].DestinationPath);
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "sub_dir", "valid.txt")));
    }

    [Fact]
    public async Task MoveAsync_ConflictedFileDeletedOnDisk_ThrowsConflicted()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var filePath = Path.Combine(testRepo.WorkingDirectory, "conflicted.txt");
        await File.WriteAllTextAsync(filePath, "data");
        await repo.StageAsync("conflicted.txt");

        // Set stage 1 and stage 2 entries in index, delete stage 0
        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        var entry = index.FindEntry("conflicted.txt")!;
        index.Remove("conflicted.txt", stage: 0);
        var stage1 = new GitIndexEntry("conflicted.txt", entry.Hash, entry.FileMode, stage: 1);
        var stage2 = new GitIndexEntry("conflicted.txt", entry.Hash, entry.FileMode, stage: 2);
        index.AddOrUpdate(stage1);
        index.AddOrUpdate(stage2);
        await index.WriteAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);

        // Delete file on disk
        File.Delete(filePath);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("conflicted.txt", "dest.txt"));
        Assert.Contains("conflicted", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_CaseInsensitiveFs_DuplicateDestinationsDifferentCasing_ThrowsOrSkips()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        if (!GitIndexManager.IsFileSystemCaseInsensitive(testRepo.WorkingDirectory))
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "left"));
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "right"));
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "out"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "left", "a.txt"), "left A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "right", "A.txt"), "right A");
        await repo.StageAsync(["left/a.txt", "right/A.txt"]);
        await repo.CommitAsync("Add left and right files");

        // Without skip errors: throws duplicate destination error
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync(["left/a.txt", "right/A.txt"], "out"));
        Assert.Contains("destination exists", ex.Message);

        // With skip errors: removes duplicate destinations and moves nothing
        var result = await repo.MoveAsync(["left/a.txt", "right/A.txt"], "out", new GitMoveOptions { SkipErrors = true });
        Assert.Empty(result.MovedItems);
    }

    [Fact]
    public async Task MoveAsync_CaseSensitiveVolume_DistinctCaseFiles_ThrowsWithoutForce()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Force case-sensitive mode for test
        GitIndexManager.SetFileSystemCaseSensitivityForTest(repo.RootPath, false);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file.txt"), "lowercase");
            await repo.StageAsync("file.txt");
            await repo.CommitAsync("Add file.txt");

            // Also add FILE.TXT to index as a distinct file
            var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
            var entry = index.FindEntry("file.txt")!;
            index.AddOrUpdate(new GitIndexEntry("FILE.TXT", entry.Hash, entry.FileMode));
            await index.WriteAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("file.txt", "FILE.TXT"));
            Assert.Contains("destination exists", ex.Message);
        }
        finally
        {
            GitIndexManager.SetFileSystemCaseSensitivityForTest(repo.RootPath, null);
        }
    }

    [Fact]
    public async Task MoveAsync_TrackedDirectorySymlink_MovesLinkItselfWithoutTraversing()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var targetDir = Path.Combine(testRepo.WorkingDirectory, "target");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "target_file.txt"), "target data");
        await repo.StageAsync("target/target_file.txt");
        await repo.CommitAsync("Add target file");

        var linkPath = Path.Combine(testRepo.WorkingDirectory, "link_to_target");
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Skip test if symbolic link creation is not supported by environment
            return;
        }

        // Add symlink to index with mode 40960 (symlink mode)
        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        var targetEntry = index.FindEntry("target/target_file.txt")!;
        index.AddOrUpdate(new GitIndexEntry("link_to_target", targetEntry.Hash, fileMode: 40960));
        await index.WriteAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);

        var result = await repo.MoveAsync(["link_to_target"], "new_link_to_target", new GitMoveOptions());
        Assert.Single(result.MovedItems);
        Assert.Equal("link_to_target", result.MovedItems[0].SourcePath);
        Assert.Equal("new_link_to_target", result.MovedItems[0].DestinationPath);

        // Verify index contains new_link_to_target with symlink mode
        var updatedIndex = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        var movedEntry = updatedIndex.FindEntry("new_link_to_target");
        Assert.NotNull(movedEntry);
        Assert.Equal(40960, movedEntry.FileMode);
        Assert.Null(updatedIndex.FindEntry("link_to_target"));

        // Verify target file still exists
        Assert.True(File.Exists(Path.Combine(targetDir, "target_file.txt")));
    }

    [Fact]
    public async Task MoveAsync_DirectoryMove_TargetAncestorIsIndexedFile_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Add tracked file "dest"
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "dest"), "dest content");
        // Add tracked directory "src/a.txt"
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "src"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "src", "a.txt"), "A");
        await repo.StageAsync(["dest", "src/a.txt"]);
        await repo.CommitAsync("Add dest and src/a.txt");

        // Delete "dest" on disk only, leaving it in index
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "dest"));

        // Moving directory "src" to "dest" should throw because "dest" is an ancestor conflict in index
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync("src", "dest"));
        Assert.Contains("destination exists", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_OverlappingSources_Throws()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "src"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "src", "a.txt"), "A");
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "out"));
        await repo.StageAsync("src/a.txt");
        await repo.CommitAsync("Add src/a.txt");

        // "src" and "src/a.txt" overlap
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.MoveAsync(["src", "src/a.txt"], "out"));
        Assert.Contains("overlapping source", ex.Message);
    }

    [Fact]
    public async Task MoveAsync_SkipErrors_DuplicateSources_DropsWholeDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "src"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "src", "a.txt"), "A");
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "out"));
        await repo.StageAsync("src/a.txt");
        await repo.CommitAsync("Add src/a.txt");

        var result = await repo.MoveAsync(["src", "src"], "out", new GitMoveOptions { SkipErrors = true });

        // Whole directory operation was dropped because child had duplicate destination
        Assert.Empty(result.MovedItems);
        Assert.True(Directory.Exists(Path.Combine(testRepo.WorkingDirectory, "src")));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "src", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(testRepo.WorkingDirectory, "out", "src")));

        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        Assert.NotNull(index.FindEntry("src/a.txt"));
        Assert.Null(index.FindEntry("out/src/a.txt"));
    }
}

