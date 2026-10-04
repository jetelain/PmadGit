using System.IO;
using Pmad.Git.CliEmulator.Internal.Formatters;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class StatusFormatterTests
{
    [Fact]
    public async Task WriteAsync_DetachedHead_OutputsDetachedMessage()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Second commit", ("file2.txt", "content2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Detach HEAD to current commit
        var headCommit = await repo.GetCommitAsync("HEAD");
        await repo.CheckoutCommitAsync(headCommit.Id.ToString());

        var sw = new StringWriter();
        await StatusFormatter.WriteAsync(repo, sw, CancellationToken.None);
        var output = sw.ToString();

        Assert.Contains("HEAD detached at", output);
    }

    [Fact]
    public async Task WriteAsync_StagedNewModifiedDeleted_OutputsSections()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file_del.txt", "del"), ("file_mod.txt", "mod"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Stage new file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file_new.txt"), "new");
        await repo.StageAsync("file_new.txt");

        // Stage modified file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file_mod.txt"), "modified");
        await repo.StageAsync("file_mod.txt");

        // Stage deleted file
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "file_del.txt"));
        await repo.StageAsync("file_del.txt");

        var sw = new StringWriter();
        await StatusFormatter.WriteAsync(repo, sw, CancellationToken.None);
        var output = sw.ToString();

        Assert.Contains("Changes to be committed:", output);
        Assert.Contains("new file:     file_new.txt", output);
        Assert.Contains("modified:     file_mod.txt", output);
        Assert.Contains("deleted:      file_del.txt", output);
    }

    [Fact]
    public async Task WriteShortAsync_OutputsExpectedCodes()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file_mod.txt", "mod"), ("file_del.txt", "del"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Untracked file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "untracked.txt"), "untracked");

        // Staged new file
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "staged_new.txt"), "staged");
        await repo.StageAsync("staged_new.txt");

        // Unstaged modification
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "file_mod.txt"), "unstaged modification");

        // Unstaged deletion
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "file_del.txt"));

        var sw = new StringWriter();
        await StatusFormatter.WriteShortAsync(repo, sw, CancellationToken.None);
        var output = sw.ToString();

        Assert.Contains("?? untracked.txt", output);
        Assert.Contains("A  staged_new.txt", output);
        Assert.Contains(" M file_mod.txt", output);
        Assert.Contains(" D file_del.txt", output);
    }

    [Fact]
    public async Task WriteShortAsync_StagedRename_OutputsR()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("old_name.txt", "identical-content-for-blob-match"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // Simulate rename by deleting old_name and staging new_name with same content
        File.Delete(Path.Combine(testRepo.WorkingDirectory, "old_name.txt"));
        await repo.StageAsync("old_name.txt");

        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "new_name.txt"), "identical-content-for-blob-match");
        await repo.StageAsync("new_name.txt");

        var sw = new StringWriter();
        await StatusFormatter.WriteShortAsync(repo, sw, CancellationToken.None);
        var output = sw.ToString();

        Assert.Contains("R  old_name.txt -> new_name.txt", output);
    }
}
