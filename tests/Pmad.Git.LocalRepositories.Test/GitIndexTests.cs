using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
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
        // Arrange: build a valid index, corrupt the first magic byte, then recompute
        // the trailing SHA-1 checksum so that the magic check (not the checksum check) fires.
        var validData = new GitIndex().ToByteArray();

        // Corrupt 'D' -> 'X'
        validData[0] = (byte)'X';

        // Recompute SHA-1 checksum over everything except the last 20 bytes
        var bodyLength = validData.Length - GitHash.Sha1ByteLength;
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var newChecksum = sha1.ComputeHash(validData, 0, bodyLength);
        newChecksum.CopyTo(validData, bodyLength);

        var ex = Assert.Throws<InvalidDataException>(() => GitIndex.FromBytes(validData));
        Assert.Contains("DIRC", ex.Message);
    }

    [Fact]
    public void ToByteArray_ThrowsWhenEntryHashLengthMismatchesSha256Index()
    {
        // A SHA-1 hash (20 bytes) in an index serialised as SHA-256 (32 bytes) must be rejected.
        var index = new GitIndex();
        var sha1Hash = new GitHash("aabbccddeeff00112233445566778899aabbccdd"); // 20-byte SHA-1
        index.Entries.Add(new GitIndexEntry("file.txt", sha1Hash));

        Assert.Throws<InvalidOperationException>(() => index.ToByteArray(GitHash.Sha256ByteLength));
    }

    [Fact]
    public void ToByteArray_ThrowsWhenEntryHashLengthMismatchesSha1Index()
    {
        // A SHA-256 hash (32 bytes) in an index serialised as SHA-1 (20 bytes) must be rejected.
        var index = new GitIndex();
        var sha256Hash = new GitHash("aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"); // 32-byte SHA-256
        index.Entries.Add(new GitIndexEntry("file.txt", sha256Hash));

        Assert.Throws<InvalidOperationException>(() => index.ToByteArray(GitHash.Sha1ByteLength));
    }

    [Fact]
    public void FromFileInfo_CtimeEqualsToMtime()
    {
        // Git uses mtime as ctime on Windows (no kernel inode-change-time API).
        // Verify that FromFileInfo sets ctime == mtime, not the file birth/creation time.
        var tmpPath = Path.Combine(Path.GetTempPath(), $"pmad_ctime_test_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tmpPath, "ctime test");
            var hash = new GitHash("1111111111111111111111111111111111111111");
            var fi = new FileInfo(tmpPath);

            var entry = GitIndexEntry.FromFileInfo("ctime_test.txt", fi, hash);

            Assert.Equal(entry.MtimeSeconds, entry.CtimeSeconds);
            Assert.Equal(entry.MtimeNanoseconds, entry.CtimeNanoseconds);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void FromFileInfo_RegularFileHasMode100644()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"pmad_mode_test_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tmpPath, "mode test");
            var hash = new GitHash("1111111111111111111111111111111111111111");
            var fi = new FileInfo(tmpPath);

            var entry = GitIndexEntry.FromFileInfo("mode_test.txt", fi, hash);

            Assert.Equal(33188, entry.FileMode); // 100644
        }
        finally
        {
            File.Delete(tmpPath);
        }
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

    [Fact]
    public void FromSpan_IndexVersion3WithExtendedFlags_ParsesCorrectly()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: DIRC, version 3, 2 entries
        writer.Write(new byte[] { (byte)'D', (byte)'I', (byte)'R', (byte)'C' });
        writer.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((uint)3));
        writer.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((uint)2));

        var hash1 = new byte[20];
        hash1[0] = 0xAA;
        var hash2 = new byte[20];
        hash2[0] = 0xBB;

        // Entry 1: with extended flag (flags = 0x4000 | path length 5)
        WriteEntry(ms, hash1, "file1", isExtended: true, extendedFlagsVal: 0x4000);

        // Entry 2: normal entry (flags = path length 5)
        WriteEntry(ms, hash2, "file2", isExtended: false, extendedFlagsVal: 0);

        // Compute checksum
        var content = ms.ToArray();
        var sha1 = System.Security.Cryptography.SHA1.HashData(content);

        var fullData = new byte[content.Length + 20];
        Buffer.BlockCopy(content, 0, fullData, 0, content.Length);
        Buffer.BlockCopy(sha1, 0, fullData, content.Length, 20);

        var index = GitIndex.FromSpan(fullData, 20);

        Assert.Equal(3, index.Version);
        Assert.Equal(2, index.Entries.Count);

        var e1 = index.FindEntry("file1");
        Assert.NotNull(e1);
        Assert.Equal("file1", e1.Path);
        Assert.Equal(GitHash.FromBytes(hash1), e1.Hash);
        Assert.True((e1.Flags & 0x4000) != 0, "CE_EXTENDED should be set in Flags");
        Assert.Equal((ushort)0x4000, e1.ExtendedFlags);

        var e2 = index.FindEntry("file2");
        Assert.NotNull(e2);
        Assert.Equal("file2", e2.Path);
        Assert.Equal(GitHash.FromBytes(hash2), e2.Hash);
        Assert.False((e2.Flags & 0x4000) != 0, "CE_EXTENDED should not be set in Flags");
        Assert.Equal((ushort)0, e2.ExtendedFlags);

        static void WriteEntry(MemoryStream stream, byte[] hash, string path, bool isExtended, ushort extendedFlagsVal)
        {
            var startPos = stream.Position;
            var w = new BinaryWriter(stream);
            // 10 uint32 stats fields (40 bytes)
            for (int i = 0; i < 10; i++)
            {
                w.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness((uint)(i == 6 ? 33188 : 0))); // fileMode at index 6
            }
            // hash (20 bytes)
            w.Write(hash);
            // flags (2 bytes)
            ushort flags = (ushort)(path.Length < 0xFFF ? path.Length : 0xFFF);
            if (isExtended)
            {
                flags |= 0x4000;
            }
            w.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(flags));
            if (isExtended)
            {
                w.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(extendedFlagsVal));
            }
            // path
            var pathBytes = Encoding.UTF8.GetBytes(path);
            w.Write(pathBytes);
            w.Write((byte)0); // NUL terminator
            // Pad with NUL bytes to 8-byte boundary relative to entry start
            var entryLen = stream.Position - startPos;
            var pad = (8 - (entryLen % 8)) % 8;
            if (pad > 0)
            {
                w.Write(new byte[pad]);
            }
        }
    }

    [Fact]
    public void FromSpan_ValidationErrors_ThrowsExpectedExceptions()
    {
        // Too short
        Assert.Throws<InvalidDataException>(() => GitIndex.FromSpan(new byte[10]));

        // Invalid signature
        var badSig = new byte[32];
        Assert.Throws<InvalidDataException>(() => GitIndex.FromSpan(badSig));

        // Unsupported version
        using var ms = new MemoryStream();
        ms.Write(new byte[] { (byte)'D', (byte)'I', (byte)'R', (byte)'C' });
        Span<byte> uintBuf = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(uintBuf, 4); // Version 4
        ms.Write(uintBuf);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(uintBuf, 0); // 0 entries
        ms.Write(uintBuf);
        var raw = ms.ToArray();
        var sha1 = System.Security.Cryptography.SHA1.HashData(raw);
        var buf = new byte[raw.Length + 20];
        Buffer.BlockCopy(raw, 0, buf, 0, raw.Length);
        Buffer.BlockCopy(sha1, 0, buf, raw.Length, 20);

        Assert.Throws<NotSupportedException>(() => GitIndex.FromSpan(buf));

        // Checksum mismatch
        buf[^1] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => GitIndex.FromSpan(buf));
    }

    [Fact]
    public void ToByteArray_RoundTrip_PreservesExtendedFlagsAndV3Format()
    {
        var original = new GitIndex { Version = 3 };
        var hash1 = new GitHash("1111111111111111111111111111111111111111");
        var hash2 = new GitHash("2222222222222222222222222222222222222222");

        // Entry 1: extended flags (skip-worktree / intent-to-add etc)
        var entry1 = new GitIndexEntry(
            "extended.txt",
            hash1,
            fileMode: 33188,
            fileSize: 123,
            mtimeSeconds: 1000,
            mtimeNanoseconds: 200,
            ctimeSeconds: 1000,
            ctimeNanoseconds: 200,
            dev: 1,
            ino: 2,
            uid: 3,
            gid: 4,
            flags: 0x4000,
            extendedFlags: 0x4000);

        // Entry 2: standard entry without extended flags
        var entry2 = new GitIndexEntry(
            "standard.txt",
            hash2,
            fileMode: 33188,
            fileSize: 456,
            mtimeSeconds: 2000,
            mtimeNanoseconds: 400,
            ctimeSeconds: 2000,
            ctimeNanoseconds: 400,
            dev: 5,
            ino: 6,
            uid: 7,
            gid: 8,
            flags: 0,
            extendedFlags: 0);

        original.AddOrUpdate(entry1);
        original.AddOrUpdate(entry2);

        // Serialize to bytes
        var serializedBytes = original.ToByteArray(GitHash.Sha1ByteLength);

        // Parse back
        var roundTripped = GitIndex.FromSpan(serializedBytes, GitHash.Sha1ByteLength);

        Assert.Equal(3, roundTripped.Version);
        Assert.Equal(2, roundTripped.Entries.Count);

        var rt1 = roundTripped.FindEntry("extended.txt");
        Assert.NotNull(rt1);
        Assert.Equal(hash1, rt1.Hash);
        Assert.Equal(33188, rt1.FileMode);
        Assert.Equal((uint)123, rt1.FileSize);
        Assert.True((rt1.Flags & 0x4000) != 0, "CE_EXTENDED flag should be preserved in Flags");
        Assert.Equal((ushort)0x4000, rt1.ExtendedFlags);

        var rt2 = roundTripped.FindEntry("standard.txt");
        Assert.NotNull(rt2);
        Assert.Equal(hash2, rt2.Hash);
        Assert.Equal(33188, rt2.FileMode);
        Assert.Equal((uint)456, rt2.FileSize);
        Assert.False((rt2.Flags & 0x4000) != 0, "CE_EXTENDED flag should not be set for standard entry");
        Assert.Equal((ushort)0, rt2.ExtendedFlags);
    }

    [Fact]
    public void FromSpan_MalformedEntryUnterminatedPath_WithZeroByteInChecksum_ThrowsInvalidDataException()
    {
        var index = new GitIndex();
        index.AddOrUpdate(new GitIndexEntry("test.txt", new GitHash("1111111111111111111111111111111111111111")));
        var validBytes = index.ToByteArray(GitHash.Sha1ByteLength);

        // Find the NUL terminator of "test.txt" in the validBytes
        var pathBytes = Encoding.UTF8.GetBytes("test.txt");
        var pathIndex = validBytes.AsSpan().IndexOf(pathBytes);
        Assert.True(pathIndex >= 0);
        var nulIndex = pathIndex + pathBytes.Length;
        Assert.Equal(0, validBytes[nulIndex]);

        // Overwrite the path terminator and all padding with non-zero bytes up to the checksum
        var corruptBytes = (byte[])validBytes.Clone();
        var bodyLength = corruptBytes.Length - GitHash.Sha1ByteLength;
        corruptBytes.AsSpan(pathIndex, bodyLength - pathIndex).Fill((byte)'X');
        using var sha1 = SHA1.Create();
        for (uint tweak = 0; tweak < 1000; tweak++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(corruptBytes.AsSpan(16, 4), tweak); // tweak dev field
            var hash = sha1.ComputeHash(corruptBytes, 0, bodyLength);
            if (Array.IndexOf(hash, (byte)0) >= 0)
            {
                hash.CopyTo(corruptBytes, bodyLength);
                break;
            }
        }

        // Verify checksum actually contains 0x00
        Assert.Contains((byte)0, corruptBytes.AsSpan(bodyLength).ToArray());

        var ex = Assert.Throws<InvalidDataException>(() => GitIndex.FromSpan(corruptBytes, GitHash.Sha1ByteLength));
        Assert.Contains("null-terminated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
