using System.IO;
using System.Text;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitIndexManagerTests
{
    [Fact]
    public async Task GetStatusAsync_CleanRepository_ReturnsIsCleanTrue()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial commit", ("file1.txt", "content 1"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        var status = await manager.GetStatusAsync();

        Assert.True(status.IsClean);
        Assert.Empty(status.Entries);
        Assert.True(await manager.IsWorkingTreeCleanAsync());
    }

    [Fact]
    public async Task GetStatusAsync_DetectsUntrackedModifiedAndDeletedFiles()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file1.txt", "content 1"), ("file2.txt", "content 2"));

        // Create untracked file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "untracked.txt"), "new");

        // Modify tracked file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file1.txt"), "modified content");

        // Delete tracked file
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "file2.txt"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        var status = await manager.GetStatusAsync();

        Assert.False(status.IsClean);
        Assert.False(await manager.IsWorkingTreeCleanAsync());

        var untracked = status.FindEntry("untracked.txt");
        Assert.NotNull(untracked);
        Assert.Equal(GitFileStatus.Untracked, untracked.WorkingTreeStatus);

        var modified = status.FindEntry("file1.txt");
        Assert.NotNull(modified);
        Assert.Equal(GitFileStatus.Modified, modified.WorkingTreeStatus);

        var deleted = status.FindEntry("file2.txt");
        Assert.NotNull(deleted);
        Assert.Equal(GitFileStatus.Deleted, deleted.WorkingTreeStatus);
    }

    [Fact]
    public async Task StageAsync_StagesNewFile_CompatibleWithGitCli()
    {
        using var testRepo = GitTestRepository.Create();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "added.txt"), "new file");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.StageAsync("added.txt");

        var status = await manager.GetStatusAsync();
        var entry = status.FindEntry("added.txt");
        Assert.NotNull(entry);
        Assert.Equal(GitFileStatus.StagedNew, entry.StagedStatus);
        Assert.Equal(GitFileStatus.Clean, entry.WorkingTreeStatus);

        // Verify git CLI status agrees
        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("A  added.txt", cliStatus);
    }

    [Fact]
    public async Task StageAsync_StagesModifiedFile()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "original"));

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file.txt"), "updated");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.StageAsync("file.txt");

        var status = await manager.GetStatusAsync();
        var entry = status.FindEntry("file.txt");
        Assert.NotNull(entry);
        Assert.Equal(GitFileStatus.StagedModified, entry.StagedStatus);
        Assert.Equal(GitFileStatus.Clean, entry.WorkingTreeStatus);

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("M  file.txt", cliStatus);
    }

    [Fact]
    public async Task StageAsync_StagesDeletedFile()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "content"));

        File.Delete(Path.Combine(testRepo.WorkingDirectory, "file.txt"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.StageAsync("file.txt");

        var status = await manager.GetStatusAsync();
        var entry = status.FindEntry("file.txt");
        Assert.NotNull(entry);
        Assert.Equal(GitFileStatus.StagedDeleted, entry.StagedStatus);

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("D  file.txt", cliStatus);
    }

    [Fact]
    public async Task StageAllAsync_StagesEverythingAtOnce()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("f1.txt", "1"), ("f2.txt", "2"));

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "f1.txt"), "1-mod");
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "f2.txt"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "f3.txt"), "3-new");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.StageAllAsync();

        var status = await manager.GetStatusAsync();
        Assert.Equal(3, status.StagedEntries.Count);
        Assert.Empty(status.ModifiedEntries);
        Assert.Empty(status.UntrackedEntries);
        Assert.Empty(status.DeletedEntries);

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("M  f1.txt", cliStatus);
        Assert.Contains("D  f2.txt", cliStatus);
        Assert.Contains("A  f3.txt", cliStatus);
    }

    [Fact]
    public async Task UnstageAsync_RevertsStagedFileToMatchHead()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "original"));

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file.txt"), "modified");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.StageAsync("file.txt");
        var stagedStatus = await manager.GetStatusAsync();
        Assert.Equal(GitFileStatus.StagedModified, stagedStatus.FindEntry("file.txt")!.StagedStatus);

        await manager.UnstageAsync("file.txt");
        var unstageStatus = await manager.GetStatusAsync();
        var entry = unstageStatus.FindEntry("file.txt")!;
        Assert.Equal(GitFileStatus.Clean, entry.StagedStatus);
        Assert.Equal(GitFileStatus.Modified, entry.WorkingTreeStatus);

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains(" M file.txt", cliStatus);
    }

    [Fact]
    public async Task UnstageAsync_RemovesNewlyAddedFileFromIndex()
    {
        using var testRepo = GitTestRepository.Create();
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "newfile.txt"), "content");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.StageAsync("newfile.txt");
        Assert.Equal(GitFileStatus.StagedNew, (await manager.GetStatusAsync()).FindEntry("newfile.txt")!.StagedStatus);

        await manager.UnstageAsync("newfile.txt");
        var status = await manager.GetStatusAsync();
        var entry = status.FindEntry("newfile.txt")!;
        Assert.Equal(GitFileStatus.Clean, entry.StagedStatus);
        Assert.Equal(GitFileStatus.Untracked, entry.WorkingTreeStatus);

        var cliStatus = testRepo.RunGit("status --porcelain");
        Assert.Contains("?? newfile.txt", cliStatus);
    }

    [Fact]
    public async Task RestoreFileAsync_DiscardsModifiedAndDeletedChanges()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("f1.txt", "f1-original"), ("f2.txt", "f2-original"));

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "f1.txt"), "f1-modified");
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "f2.txt"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.RestoreFileAsync("f1.txt");
        await manager.RestoreFileAsync("f2.txt");

        Assert.Equal("f1-original", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "f1.txt")));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "f2.txt")));
        Assert.Equal("f2-original", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "f2.txt")));

        var status = await manager.GetStatusAsync();
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task RestoreAllAsync_DiscardsAllWorkingTreeChanges()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("a.txt", "a"), ("b.txt", "b"));

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "a-tampered");
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "b.txt"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "untracked.txt"), "trash");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await manager.RestoreAllAsync(removeUntracked: true);

        Assert.Equal("a", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt")));
        Assert.Equal("b", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "untracked.txt")));

        Assert.True(await manager.IsWorkingTreeCleanAsync());
    }

    [Fact]
    public async Task GitIgnore_IgnoresSpecifiedPatterns()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("README.md", "# Readme"));

        // Write .gitignore
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "*.log\nbuild/\n!important.log\n");

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "test.log"), "log content");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "important.log"), "keep this");
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "build"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "build", "app.dll"), "bin");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        var status = await manager.GetStatusAsync();

        // test.log and build/app.dll should NOT appear in untracked
        Assert.Null(status.FindEntry("test.log"));
        Assert.Null(status.FindEntry("build/app.dll"));

        // important.log was negated with ! so it SHOULD appear
        Assert.NotNull(status.FindEntry("important.log"));
        Assert.Equal(GitFileStatus.Untracked, status.FindEntry("important.log")!.WorkingTreeStatus);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("dir/../../outside.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("..")]
    [InlineData(".")]
    public async Task StageAsync_RejectsEscapingPaths(string invalidPath)
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await Assert.ThrowsAsync<ArgumentException>(() => manager.StageAsync(invalidPath));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/rooted.txt")]
    public async Task UnstageAsync_RejectsEscapingPaths(string invalidPath)
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await Assert.ThrowsAsync<ArgumentException>(() => manager.UnstageAsync(invalidPath));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/rooted.txt")]
    public async Task RestoreFileAsync_RejectsEscapingPaths(string invalidPath)
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        await Assert.ThrowsAsync<ArgumentException>(() => manager.RestoreFileAsync(invalidPath));
    }

    [Fact]
    public async Task GetStatusAsync_TrackedFileInIgnoredDirectory_IsNotReportedAsDeleted()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("build/app.dll", "binary content"));

        // Ignore build/ directory
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, ".gitignore"), "build/\n");

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        var status = await manager.GetStatusAsync(includeClean: true);

        var entry = status.FindEntry("build/app.dll");
        Assert.NotNull(entry);
        // Tracked file is on disk; it must NOT be reported as Deleted
        Assert.Equal(GitFileStatus.Clean, entry.WorkingTreeStatus);
        Assert.Equal(GitFileStatus.Clean, entry.StagedStatus);

        // Also verify that with default includeClean: false, build/app.dll is not reported as having changes
        var defaultStatus = await manager.GetStatusAsync();
        Assert.Null(defaultStatus.FindEntry("build/app.dll"));
    }


    [Fact]
    public async Task GetStatusAsync_ModeChangeOnly_ReportsStagedModified()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("script.sh", "echo hello"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        // Update the index entry mode to executable (33261 / 100755) while keeping the same content hash
        var index = await GitIndex.ReadAsync(manager.IndexPath);
        var entry = index.FindEntry("script.sh")!;
        entry.FileMode = 33261; // 100755 executable
        await index.WriteAsync(manager.IndexPath);

        var status = await manager.GetStatusAsync();
        var statusEntry = status.FindEntry("script.sh");
        Assert.NotNull(statusEntry);
        // Mode changed between index and HEAD: must be StagedModified
        Assert.Equal(GitFileStatus.StagedModified, statusEntry.StagedStatus);
    }

    [Fact]
    public async Task GetStatusAsync_NanosecondTimestampChange_DetectsModification()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "abc"));

        var filePath = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        // Overwrite file with same length content, but different nanosecond timestamp
        var originalTime = File.GetLastWriteTimeUtc(filePath);
        // Change content to "xyz" (same length 3)
        await File.WriteAllTextAsync(filePath, "xyz");
        // Set same whole second, different nanoseconds (ticks fraction)
        var sameSecondTime = new DateTime(originalTime.Year, originalTime.Month, originalTime.Day,
            originalTime.Hour, originalTime.Minute, originalTime.Second, DateTimeKind.Utc).AddTicks(5000000); // 500ms
        File.SetLastWriteTimeUtc(filePath, sameSecondTime);

        var status = await manager.GetStatusAsync();
        var entry = status.FindEntry("file.txt");
        Assert.NotNull(entry);
        Assert.Equal(GitFileStatus.Modified, entry.WorkingTreeStatus);
    }

    [Fact]
    public async Task StageAsync_ConcurrentCalls_AreSerialized()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        const int count = 8;
        var fileNames = Enumerable.Range(0, count).Select(i => $"concurrent_{i}.txt").ToList();
        foreach (var name in fileNames)
        {
            await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, name), $"content of {name}");
        }

        // Run StageAsync concurrently on all files
        var tasks = fileNames.Select(name => manager.StageAsync(name)).ToArray();
        await Task.WhenAll(tasks);

        var status = await manager.GetStatusAsync();
        foreach (var name in fileNames)
        {
            var entry = status.FindEntry(name);
            Assert.NotNull(entry);
            Assert.Equal(GitFileStatus.StagedNew, entry.StagedStatus);
        }
    }

    [Fact]
    public async Task StageAsync_RejectsSymlinkEscapingRepository()
    {
        using var testRepo = GitTestRepository.Create();
        var externalDir = Path.Combine(Path.GetTempPath(), $"pmad_ext_stage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(externalDir, "secret.txt"), "classified");

            var linkPath = Path.Combine(testRepo.WorkingDirectory, "ext_link");
            if (!TryCreateDirectoryLink(linkPath, externalDir))
            {
                return;
            }

            var repo = GitRepository.Open(testRepo.WorkingDirectory);
            var manager = repo.IndexManager!;

            await Assert.ThrowsAsync<ArgumentException>(() => manager.StageAsync("ext_link/secret.txt"));
        }
        finally
        {
            try { Directory.Delete(externalDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ScanWorkingDirectory_SkipsDirectorySymlinks()
    {
        using var testRepo = GitTestRepository.Create();
        var externalDir = Path.Combine(Path.GetTempPath(), $"pmad_ext_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(externalDir, "external.txt"), "outside");

            var linkPath = Path.Combine(testRepo.WorkingDirectory, "scan_link");
            if (!TryCreateDirectoryLink(linkPath, externalDir))
            {
                return;
            }

            var repo = GitRepository.Open(testRepo.WorkingDirectory);
            var manager = repo.IndexManager!;

            var status = await manager.GetStatusAsync();

            // The external file reached through a directory symlink must NOT be reported
            Assert.Null(status.FindEntry("scan_link/external.txt"));
        }
        finally
        {
            try { Directory.Delete(externalDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RestoreFileAsync_RestoresContentAndPreservesExecutableMode()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("run.sh", "#!/bin/sh\necho hello\n"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        // Record entry as executable in index
        var index = await GitIndex.ReadAsync(manager.IndexPath);
        var entry = index.FindEntry("run.sh")!;
        entry.FileMode = 33261; // 100755
        await index.WriteAsync(manager.IndexPath);

        // Delete from working tree
        var filePath = Path.Combine(testRepo.WorkingDirectory, "run.sh");
        File.Delete(filePath);

        // Restore file
        await manager.RestoreFileAsync("run.sh");

        Assert.True(File.Exists(filePath));
        Assert.Equal("#!/bin/sh\necho hello\n", await File.ReadAllTextAsync(filePath));

        if (!OperatingSystem.IsWindows())
        {
            var unixMode = File.GetUnixFileMode(filePath);
            Assert.True((unixMode & UnixFileMode.UserExecute) != 0, "Executable bit should be restored on Unix");
        }
    }

    [Fact]
    public async Task UnstageAllAsync_WithStagedChanges_RestoresIndexToHeadAndPreservesWorkingTree()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial commit", ("file1.txt", "content 1"), ("file2.txt", "content 2"));

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        // Modify file1.txt and stage it
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file1.txt"), "modified content 1");
        await manager.StageAsync("file1.txt");

        // Add file3.txt and stage it
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file3.txt"), "content 3");
        await manager.StageAsync("file3.txt");

        // Stage removal of file2.txt
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "file2.txt"));
        await manager.StageAsync("file2.txt");

        // Verify status shows staged changes
        var statusBefore = await manager.GetStatusAsync();
        Assert.Equal(GitFileStatus.StagedModified, statusBefore.FindEntry("file1.txt")!.StagedStatus);
        Assert.Equal(GitFileStatus.StagedNew, statusBefore.FindEntry("file3.txt")!.StagedStatus);
        Assert.Equal(GitFileStatus.StagedDeleted, statusBefore.FindEntry("file2.txt")!.StagedStatus);

        // Act: unstage all
        await manager.UnstageAllAsync();

        // Assert:
        // 1. Index matches HEAD
        var statusAfter = await manager.GetStatusAsync();
        var f1 = statusAfter.FindEntry("file1.txt")!;
        Assert.Equal(GitFileStatus.Clean, f1.StagedStatus);
        Assert.Equal(GitFileStatus.Modified, f1.WorkingTreeStatus);

        var f2 = statusAfter.FindEntry("file2.txt")!;
        Assert.Equal(GitFileStatus.Clean, f2.StagedStatus);
        Assert.Equal(GitFileStatus.Deleted, f2.WorkingTreeStatus);

        var f3 = statusAfter.FindEntry("file3.txt")!;
        Assert.Equal(GitFileStatus.Untracked, f3.WorkingTreeStatus);
        Assert.Equal(GitFileStatus.Clean, f3.StagedStatus);

        // 2. Working tree file contents are preserved
        Assert.Equal("modified content 1", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file1.txt")));
        Assert.Equal("content 3", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file3.txt")));
    }

    [Fact]
    public async Task UnstageAllAsync_NoHeadCommit_ClearsEntireIndex()
    {
        using var testRepo = GitTestRepository.Create();
        // Delete the initial commit from GitTestRepository so HEAD is unborn
        var repoDir = testRepo.WorkingDirectory;
        var gitDir = Path.Combine(repoDir, ".git");
        var headRefFile = Path.Combine(gitDir, "refs", "heads", "main");
        if (File.Exists(headRefFile)) File.Delete(headRefFile);
        var headRefMaster = Path.Combine(gitDir, "refs", "heads", "master");
        if (File.Exists(headRefMaster)) File.Delete(headRefMaster);

        var repo = GitRepository.Open(repoDir);
        var manager = repo.IndexManager!;

        await File.WriteAllTextAsync(Path.Combine(repoDir, "newfile.txt"), "hello");
        await manager.StageAsync("newfile.txt");

        var statusBefore = await manager.GetStatusAsync();
        Assert.Equal(GitFileStatus.StagedNew, statusBefore.FindEntry("newfile.txt")!.StagedStatus);

        // Act: unstage all
        await manager.UnstageAllAsync();

        // Assert: index is cleared, file is untracked
        var statusAfter = await manager.GetStatusAsync();
        Assert.Equal(GitFileStatus.Untracked, statusAfter.FindEntry("newfile.txt")!.WorkingTreeStatus);
        Assert.Equal(GitFileStatus.Clean, statusAfter.FindEntry("newfile.txt")!.StagedStatus);
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                process?.WaitForExit();
                return Directory.Exists(linkPath);
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task StageAsync_ExistingTrackedExecutableFile_PreservesExecutableModeOnWindows()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Add file directly to index with 100755 mode (33261)
        var filePath = Path.Combine(testRepo.WorkingDirectory, "script.sh");
        await File.WriteAllTextAsync(filePath, "#!/bin/sh\necho 'hello'");

        var blobHash = await repo.ObjectStore.WriteObjectAsync(GitObjectType.Blob, Encoding.UTF8.GetBytes("#!/bin/sh\necho 'hello'"), CancellationToken.None);
        var initialIndex = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        initialIndex.AddOrUpdate(new GitIndexEntry("script.sh", blobHash, fileMode: 33261));
        await initialIndex.WriteAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);

        // Commit initial state
        var metadata = new GitCommitMetadata("Add executable script", new GitCommitSignature("Author", "author@example.com", DateTimeOffset.UtcNow));
        await repo.CommitAsync("Add executable script", metadata);

        // Now modify the file on disk
        await File.WriteAllTextAsync(filePath, "#!/bin/sh\necho 'updated'");

        // Stage the modified file
        await repo.IndexManager.StageAsync("script.sh");

        // Verify index entry preserves 33261 mode
        var updatedIndex = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes);
        var entry = updatedIndex.FindEntry("script.sh");
        Assert.NotNull(entry);
        Assert.Equal(33261, entry.FileMode);

        // Commit and verify tree entry retains mode 33261
        var commitHash = await repo.CommitAsync("Update executable script", metadata);
        GitTreeEntry? scriptTreeEntry = null;
        await foreach (var item in repo.EnumerateCommitTreeAsync(commitHash.Value))
        {
            if (item.Path == "script.sh")
            {
                scriptTreeEntry = item.Entry;
                break;
            }
        }
        Assert.NotNull(scriptTreeEntry);
        Assert.Equal(33261, scriptTreeEntry.Mode);
    }
}


