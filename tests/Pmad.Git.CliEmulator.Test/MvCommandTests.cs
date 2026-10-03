using System.IO;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class MvCommandTests
{
    [Fact]
    public async Task Mv_FileToNewPath_Success()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var filePath = Path.Combine(testRepo.WorkingDirectory, "file1.txt");
        await File.WriteAllTextAsync(filePath, "test content");
        await emulator.InvokeAsync(["add", "file1.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add file1"], approval);

        var response = await emulator.InvokeAsync(["mv", "file1.txt", "file2.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Empty(response.StdOut);
        Assert.False(File.Exists(filePath));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "file2.txt")));

        var status = await emulator.InvokeAsync(["status"], approval);
        Assert.Contains("deleted:", status.StdOut);
        Assert.Contains("file1.txt", status.StdOut);
        Assert.Contains("new file:", status.StdOut);
        Assert.Contains("file2.txt", status.StdOut);
    }

    [Fact]
    public async Task Mv_MultipleFiles_IntoDirectory_Success()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "sub"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "B");
        await emulator.InvokeAsync(["add", "a.txt", "b.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add files"], approval);

        var response = await emulator.InvokeAsync(["mv", "a.txt", "b.txt", "sub"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "sub", "a.txt")));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "sub", "b.txt")));
    }

    [Fact]
    public async Task Mv_DirectoryRename_Success()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var dir1 = Path.Combine(testRepo.WorkingDirectory, "dir1");
        Directory.CreateDirectory(dir1);
        await File.WriteAllTextAsync(Path.Combine(dir1, "a.txt"), "hello");
        await emulator.InvokeAsync(["add", "dir1/a.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add dir1 file"], approval);

        var response = await emulator.InvokeAsync(["mv", "dir1", "dir2"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.False(Directory.Exists(dir1));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "dir2", "a.txt")));
    }

    [Fact]
    public async Task Mv_DryRun_PrintsCheckingAndDoesNotMove()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var filePath = Path.Combine(testRepo.WorkingDirectory, "a.txt");
        await File.WriteAllTextAsync(filePath, "data");
        await emulator.InvokeAsync(["add", "a.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add a.txt"], approval);

        var response = await emulator.InvokeAsync(["mv", "-n", "a.txt", "b.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Checking rename of 'a.txt' to 'b.txt'", response.StdOut);
        Assert.Contains("Renaming a.txt to b.txt", response.StdOut);
        Assert.True(File.Exists(filePath));
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
    }

    [Fact]
    public async Task Mv_Verbose_PrintsRenaming()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var filePath = Path.Combine(testRepo.WorkingDirectory, "a.txt");
        await File.WriteAllTextAsync(filePath, "data");
        await emulator.InvokeAsync(["add", "a.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add a.txt"], approval);

        var response = await emulator.InvokeAsync(["mv", "-v", "a.txt", "b.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Renaming a.txt to b.txt", response.StdOut);
        Assert.DoesNotContain("Checking rename", response.StdOut);
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
    }

    [Fact]
    public async Task Mv_NoArgs_Returns129()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);

        var response = await emulator.InvokeAsync(["mv"]);

        Assert.Equal(129, response.ExitCode);
        Assert.Contains("usage: git mv", response.StdErr);
    }

    [Fact]
    public async Task Mv_OneArg_Returns129()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);

        var response = await emulator.InvokeAsync(["mv", "file.txt"]);

        Assert.Equal(129, response.ExitCode);
        Assert.Contains("usage: git mv", response.StdErr);
    }

    [Fact]
    public async Task Mv_BadSource_Returns128WithFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);

        var response = await emulator.InvokeAsync(["mv", "missing.txt", "dest.txt"]);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal: bad source", response.StdErr);
    }

    [Fact]
    public async Task Mv_NotUnderVersionControl_Returns128WithFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "untracked.txt"), "data");

        var response = await emulator.InvokeAsync(["mv", "untracked.txt", "dest.txt"]);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal: not under version control", response.StdErr);
    }

    [Fact]
    public async Task Mv_DestinationExists_WithoutForce_Returns128WithFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "B");
        await emulator.InvokeAsync(["add", "a.txt", "b.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add files"], approval);

        var response = await emulator.InvokeAsync(["mv", "a.txt", "b.txt"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal: destination exists", response.StdErr);
    }

    [Fact]
    public async Task Mv_DestinationExists_WithForce_PromptsApproval_AndOverwrites()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "new content from A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "old content from B");
        await emulator.InvokeAsync(["add", "a.txt", "b.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add files"], approval);

        var response = await emulator.InvokeAsync(["mv", "-f", "a.txt", "b.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("mv --force", approval.DiscardLocalChangesCalls[0].Operation);
        Assert.Contains("b.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
        Assert.Equal("new content from A", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
    }

    [Fact]
    public async Task Mv_DestinationExists_WithForce_WhenDenied_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            DiscardLocalChangesResult = ApprovalResult.Denied
        };

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "new content from A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "old content from B");
        await repo.StageAsync(["a.txt", "b.txt"]);
        await repo.CommitAsync("Add files");

        var response = await emulator.InvokeAsync(["mv", "-f", "a.txt", "b.txt"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "a.txt")));
        Assert.Equal("old content from B", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
    }

    [Fact]
    public async Task Mv_SkipErrors_SkipsInvalidAndMovesValid()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "sub"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "valid.txt"), "valid data");
        await emulator.InvokeAsync(["add", "valid.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add valid"], approval);

        var response = await emulator.InvokeAsync(["mv", "-k", "valid.txt", "missing.txt", "sub"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "sub", "valid.txt")));
    }

    [Fact]
    public async Task Mv_Force_WhenDestinationInIndexDeletedOnDisk_RequestsApproval()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt"), "B");
        await emulator.InvokeAsync(["add", "a.txt", "b.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add files"], approval);

        // Delete b.txt on disk but leave it in index
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "b.txt"));

        var response = await emulator.InvokeAsync(["mv", "-f", "a.txt", "b.txt"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("mv --force", approval.DiscardLocalChangesCalls[0].Operation);
        Assert.Contains("b.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
        Assert.Equal("A", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "b.txt")));
    }

    [Fact]
    public async Task Mv_DirectoryMerge_UntrackedFileOverwritesExisting_RequiresApproval()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Tracked src/a.txt
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "src"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "src", "a.txt"), "A");
        await emulator.InvokeAsync(["add", "src/a.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Add src/a.txt"], approval);

        // Untracked src/notes.txt
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "src", "notes.txt"), "untracked notes in src");

        // Existing out/src/notes.txt
        Directory.CreateDirectory(Path.Combine(testRepo.WorkingDirectory, "out", "src"));
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "out", "src", "notes.txt"), "existing notes in out");

        var response = await emulator.InvokeAsync(["mv", "-f", "src", "out"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("mv --force", approval.DiscardLocalChangesCalls[0].Operation);
        Assert.Contains("out/src/notes.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
        Assert.Equal("untracked notes in src", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "out", "src", "notes.txt")));
    }
}
