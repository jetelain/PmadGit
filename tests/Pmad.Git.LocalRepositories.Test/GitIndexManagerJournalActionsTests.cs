using Pmad.Git.LocalRepositories;

namespace Pmad.Git.LocalRepositories.Test;

public class GitIndexManagerJournalActionsTests : IDisposable
{
    private readonly string _tempDir;

    public GitIndexManagerJournalActionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pmad_git_journal_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void MoveFileAction_Execute_MovesFile()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dst = Path.Combine(_tempDir, "dest.txt");
        File.WriteAllText(src, "content");

        var action = new GitIndexManager.MoveFileAction(src, dst, force: false);
        action.Execute();

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("content", File.ReadAllText(dst));
    }

    [Fact]
    public void MoveFileAction_Execute_DestinationExists_WithoutForce_ThrowsIOException()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dst = Path.Combine(_tempDir, "dest.txt");
        File.WriteAllText(src, "content src");
        File.WriteAllText(dst, "content dst");

        var action = new GitIndexManager.MoveFileAction(src, dst, force: false);
        Assert.Throws<IOException>(() => action.Execute());
    }

    [Fact]
    public void MoveFileAction_Execute_DestinationExists_WithForce_AndCommit_DeletesBackup()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dst = Path.Combine(_tempDir, "dest.txt");
        File.WriteAllText(src, "content src");
        File.WriteAllText(dst, "content dst");

        var action = new GitIndexManager.MoveFileAction(src, dst, force: true);
        action.Execute();

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("content src", File.ReadAllText(dst));

        // Commit cleans up backup file
        action.Commit();
        Assert.Single(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public void MoveFileAction_Rollback_RestoresSourceAndBackup()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dst = Path.Combine(_tempDir, "dest.txt");
        File.WriteAllText(src, "content src");
        File.WriteAllText(dst, "content dst");

        var action = new GitIndexManager.MoveFileAction(src, dst, force: true);
        action.Execute();
        action.Rollback();

        Assert.True(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("content src", File.ReadAllText(src));
        Assert.Equal("content dst", File.ReadAllText(dst));
    }

    [Fact]
    public void MoveFileAction_Rollback_WhenNoBackup_RestoresSource()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dst = Path.Combine(_tempDir, "dest.txt");
        File.WriteAllText(src, "content src");

        var action = new GitIndexManager.MoveFileAction(src, dst, force: false);
        action.Execute();
        action.Rollback();

        Assert.True(File.Exists(src));
        Assert.False(File.Exists(dst));
    }

    [Fact]
    public void MoveCaseOnlyFileAction_ExecuteAndRollback_Succeeds()
    {
        var src = Path.Combine(_tempDir, "myfile.txt");
        var dst = Path.Combine(_tempDir, "MYFILE.TXT");
        File.WriteAllText(src, "case content");

        var action = new GitIndexManager.MoveCaseOnlyFileAction(src, dst);
        action.Execute();

        Assert.True(File.Exists(dst));

        action.Commit(); // Commit is no-op

        action.Rollback();
        Assert.True(File.Exists(src));
        Assert.Equal("case content", File.ReadAllText(src));
    }

    [Fact]
    public void MoveDirectoryAction_ExecuteAndRollback_Succeeds()
    {
        var src = Path.Combine(_tempDir, "dirA");
        var dst = Path.Combine(_tempDir, "dirB");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "test.txt"), "hello");

        var action = new GitIndexManager.MoveDirectoryAction(src, dst);
        action.Execute();

        Assert.False(Directory.Exists(src));
        Assert.True(Directory.Exists(dst));
        Assert.True(File.Exists(Path.Combine(dst, "test.txt")));

        action.Commit();

        action.Rollback();
        Assert.True(Directory.Exists(src));
        Assert.False(Directory.Exists(dst));
        Assert.True(File.Exists(Path.Combine(src, "test.txt")));
    }

    [Fact]
    public void MoveCaseOnlyDirectoryAction_ExecuteAndRollback_Succeeds()
    {
        var src = Path.Combine(_tempDir, "folder");
        var dst = Path.Combine(_tempDir, "FOLDER");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "test.txt"), "hello");

        var action = new GitIndexManager.MoveCaseOnlyDirectoryAction(src, dst);
        action.Execute();

        Assert.True(Directory.Exists(dst));

        action.Commit();

        action.Rollback();
        Assert.True(Directory.Exists(src));
        Assert.True(File.Exists(Path.Combine(src, "test.txt")));
    }

    [Fact]
    public void EnsureDirectoryAction_CreatesDirectoryWhenNotPresent_AndRollbackRemovesIfEmpty()
    {
        var dir = Path.Combine(_tempDir, "sub", "deep");

        var action = new GitIndexManager.EnsureDirectoryAction(dir);
        action.Execute();

        Assert.True(Directory.Exists(dir));

        action.Commit(); // No-op

        action.Rollback();
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void EnsureDirectoryAction_DirectoryAlreadyExists_RollbackDoesNotDelete()
    {
        var dir = Path.Combine(_tempDir, "existing");
        Directory.CreateDirectory(dir);

        var action = new GitIndexManager.EnsureDirectoryAction(dir);
        action.Execute();

        action.Rollback();
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void EnsureDirectoryAction_NotEmpty_RollbackDoesNotDelete()
    {
        var dir = Path.Combine(_tempDir, "newdir");

        var action = new GitIndexManager.EnsureDirectoryAction(dir);
        action.Execute();

        File.WriteAllText(Path.Combine(dir, "file.txt"), "keep");

        action.Rollback();
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteEmptyDirectoryAction_ExecuteAndRollback_Succeeds()
    {
        var dir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(dir);

        var action = new GitIndexManager.DeleteEmptyDirectoryAction(dir);
        action.Execute();

        Assert.False(Directory.Exists(dir));

        action.Rollback();
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void MoveDirectoryLinkAction_ExecuteWithForce_RollbackAndCommit()
    {
        var src = Path.Combine(_tempDir, "linkSrc");
        var dst = Path.Combine(_tempDir, "linkDst");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(src, "f.txt"), "src");
        File.WriteAllText(Path.Combine(dst, "f.txt"), "dst");

        // Without force should throw
        var actionNoForce = new GitIndexManager.MoveDirectoryLinkAction(src, dst, force: false);
        Assert.Throws<IOException>(() => actionNoForce.Execute());

        // With force should succeed
        var actionForce = new GitIndexManager.MoveDirectoryLinkAction(src, dst, force: true);
        actionForce.Execute();

        Assert.True(Directory.Exists(dst));
        Assert.Equal("src", File.ReadAllText(Path.Combine(dst, "f.txt")));

        actionForce.Rollback();

        Assert.True(Directory.Exists(src));
        Assert.True(Directory.Exists(dst));
        Assert.Equal("src", File.ReadAllText(Path.Combine(src, "f.txt")));
        Assert.Equal("dst", File.ReadAllText(Path.Combine(dst, "f.txt")));
    }

    [Fact]
    public void MoveDirectoryLinkAction_DestinationIsFile_WithForce()
    {
        var src = Path.Combine(_tempDir, "linkSrc2");
        var dst = Path.Combine(_tempDir, "dstFile.txt");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "item.txt"), "src item");
        File.WriteAllText(dst, "dst file");

        var actionForce = new GitIndexManager.MoveDirectoryLinkAction(src, dst, force: true);
        actionForce.Execute();

        actionForce.Commit(); // Deletes backup file
        Assert.True(Directory.Exists(dst));
    }

    [Fact]
    public void FilesystemJournal_Commit_DoesNotRollbackOnDispose()
    {
        var src = Path.Combine(_tempDir, "j_src.txt");
        var dst = Path.Combine(_tempDir, "j_dst.txt");
        File.WriteAllText(src, "hello");

        using (var journal = new GitIndexManager.FilesystemJournal())
        {
            journal.MoveFile(src, dst, force: false);
            journal.EnsureDirectory(Path.Combine(_tempDir, "j_newdir"));
            journal.Commit();
        }

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "j_newdir")));
    }

    [Fact]
    public void FilesystemJournal_DisposeWithoutCommit_TriggersRollback()
    {
        var src = Path.Combine(_tempDir, "j_roll_src.txt");
        var dst = Path.Combine(_tempDir, "j_roll_dst.txt");
        File.WriteAllText(src, "hello");

        using (var journal = new GitIndexManager.FilesystemJournal())
        {
            journal.MoveFile(src, dst, force: false);
        }

        Assert.True(File.Exists(src));
        Assert.False(File.Exists(dst));
    }

    [Fact]
    public void FilesystemJournal_AllMethods_RecordAndExecute()
    {
        var dir1 = Path.Combine(_tempDir, "fj_dir1");
        var dir2 = Path.Combine(_tempDir, "fj_dir2");
        var emptyDir = Path.Combine(_tempDir, "fj_empty");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(emptyDir);
        File.WriteAllText(Path.Combine(dir1, "f.txt"), "data");

        using var journal = new GitIndexManager.FilesystemJournal();
        journal.EnsureDirectory(dir1); // Already exists branch
        journal.EnsureDirectory(Path.Combine(_tempDir, "fj_created")); // Not exists branch
        journal.DeleteEmptyDirectory(emptyDir);
        journal.MoveDirectory(dir1, dir2);
        journal.Commit();

        Assert.True(Directory.Exists(dir2));
        Assert.False(Directory.Exists(emptyDir));
    }

    [Fact]
    public void FilesystemJournal_MoveCaseOnlyFile_AndMoveCaseOnlyDirectory_Executes()
    {
        var fileSrc = Path.Combine(_tempDir, "fj_case_file.txt");
        var fileDst = Path.Combine(_tempDir, "FJ_CASE_FILE.TXT");
        File.WriteAllText(fileSrc, "content");

        var dirSrc = Path.Combine(_tempDir, "fj_case_dir");
        var dirDst = Path.Combine(_tempDir, "FJ_CASE_DIR");
        Directory.CreateDirectory(dirSrc);
        File.WriteAllText(Path.Combine(dirSrc, "file.txt"), "dircontent");

        using var journal = new GitIndexManager.FilesystemJournal();
        journal.MoveCaseOnlyFile(fileSrc, fileDst);
        journal.MoveCaseOnlyDirectory(dirSrc, dirDst);
        journal.Commit();

        Assert.True(File.Exists(fileDst));
        Assert.True(Directory.Exists(dirDst));
    }

    [Fact]
    public void FilesystemJournal_RollbackAndCommit_HandleExceptionsGracefully()
    {
        var failingAction = new ThrowingAction();
        using var journal = new GitIndexManager.FilesystemJournal();
        journal.Record(failingAction);

        // Rollback should not throw even if action throws
        journal.Rollback();

        // Commit should not throw even if action throws
        journal.Commit();
    }

    private sealed class ThrowingAction : GitIndexManager.IJournalAction
    {
        public void Rollback() => throw new InvalidOperationException("Rollback failed");
        public void Commit() => throw new InvalidOperationException("Commit failed");
    }

    [Fact]
    public void MoveDirectoryAction_Rollback_WhenAlreadyExists_DoesNothing()
    {
        var src = Path.Combine(_tempDir, "srcExists");
        var dst = Path.Combine(_tempDir, "dstExists");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dst);

        var action = new GitIndexManager.MoveDirectoryAction(src, dst);
        action.Rollback(); // Both exist, so should not move

        Assert.True(Directory.Exists(src));
        Assert.True(Directory.Exists(dst));
    }

    [Fact]
    public void MoveDirectoryLinkAction_Execute_DestinationDoesNotExist_MovesDirectly()
    {
        var src = Path.Combine(_tempDir, "linkSrcDirect");
        var dst = Path.Combine(_tempDir, "linkDstDirect");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "test.txt"), "hello");

        var action = new GitIndexManager.MoveDirectoryLinkAction(src, dst, force: false);
        action.Execute();

        Assert.False(Directory.Exists(src));
        Assert.True(Directory.Exists(dst));

        action.Rollback();
        Assert.True(Directory.Exists(src));
        Assert.False(Directory.Exists(dst));
    }
}
