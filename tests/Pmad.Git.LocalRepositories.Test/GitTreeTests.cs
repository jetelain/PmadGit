using System;
using System.IO;
using System.Linq;
using System.Text;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitTreeTests
{
    [Fact]
    public void Parse_ReturnsEntriesWithCorrectKinds()
    {
        var treeId = new GitHash("1111111111111111111111111111111111111111");
        var blobHash = GitHash.FromBytes(CreateSequentialBytes(0));
        var subtreeHash = GitHash.FromBytes(CreateSequentialBytes(1));
        var symlinkHash = GitHash.FromBytes(CreateSequentialBytes(2));

        var content = Combine(
            CreateTreeEntry("100644", "README.md", blobHash),
            CreateTreeEntry("40000", "src", subtreeHash),
            CreateTreeEntry("120000", "link", symlinkHash));

        var tree = GitTree.Parse(treeId, content);

        Assert.Equal(3, tree.Entries.Count);
        Assert.Equal("README.md", tree.Entries[0].Name);
        Assert.Equal(GitTreeEntryKind.Blob, tree.Entries[0].Kind);
		Assert.Equal(ParseOctal("100644"), tree.Entries[0].Mode);
        Assert.Equal(blobHash, tree.Entries[0].Hash);

        Assert.Equal("src", tree.Entries[1].Name);
        Assert.Equal(GitTreeEntryKind.Tree, tree.Entries[1].Kind);
		Assert.Equal(ParseOctal("40000"), tree.Entries[1].Mode);
        Assert.Equal(subtreeHash, tree.Entries[1].Hash);

        Assert.Equal("link", tree.Entries[2].Name);
        Assert.Equal(GitTreeEntryKind.Symlink, tree.Entries[2].Kind);
		Assert.Equal(ParseOctal("120000"), tree.Entries[2].Mode);
        Assert.Equal(symlinkHash, tree.Entries[2].Hash);
    }

    [Fact]
    public void Parse_WithTruncatedEntry_Throws()
    {
        var treeId = new GitHash("2222222222222222222222222222222222222222");
        var entry = CreateTreeEntry("100644", "README.md", GitHash.FromBytes(CreateSequentialBytes(3)));
        Array.Resize(ref entry, entry.Length - 5);

        Assert.Throws<InvalidOperationException>(() => GitTree.Parse(treeId, entry));
    }

	[Fact]
	public void Parse_InvalidHashLength_Throws()
	{
		var treeId = new GitHash("3333333333333333333333333333333333333333");
		Assert.Throws<ArgumentOutOfRangeException>(() => GitTree.Parse(treeId, Array.Empty<byte>(), 10));
	}

	[Fact]
	public void Parse_NonOctalMode_Throws()
	{
		var treeId = new GitHash("4444444444444444444444444444444444444444");
		var entry = CreateTreeEntry("100648", "invalid", GitHash.FromBytes(CreateSequentialBytes(4)));
		Assert.Throws<InvalidOperationException>(() => GitTree.Parse(treeId, entry));
	}

	[Fact]
	public void Parse_MissingNameTerminator_Throws()
	{
		var treeId = new GitHash("5555555555555555555555555555555555555555");
		var entry = CreateEntryWithoutNameTerminator("100644", "file.txt", GitHash.FromBytes(CreateSequentialBytes(5)));
		Assert.Throws<InvalidOperationException>(() => GitTree.Parse(treeId, entry));
	}

	[Fact]
	public void Parse_RecognizesSubmoduleKind()
	{
		var treeId = new GitHash("6666666666666666666666666666666666666666");
		var entry = CreateTreeEntry("160000", "submodule", GitHash.FromBytes(CreateSequentialBytes(6)));
		var tree = GitTree.Parse(treeId, entry);
		Assert.Single(tree.Entries);
		Assert.Equal(GitTreeEntryKind.Submodule, tree.Entries[0].Kind);
	}

	[Fact]
	public void Parse_SupportsSha256Entries()
	{
		var treeId = new GitHash("7777777777777777777777777777777777777777");
		var sha256Hash = GitHash.FromBytes(CreateSequentialBytes(7, GitHash.Sha256ByteLength));
		var content = CreateTreeEntry("100644", "payload.bin", sha256Hash);
		var tree = GitTree.Parse(treeId, content, GitHash.Sha256ByteLength);
		Assert.Equal(sha256Hash, tree.Entries[0].Hash);
	}

    private static byte[] CreateTreeEntry(string mode, string name, GitHash hash)
    {
        using var buffer = new MemoryStream();
        buffer.Write(Encoding.ASCII.GetBytes(mode));
        buffer.WriteByte((byte)' ');
        buffer.Write(Encoding.UTF8.GetBytes(name));
        buffer.WriteByte(0);
        buffer.Write(hash.ToByteArray());
        return buffer.ToArray();
    }

    private static byte[] Combine(params byte[][] entries)
    {
        var totalLength = entries.Sum(entry => entry.Length);
        var result = new byte[totalLength];
        var offset = 0;
        foreach (var entry in entries)
        {
            Buffer.BlockCopy(entry, 0, result, offset, entry.Length);
            offset += entry.Length;
        }

        return result;
    }

	private static byte[] CreateSequentialBytes(int seed, int length = GitHash.Sha1ByteLength)
	{
		var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((i + seed) % 256);
        }

        return bytes;
    }

	private static byte[] CreateEntryWithoutNameTerminator(string mode, string name, GitHash hash)
	{
		using var buffer = new MemoryStream();
		buffer.Write(Encoding.ASCII.GetBytes(mode));
		buffer.WriteByte((byte)' ');
		buffer.Write(Encoding.UTF8.GetBytes(name));
		buffer.Write(hash.ToByteArray());
		return buffer.ToArray();
	}

	private static int ParseOctal(string value) => Convert.ToInt32(value, 8);
}
