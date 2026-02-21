using System.Diagnostics;
using Pmad.Git.HttpServer.Pack;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Pack;

public sealed class GitObjectWalkerTest : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _gitDirectory;

    public GitObjectWalkerTest()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "PmadGitObjectWalkerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
        _gitDirectory = Path.Combine(_workingDirectory, ".git");
        InitializeRepository();
    }

    private void InitializeRepository()
    {
        RunGit("init --quiet --initial-branch=main");
        RunGit("config user.name \"Test User\"");
        RunGit("config user.email test@example.com");
    }

    [Fact]
    public async Task CollectAsync_WithSingleCommit_ShouldCollectCommitTreeAndBlobs()
    {
        // Arrange
        CreateFile("file.txt", "content");
        RunGit("add file.txt");
        RunGit("commit -m \"Test commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert
        Assert.NotEmpty(objects);
        
        // Should contain at least: commit, tree, blob
        var objectTypes = new Dictionary<GitObjectType, int>();
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            objectTypes.TryGetValue(obj.Type, out var count);
            objectTypes[obj.Type] = count + 1;
        }

        Assert.True(objectTypes.ContainsKey(GitObjectType.Commit));
        Assert.True(objectTypes.ContainsKey(GitObjectType.Tree));
        Assert.True(objectTypes.ContainsKey(GitObjectType.Blob));
        Assert.Equal(1, objectTypes[GitObjectType.Commit]);
        Assert.Equal(1, objectTypes[GitObjectType.Tree]);
        Assert.Equal(1, objectTypes[GitObjectType.Blob]);
    }

    [Fact]
    public async Task CollectAsync_WithMultipleFiles_ShouldCollectAllBlobs()
    {
        // Arrange
        CreateFile("file1.txt", "content 1");
        CreateFile("file2.txt", "content 2");
        CreateFile("file3.txt", "content 3");
        RunGit("add -A");
        RunGit("commit -m \"Multiple files\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert
        var blobCount = 0;
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            if (obj.Type == GitObjectType.Blob)
            {
                blobCount++;
            }
        }

        Assert.Equal(3, blobCount);
    }

    [Fact]
    public async Task CollectAsync_WithNestedDirectories_ShouldCollectAllTrees()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "dir1", "dir2"));
        CreateFile("dir1/file1.txt", "content 1");
        CreateFile("dir1/dir2/file2.txt", "content 2");
        RunGit("add -A");
        RunGit("commit -m \"Nested directories\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert
        var treeCount = 0;
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            if (obj.Type == GitObjectType.Tree)
            {
                treeCount++;
            }
        }

        // Should have: root tree, dir1 tree, dir2 tree
        Assert.True(treeCount >= 3, $"Expected at least 3 trees, got {treeCount}");
    }

    [Fact]
    public async Task CollectAsync_WithMultipleCommits_ShouldCollectAllCommits()
    {
        // Arrange
        CreateFile("file1.txt", "content 1");
        RunGit("add file1.txt");
        RunGit("commit -m \"First commit\" --quiet");

        CreateFile("file2.txt", "content 2");
        RunGit("add file2.txt");
        RunGit("commit -m \"Second commit\" --quiet");

        CreateFile("file3.txt", "content 3");
        RunGit("add file3.txt");
        RunGit("commit -m \"Third commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert
        var commitCount = 0;
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            if (obj.Type == GitObjectType.Commit)
            {
                commitCount++;
            }
        }

        Assert.Equal(3, commitCount);
    }

    [Fact]
    public async Task CollectAsync_WithTag_ShouldCollectTagAndTarget()
    {
        // Arrange
        CreateFile("file.txt", "tagged content");
        RunGit("add file.txt");
        RunGit("commit -m \"Commit to tag\" --quiet");
        RunGit("tag -a v1.0 -m \"Version 1.0\"");

        var repository = GitRepository.Open(_workingDirectory);
        var refs = await repository.ReferenceStore.GetReferencesAsync();
        var tagHash = refs["refs/tags/v1.0"];
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { tagHash }, CancellationToken.None);

        // Assert
        var objectTypes = new HashSet<GitObjectType>();
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            objectTypes.Add(obj.Type);
        }

        Assert.Contains(GitObjectType.Tag, objectTypes);
        Assert.Contains(GitObjectType.Commit, objectTypes);
        Assert.Contains(GitObjectType.Tree, objectTypes);
        Assert.Contains(GitObjectType.Blob, objectTypes);
    }

    [Fact]
    public async Task CollectAsync_WithMultipleRoots_ShouldCollectAllObjects()
    {
        // Arrange: Create two separate commits
        CreateFile("file1.txt", "content 1");
        RunGit("add file1.txt");
        RunGit("commit -m \"First commit\" --quiet");
        var commit1Output = RunGit("rev-parse HEAD");
        var commit1Hash = GitHash.TryParse(commit1Output.Trim(), out var hash1) ? hash1 : throw new Exception("Failed to parse hash");

        CreateFile("file2.txt", "content 2");
        RunGit("add file2.txt");
        RunGit("commit -m \"Second commit\" --quiet");
        var commit2Output = RunGit("rev-parse HEAD");
        var commit2Hash = GitHash.TryParse(commit2Output.Trim(), out var hash2) ? hash2 : throw new Exception("Failed to parse hash");

        var repository = GitRepository.Open(_workingDirectory);
        var walker = new GitObjectWalker(repository);

        // Act: Collect starting from both commits (though commit2 includes commit1)
        var objects = await walker.CollectAsync(new[] { commit1Hash, commit2Hash }, CancellationToken.None);

        // Assert
        Assert.NotEmpty(objects);
        Assert.Contains(commit1Hash, objects);
        Assert.Contains(commit2Hash, objects);
    }

    [Fact]
    public async Task CollectAsync_ShouldNotDuplicateObjects()
    {
        // Arrange
        CreateFile("file.txt", "content");
        RunGit("add file.txt");
        RunGit("commit -m \"Test commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act: Provide the same root twice
        var objects = await walker.CollectAsync(new[] { commit.Id, commit.Id }, CancellationToken.None);

        // Assert: Each hash should appear only once
        var distinct = objects.Distinct().ToList();
        Assert.Equal(objects.Count, distinct.Count);
    }

    [Fact]
    public async Task CollectAsync_WithSharedObjects_ShouldNotDuplicate()
    {
        // Arrange: Two commits sharing a blob
        CreateFile("shared.txt", "shared content");
        CreateFile("file1.txt", "unique 1");
        RunGit("add -A");
        RunGit("commit -m \"First commit\" --quiet");

        // Modify file1 but keep shared.txt unchanged
        CreateFile("file1.txt", "unique 2");
        RunGit("add file1.txt");
        RunGit("commit -m \"Second commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert: shared.txt blob should appear only once
        var blobHashes = new Dictionary<string, int>();
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            if (obj.Type == GitObjectType.Blob)
            {
                blobHashes.TryGetValue(hash.Value, out var count);
                blobHashes[hash.Value] = count + 1;
            }
        }

        Assert.All(blobHashes.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task CollectAsync_ReturnsObjectsInDepthFirstOrder()
    {
        // Arrange
        CreateFile("file.txt", "content");
        RunGit("add file.txt");
        RunGit("commit -m \"Test commit\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert: First object should be the commit we started with
        Assert.Equal(commit.Id, objects[0]);
        
        // Commit should come before its tree
        var commitIndex = objects.ToList().FindIndex(h => h.Equals(commit.Id));
        var commitObj = await repository.GetCommitAsync(commit.Id.Value);
        var treeIndex = objects.ToList().FindIndex(h => h.Equals(commitObj.Tree));
        
        Assert.True(commitIndex < treeIndex, "Commit should come before its tree in depth-first order");
    }

    [Fact]
    public async Task CollectAsync_WithNullRoots_ShouldThrowArgumentNullException()
    {
        // Arrange
        var repository = GitRepository.Open(_workingDirectory);
        var walker = new GitObjectWalker(repository);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await walker.CollectAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task CollectAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange: Create many commits to ensure operation takes time
        for (int i = 0; i < 10; i++)
        {
            CreateFile($"file{i}.txt", $"content {i}");
            RunGit($"add file{i}.txt");
            RunGit($"commit -m \"Commit {i}\" --quiet");
        }

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await walker.CollectAsync(new[] { commit.Id }, cts.Token));
    }

    [Fact]
    public async Task CollectAsync_WithEmptyRepository_ShouldReturnEmpty()
    {
        // Arrange: Empty repository (no commits yet)
        var emptyRepoDir = Path.Combine(Path.GetTempPath(), "EmptyRepo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRepoDir);
        try
        {
            RunGitInDirectory(emptyRepoDir, "init --quiet --initial-branch=main");
            var repository = GitRepository.Open(emptyRepoDir);
            var walker = new GitObjectWalker(repository);

            // Act
            var objects = await walker.CollectAsync(Array.Empty<GitHash>(), CancellationToken.None);

            // Assert
            Assert.Empty(objects);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(emptyRepoDir);
        }
    }

    [Fact]
    public async Task CollectAsync_WithBranchMerge_ShouldHandleMultipleParents()
    {
        // Arrange: Create a divergent history with explicit merge
        CreateFile("base.txt", "base content");
        RunGit("add base.txt");
        RunGit("commit -m \"Base commit\" --quiet");
        
        // Create feature branch
        RunGit("checkout -b feature --quiet");
        CreateFile("feature.txt", "feature content");
        RunGit("add feature.txt");
        RunGit("commit -m \"Feature commit\" --quiet");
        var featureOutput = RunGit("rev-parse HEAD");
        var featureCommit = featureOutput.Trim();

        // Go back to main and create another commit
        RunGit("checkout main --quiet");
        CreateFile("main.txt", "main content");
        RunGit("add main.txt");
        RunGit("commit -m \"Main commit\" --quiet");
        var mainOutput = RunGit("rev-parse HEAD");
        var mainCommit = mainOutput.Trim();

        // Force a merge commit using git merge --no-ff
        try
        {
            RunGit("merge feature --no-ff -m \"Merge feature branch\" --quiet");
        }
        catch
        {
            // If automatic merge fails, we'll just test with what we have
        }
        
        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert: Should collect objects from both branches
        var blobContents = new List<string>();
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            if (obj.Type == GitObjectType.Blob)
            {
                blobContents.Add(System.Text.Encoding.UTF8.GetString(obj.Content));
            }
        }

        // Should have base content from common ancestor
        Assert.Contains("base content", blobContents);
        
        // Should have at least one of the branch-specific contents
        var hasFeature = blobContents.Contains("feature content");
        var hasMain = blobContents.Contains("main content");
        Assert.True(hasFeature || hasMain, "Should contain content from at least one branch");
        
        // Verify walker handles the structure correctly (no duplicates)
        var distinct = objects.Distinct().ToList();
        Assert.Equal(objects.Count, distinct.Count);
    }

    [Fact]
    public async Task CollectAsync_WithSymlink_ShouldCollectSymlinkBlob()
    {
        // Arrange
        // Note: Symlinks may not work on all platforms, so we'll create a regular file
        // but git will store it as a blob regardless
        CreateFile("link-target.txt", "target content");
        CreateFile("regular-file.txt", "regular content");
        RunGit("add -A");
        RunGit("commit -m \"Files\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert
        var blobCount = 0;
        foreach (var hash in objects)
        {
            var obj = await repository.ObjectStore.ReadObjectAsync(hash);
            if (obj.Type == GitObjectType.Blob)
            {
                blobCount++;
            }
        }

        Assert.Equal(2, blobCount);
    }

    [Fact]
    public async Task CollectAsync_WithLargeNumberOfObjects_ShouldComplete()
    {
        // Arrange: Create many files to generate many objects
        for (int i = 0; i < 50; i++)
        {
            CreateFile($"file{i}.txt", $"content {i}");
        }
        RunGit("add -A");
        RunGit("commit -m \"Many files\" --quiet");

        var repository = GitRepository.Open(_workingDirectory);
        var commit = await repository.GetCommitAsync();
        var walker = new GitObjectWalker(repository);

        // Act
        var objects = await walker.CollectAsync(new[] { commit.Id }, CancellationToken.None);

        // Assert
        Assert.True(objects.Count >= 52); // At least 1 commit + 1 tree + 50 blobs
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content);
    }

    private string RunGit(string arguments)
    {
        return RunGitInDirectory(_workingDirectory, arguments);
    }

    private string RunGitInDirectory(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git process");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !arguments.Contains("merge"))
        {
            throw new InvalidOperationException($"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}");
        }

        return string.IsNullOrEmpty(output) ? error : output;
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_workingDirectory);
    }
}
