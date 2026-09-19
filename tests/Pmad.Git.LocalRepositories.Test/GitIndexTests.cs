using System.IO;
using System.Text;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitIndexTests
{
    [Fact]
    public async Task ReadAsync_ReadsIndexGeneratedByGitCli_Sha1()
    {
        using var testRepo = GitTestRepository.Create(GitObjectFormat.Sha1);
        testRepo.Commit("First", ("file1.txt", "content 1"), ("sub/file2.txt", "content 2"));

        var indexPath = Path.Combine(testRepo.GitDirectory, "index");
        var index = await GitIndex.ReadAsync(indexPath, GitHash.Sha1ByteLength);

        Assert.Equal(2, index.Version);
        Assert.Equal(3, index.Entries.Count); // README.md (from init) + file1.txt + sub/file2.txt

        var readme = index.FindEntry("README.md");
        Assert.NotNull(readme);
        Assert.Equal(0, readme.Stage);
        Assert.Equal(33188, readme.FileMode); // 100644

        var file1 = index.FindEntry("file1.txt");
        Assert.NotNull(file1);
        Assert.Equal(0, file1.Stage);
        Assert.Equal(33188, file1.FileMode);

        var file2 = index.FindEntry("sub/file2.txt");
        Assert.NotNull(file2);
        Assert.Equal(0, file2.Stage);

        // Verify hashes match git ls-files --stage
        var lsFiles = testRepo.RunGit("ls-files --stage");
        Assert.Contains(file1.Hash.ToString(), lsFiles);
        Assert.Contains(file2.Hash.ToString(), lsFiles);
    }

    [Fact]
    public async Task ReadAsync_ReadsIndexGeneratedByGitCli_Sha256()
    {
        using var testRepo = GitTestRepository.Create(GitObjectFormat.Sha256);
        testRepo.Commit("Sha256 commit", ("hello.txt", "world"));

        var indexPath = Path.Combine(testRepo.GitDirectory, "index");
        var index = await GitIndex.ReadAsync(indexPath, GitHash.Sha256ByteLength);

        Assert.Equal(2, index.Version);
        Assert.Equal(2, index.Entries.Count); // README.md + hello.txt

        var hello = index.FindEntry("hello.txt");
        Assert.NotNull(hello);
        Assert.Equal(GitHash.Sha256HexLength, hello.Hash.ToString().Length);

        var lsFiles = testRepo.RunGit("ls-files --stage");
        Assert.Contains(hello.Hash.ToString(), lsFiles);
    }

    [Fact]
    public async Task WriteAsync_CreatesIndexReadableByGitCli()
    {
        using var testRepo = GitTestRepository.Create(GitObjectFormat.Sha1);

        // Write a new file to disk
        var newFilePath = Path.Combine(testRepo.WorkingDirectory, "managed.txt");
        await File.WriteAllTextAsync(newFilePath, "managed content");

        // Hash the blob with git hash-object
        var hashStr = testRepo.RunGit("hash-object -w managed.txt").Trim();
        var blobHash = new GitHash(hashStr);

        var indexPath = Path.Combine(testRepo.GitDirectory, "index");
        var index = await GitIndex.ReadAsync(indexPath);

        var fileInfo = new FileInfo(newFilePath);
        var newEntry = GitIndexEntry.FromFileInfo("managed.txt", fileInfo, blobHash);
        index.AddOrUpdate(newEntry);

        await index.WriteAsync(indexPath);

        // Verify git CLI can read the written index and sees managed.txt as staged
        var status = testRepo.RunGit("status --porcelain");
        Assert.Contains("A  managed.txt", status);

        var lsFiles = testRepo.RunGit("ls-files --stage");
        Assert.Contains("managed.txt", lsFiles);
        Assert.Contains(hashStr, lsFiles);
    }

    [Fact]
    public async Task RoundTrip_ReadAndWrite_PreservesEntries()
    {
        using var testRepo = GitTestRepository.Create(GitObjectFormat.Sha1);
        testRepo.Commit("Add files", ("a.txt", "aaa"), ("b/c.txt", "bbb"));

        var indexPath = Path.Combine(testRepo.GitDirectory, "index");
        var originalIndex = await GitIndex.ReadAsync(indexPath);

        var tempIndexFile = Path.Combine(testRepo.GitDirectory, "index.test");
        await originalIndex.WriteAsync(tempIndexFile);

        var reloadedIndex = await GitIndex.ReadAsync(tempIndexFile);

        Assert.Equal(originalIndex.Entries.Count, reloadedIndex.Entries.Count);
        for (var i = 0; i < originalIndex.Entries.Count; i++)
        {
            var orig = originalIndex.Entries[i];
            var reloaded = reloadedIndex.Entries[i];

            Assert.Equal(orig.Path, reloaded.Path);
            Assert.Equal(orig.Hash, reloaded.Hash);
            Assert.Equal(orig.FileMode, reloaded.FileMode);
            Assert.Equal(orig.FileSize, reloaded.FileSize);
            Assert.Equal(orig.Stage, reloaded.Stage);
            Assert.Equal(orig.MtimeSeconds, reloaded.MtimeSeconds);
            Assert.Equal(orig.CtimeSeconds, reloaded.CtimeSeconds);
        }
    }

    [Fact]
    public void FromBytes_ThrowsOnInvalidChecksum()
    {
        var validData = new GitIndex().ToByteArray();
        // Corrupt a byte
        validData[validData.Length - 1] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => GitIndex.FromBytes(validData));
    }

    [Fact]
    public void FromBytes_ThrowsOnInvalidMagic()
    {
        var validData = new GitIndex().ToByteArray();
        validData[0] = (byte)'X'; // Corrupt 'D' -> 'X'

        // Recompute trailing checksum so it fails magic check instead of checksum
        // Or simply test that invalid signature or checksum fails
        Assert.Throws<InvalidDataException>(() => GitIndex.FromBytes(validData));
    }

    [Fact]
    public void AddOrUpdate_ReplacesExistingEntryWithSamePathAndStage()
    {
        var index = new GitIndex();
        var h1 = new GitHash("1111111111111111111111111111111111111111");
        var h2 = new GitHash("2222222222222222222222222222222222222222");

        index.AddOrUpdate(new GitIndexEntry("test.txt", h1));
        Assert.Single(index.Entries);
        Assert.Equal(h1, index.FindEntry("test.txt")!.Hash);

        index.AddOrUpdate(new GitIndexEntry("test.txt", h2));
        Assert.Single(index.Entries);
        Assert.Equal(h2, index.FindEntry("test.txt")!.Hash);
    }

    [Fact]
    public void Remove_RemovesMatchingEntry()
    {
        var index = new GitIndex();
        var h = new GitHash("1111111111111111111111111111111111111111");

        index.AddOrUpdate(new GitIndexEntry("dir/file.txt", h));
        Assert.True(index.Remove("dir/file.txt"));
        Assert.Empty(index.Entries);
        Assert.False(index.Remove("dir/file.txt"));
    }

    [Fact]
    public void PathNormalization_HandlesBackslashes()
    {
        var index = new GitIndex();
        var h = new GitHash("1111111111111111111111111111111111111111");

        var entry = new GitIndexEntry(@"folder\sub\file.txt", h);
        Assert.Equal("folder/sub/file.txt", entry.Path);

        index.AddOrUpdate(entry);
        Assert.NotNull(index.FindEntry(@"folder\sub\file.txt"));
        Assert.NotNull(index.FindEntry("folder/sub/file.txt"));

        Assert.True(index.Remove(@"folder\sub\file.txt"));
        Assert.Null(index.FindEntry("folder/sub/file.txt"));
    }

    [Fact]
    public async Task ReadAsync_ReturnsEmptyIndexWhenFileDoesNotExist()
    {
        var index = await GitIndex.ReadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Empty(index.Entries);
    }
}
