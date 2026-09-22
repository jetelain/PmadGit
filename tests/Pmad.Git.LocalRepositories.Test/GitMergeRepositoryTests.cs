using System.Text;
using Pmad.Git.Tests.Infrastructure;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitMergeRepositoryTests
{
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").TrimEnd();

    [Fact]
    public async Task MergeAsync_AlreadyUpToDate_ReturnsAlreadyUpToDate()
    {
        using var testRepo = GitTestRepository.Create();
        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("HEAD");

        Assert.True(result.IsSuccess);
        Assert.False(result.HasConflicts);
        Assert.Equal(GitMergeStatus.AlreadyUpToDate, result.Status);
        Assert.False(await workspace.IsMergeInProgressAsync());
    }

    [Fact]
    public async Task MergeAsync_FastForward_AdvancesHeadAndSyncsWorkspace()
    {
        using var testRepo = GitTestRepository.Create();
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Create feature branch with a new file
        testRepo.RunGit("checkout -b feature");
        var featureFilePath = Path.Combine(testRepo.WorkingDirectory, "feature.txt");
        await File.WriteAllTextAsync(featureFilePath, "feature content\n");
        testRepo.RunGit("add feature.txt");
        testRepo.RunGit("commit -m \"Add feature\"");
        var featureCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Switch back to master
        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("feature");

        Assert.True(result.IsSuccess);
        Assert.Equal(GitMergeStatus.FastForward, result.Status);
        Assert.Equal(featureCommit, result.CommitHash?.ToString());
        Assert.False(await workspace.IsMergeInProgressAsync());

        // Workspace file must exist with correct content
        Assert.True(File.Exists(featureFilePath));
        Assert.Equal("feature content\n", await File.ReadAllTextAsync(featureFilePath));

        // HEAD must point to featureCommit
        var newHead = (await workspace.ReferenceStore.ResolveHeadAsync()).ToString();
        Assert.Equal(featureCommit, newHead);
    }

    [Fact]
    public async Task MergeAsync_NoFastForward_CreatesMergeCommit()
    {
        using var testRepo = GitTestRepository.Create();
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        testRepo.RunGit("checkout -b feature");
        var featureFilePath = Path.Combine(testRepo.WorkingDirectory, "feature.txt");
        await File.WriteAllTextAsync(featureFilePath, "feature content\n");
        testRepo.RunGit("add feature.txt");
        testRepo.RunGit("commit -m \"Add feature\"");
        var featureCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("feature", new GitMergeOptions { NoFastForward = true });

        Assert.True(result.IsSuccess);
        Assert.Equal(GitMergeStatus.Merged, result.Status);
        Assert.NotNull(result.CommitHash);
        Assert.NotEqual(featureCommit, result.CommitHash.Value.ToString());

        var mergeCommit = await workspace.GetCommitAsync(result.CommitHash.Value.ToString());
        Assert.Equal(2, mergeCommit.Parents.Count);
        Assert.Equal(baseCommit, mergeCommit.Parents[0].ToString());
        Assert.Equal(featureCommit, mergeCommit.Parents[1].ToString());
    }

    [Fact]
    public async Task MergeAsync_FastForwardOnly_ThrowsWhenDiverged()
    {
        using var testRepo = GitTestRepository.Create();
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Main branch commit
        var mainFile = Path.Combine(testRepo.WorkingDirectory, "main.txt");
        await File.WriteAllTextAsync(mainFile, "main\n");
        testRepo.RunGit("add main.txt");
        testRepo.RunGit("commit -m \"main commit\"");

        // Feature branch commit from base
        testRepo.RunGit($"checkout -b feature {baseCommit}");
        var featureFile = Path.Combine(testRepo.WorkingDirectory, "feature.txt");
        await File.WriteAllTextAsync(featureFile, "feature\n");
        testRepo.RunGit("add feature.txt");
        testRepo.RunGit("commit -m \"feature commit\"");

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.MergeAsync("feature", new GitMergeOptions { FastForwardOnly = true }));
    }

    [Fact]
    public async Task MergeAsync_CleanThreeWayMerge_CreatesMergeCommit()
    {
        using var testRepo = GitTestRepository.Create();
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Main branch changes
        var fileA = Path.Combine(testRepo.WorkingDirectory, "fileA.txt");
        await File.WriteAllTextAsync(fileA, "fileA from main\n");
        testRepo.RunGit("add fileA.txt");
        testRepo.RunGit("commit -m \"main changes fileA\"");
        var mainCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Feature branch changes
        testRepo.RunGit($"checkout -b feature {baseCommit}");
        var fileB = Path.Combine(testRepo.WorkingDirectory, "fileB.txt");
        await File.WriteAllTextAsync(fileB, "fileB from feature\n");
        testRepo.RunGit("add fileB.txt");
        testRepo.RunGit("commit -m \"feature changes fileB\"");
        var featureCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("feature");

        Assert.True(result.IsSuccess);
        Assert.Equal(GitMergeStatus.Merged, result.Status);
        Assert.NotNull(result.CommitHash);
        Assert.False(await workspace.IsMergeInProgressAsync());

        // Both files exist in workspace
        Assert.True(File.Exists(fileA));
        Assert.Equal(Normalize("fileA from main\n"), Normalize(await File.ReadAllTextAsync(fileA)));
        Assert.True(File.Exists(fileB));
        Assert.Equal(Normalize("fileB from feature\n"), Normalize(await File.ReadAllTextAsync(fileB)));

        // Commit has 2 parents
        var commit = await workspace.GetCommitAsync(result.CommitHash.Value.ToString());
        Assert.Equal(2, commit.Parents.Count);
        Assert.Equal(mainCommit, commit.Parents[0].ToString());
        Assert.Equal(featureCommit, commit.Parents[1].ToString());

        // Working tree and index are clean
        var status = await workspace.GetStatusAsync();
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task MergeAsync_CleanThreeWayMerge_DisjointEditsInSameFile_MatchesGitCli()
    {
        using var testRepo = GitTestRepository.Create();
        var sharedFile = Path.Combine(testRepo.WorkingDirectory, "shared.txt");
        var initialContent = "line 1\nline 2\nline 3\nline 4\nline 5\n";
        await File.WriteAllTextAsync(sharedFile, initialContent);
        testRepo.RunGit("add shared.txt");
        testRepo.RunGit("commit -m \"Initial shared.txt\"");
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Main edits line 1
        var mainContent = "line 1 modified by main\nline 2\nline 3\nline 4\nline 5\n";
        await File.WriteAllTextAsync(sharedFile, mainContent);
        testRepo.RunGit("add shared.txt");
        testRepo.RunGit("commit -m \"Main edits line 1\"");

        // Feature edits line 5
        testRepo.RunGit($"checkout -b feature {baseCommit}");
        var featureContent = "line 1\nline 2\nline 3\nline 4\nline 5 modified by feature\n";
        await File.WriteAllTextAsync(sharedFile, featureContent);
        testRepo.RunGit("add shared.txt");
        testRepo.RunGit("commit -m \"Feature edits line 5\"");

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("feature");

        Assert.True(result.IsSuccess);
        Assert.Equal(GitMergeStatus.Merged, result.Status);

        var expectedContent = "line 1 modified by main\nline 2\nline 3\nline 4\nline 5 modified by feature\n";
        var actualContent = await File.ReadAllTextAsync(sharedFile);
        Assert.Equal(Normalize(expectedContent), Normalize(actualContent));
    }

    [Fact]
    public async Task MergeAsync_ConflictedThreeWayMerge_GeneratesConflictMarkersAndStages()
    {
        using var testRepo = GitTestRepository.Create();
        var conflictFile = Path.Combine(testRepo.WorkingDirectory, "conflict.txt");
        await File.WriteAllTextAsync(conflictFile, "initial content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Initial conflict.txt\"");
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Main edits conflict.txt
        await File.WriteAllTextAsync(conflictFile, "main content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Main edits\"");

        // Feature edits conflict.txt differently
        testRepo.RunGit($"checkout -b feature {baseCommit}");
        await File.WriteAllTextAsync(conflictFile, "feature content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Feature edits\"");

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("feature");

        Assert.False(result.IsSuccess);
        Assert.True(result.HasConflicts);
        Assert.Equal(GitMergeStatus.Conflicted, result.Status);
        Assert.Contains("conflict.txt", result.ConflictedFiles);
        Assert.True(await workspace.IsMergeInProgressAsync());

        // Check conflicted files method
        var conflicted = await workspace.GetConflictedFilesAsync();
        Assert.Single(conflicted, "conflict.txt");

        // Verify conflict markers in workspace file
        var fileText = await File.ReadAllTextAsync(conflictFile);
        Assert.Contains("<<<<<<< HEAD", fileText);
        Assert.Contains("main content", fileText);
        Assert.Contains("=======", fileText);
        Assert.Contains("feature content", fileText);
        Assert.Contains(">>>>>>> feature", fileText);

        // Verify status reports conflicted
        var status = await workspace.GetStatusAsync();
        var entry = Assert.Single(status.Entries, e => e.Path == "conflict.txt");
        Assert.True(entry.IsConflicted);
    }

    [Fact]
    public async Task ResolveConflictAsync_And_ContinueMergeAsync_CompletesMerge()
    {
        using var testRepo = GitTestRepository.Create();
        var conflictFile = Path.Combine(testRepo.WorkingDirectory, "conflict.txt");
        await File.WriteAllTextAsync(conflictFile, "initial content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Initial conflict.txt\"");
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        await File.WriteAllTextAsync(conflictFile, "main content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Main edits\"");
        var mainCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        testRepo.RunGit($"checkout -b feature {baseCommit}");
        await File.WriteAllTextAsync(conflictFile, "feature content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Feature edits\"");
        var featureCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var mergeResult = await workspace.MergeAsync("feature");
        Assert.False(mergeResult.IsSuccess);

        // Resolve the conflict manually
        await File.WriteAllTextAsync(conflictFile, "resolved content\n");
        await workspace.ResolveConflictAsync("conflict.txt");

        // No more conflicted files
        var remaining = await workspace.GetConflictedFilesAsync();
        Assert.Empty(remaining);

        // Continue merge
        var commitHash = await workspace.ContinueMergeAsync("Merge feature resolved");

        Assert.False(await workspace.IsMergeInProgressAsync());

        var commit = await workspace.GetCommitAsync(commitHash.Value);
        Assert.Equal(2, commit.Parents.Count);
        Assert.Equal(mainCommit, commit.Parents[0].ToString());
        Assert.Equal(featureCommit, commit.Parents[1].ToString());

        var status = await workspace.GetStatusAsync();
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task AbortMergeAsync_RestoresPreMergeState()
    {
        using var testRepo = GitTestRepository.Create();
        var conflictFile = Path.Combine(testRepo.WorkingDirectory, "conflict.txt");
        await File.WriteAllTextAsync(conflictFile, "initial content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Initial conflict.txt\"");
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        await File.WriteAllTextAsync(conflictFile, "main content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Main edits\"");
        var mainCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        testRepo.RunGit($"checkout -b feature {baseCommit}");
        await File.WriteAllTextAsync(conflictFile, "feature content\n");
        testRepo.RunGit("add conflict.txt");
        testRepo.RunGit("commit -m \"Feature edits\"");

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var mergeResult = await workspace.MergeAsync("feature");
        Assert.False(mergeResult.IsSuccess);
        Assert.True(await workspace.IsMergeInProgressAsync());

        await workspace.AbortMergeAsync();

        Assert.False(await workspace.IsMergeInProgressAsync());

        // File is restored to main content
        var content = await File.ReadAllTextAsync(conflictFile);
        Assert.Equal(Normalize("main content\n"), Normalize(content));

        // HEAD is unchanged
        var head = (await workspace.ReferenceStore.ResolveHeadAsync()).ToString();
        Assert.Equal(mainCommit, head);

        var status = await workspace.GetStatusAsync();
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task MergeAsync_DeleteModifyConflict_Detected()
    {
        using var testRepo = GitTestRepository.Create();
        var file = Path.Combine(testRepo.WorkingDirectory, "file.txt");
        await File.WriteAllTextAsync(file, "initial\n");
        testRepo.RunGit("add file.txt");
        testRepo.RunGit("commit -m \"Add file\"");
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        // Main deletes file
        testRepo.RunGit("rm file.txt");
        testRepo.RunGit("commit -m \"Delete file\"");

        // Feature modifies file
        testRepo.RunGit($"checkout -b feature {baseCommit}");
        await File.WriteAllTextAsync(file, "modified by feature\n");
        testRepo.RunGit("add file.txt");
        testRepo.RunGit("commit -m \"Modify file\"");

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("feature");

        Assert.False(result.IsSuccess);
        Assert.True(result.HasConflicts);
        Assert.Contains("file.txt", result.ConflictedFiles);
        Assert.True(await workspace.IsMergeInProgressAsync());
    }

    [Fact]
    public async Task MergeAsync_FastForward_UntrackedFileWouldBeOverwritten_ThrowsAndLeavesHeadUnchanged()
    {
        using var testRepo = GitTestRepository.Create();
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        testRepo.RunGit("checkout -b feature");
        var featureFilePath = Path.Combine(testRepo.WorkingDirectory, "feature.txt");
        await File.WriteAllTextAsync(featureFilePath, "from feature\n");
        testRepo.RunGit("add feature.txt");
        testRepo.RunGit("commit -m \"Add feature\"");

        testRepo.RunGit("checkout master");

        // Create untracked file at the exact target path
        await File.WriteAllTextAsync(featureFilePath, "untracked local content\n");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.MergeAsync("feature"));

        Assert.Contains("untracked working tree file", ex.Message);

        // HEAD must NOT have moved
        var currentHead = (await workspace.ReferenceStore.ResolveHeadAsync()).ToString();
        Assert.Equal(baseCommit, currentHead);

        // Untracked file must NOT be overwritten
        Assert.Equal("untracked local content\n", await File.ReadAllTextAsync(featureFilePath));
    }

    [Fact]
    public async Task MergeAsync_NonUtf8BlobConflict_PreservesWorkingCopyAndSetsConflictStages()
    {
        using var testRepo = GitTestRepository.Create();
        var baseBytes = new byte[] { 0xC0, 0xAF, 0x80, 0x81, 0x82 };
        var masterBytes = new byte[] { 0xC0, 0xAF, 0xFF, 0xFE, 0x80 };
        var featureBytes = new byte[] { 0xC0, 0xAF, 0x88, 0x99, 0xAA };

        var filePath = Path.Combine(testRepo.WorkingDirectory, "blob.bin");
        await File.WriteAllBytesAsync(filePath, baseBytes);
        testRepo.RunGit("add blob.bin");
        testRepo.RunGit("commit -m \"Base blob\"");
        var baseCommit = testRepo.RunGit("rev-parse HEAD").Trim();

        await File.WriteAllBytesAsync(filePath, masterBytes);
        testRepo.RunGit("add blob.bin");
        testRepo.RunGit("commit -m \"Master blob\"");

        testRepo.RunGit($"checkout -b feature {baseCommit}");
        await File.WriteAllBytesAsync(filePath, featureBytes);
        testRepo.RunGit("add blob.bin");
        testRepo.RunGit("commit -m \"Feature blob\"");

        testRepo.RunGit("checkout master");

        using var workspace = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var result = await workspace.MergeAsync("feature");

        Assert.False(result.IsSuccess);
        Assert.True(result.HasConflicts);
        Assert.Contains("blob.bin", result.ConflictedFiles);

        // Working tree file must be preserved as master's byte content (not corrupted with replacement chars or text markers)
        var actualBytes = await File.ReadAllBytesAsync(filePath);
        Assert.Equal(masterBytes, actualBytes);

        var conflicted = await workspace.GetConflictedFilesAsync();
        Assert.Single(conflicted, "blob.bin");
    }
}

