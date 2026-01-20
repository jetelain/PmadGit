using Pmad.Git.HttpServer.Pack;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Pack;

public sealed class GitDeltaApplierTest
{
    [Fact]
    public void Apply_WithSimpleCopy_ShouldCopyFromBase()
    {
        // Arrange: Base object is "Hello World"
        var baseContent = "Hello World"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Delta: Copy 5 bytes from offset 0 (copies "Hello")
        var delta = new byte[]
        {
            11,    // base size (11 bytes)
            5,     // result size (5 bytes)
            0x91,  // copy opcode: bit 0 set (offset byte), bit 4 set (size byte)
            0x00,  // offset = 0
            0x05   // copy size = 5
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal(GitObjectType.Blob, result.Type);
        Assert.Equal("Hello"u8.ToArray(), result.Content);
    }

    [Fact]
    public void Apply_WithSimpleInsert_ShouldInsertNewData()
    {
        // Arrange
        var baseContent = "Base"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Delta: Insert "Test"
        var delta = new byte[]
        {
            4,           // base size (4 bytes)
            4,           // result size (4 bytes)
            4,           // insert opcode: insert 4 bytes
            (byte)'T', (byte)'e', (byte)'s', (byte)'t'
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal("Test"u8.ToArray(), result.Content);
    }

    [Fact]
    public void Apply_WithCopyAndInsert_ShouldCombineOperations()
    {
        // Arrange: Base is "Hello"
        var baseContent = "Hello"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Delta: Copy "Hello" then insert " World"
        var delta = new byte[]
        {
            5,           // base size (5 bytes)
            11,          // result size (11 bytes)
            0x91,        // copy opcode: bit 0 (offset), bit 4 (size)
            0x00,        // offset = 0
            0x05,        // copy 5 bytes
            6,           // insert opcode: insert 6 bytes
            (byte)' ', (byte)'W', (byte)'o', (byte)'r', (byte)'l', (byte)'d'
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal("Hello World"u8.ToArray(), result.Content);
    }

    [Fact]
    public void Apply_WithMultipleCopyOperations_ShouldHandleCorrectly()
    {
        // Arrange: Base is "ABCDEFGH"
        var baseContent = "ABCDEFGH"u8.ToArray();
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Delta: Copy "ABC" (offset 0, size 3) + Copy "FGH" (offset 5, size 3)
        var delta = new byte[]
        {
            8,           // base size (8 bytes)
            6,           // result size (6 bytes)
            0x91,        // copy opcode: bit 0 (offset), bit 4 (size)
            0x00,        // offset = 0
            0x03,        // size = 3
            0x91,        // copy opcode: bit 0 (offset), bit 4 (size)
            0x05,        // offset = 5
            0x03         // size = 3
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal("ABCFGH"u8.ToArray(), result.Content);
    }

    [Fact]
    public void Apply_WithLargeCopySize_ShouldUseDefault64KB()
    {
        // Arrange: Large base content (needs to be at least 65536 bytes)
        var baseContent = new byte[70000];
        for (int i = 0; i < baseContent.Length; i++)
        {
            baseContent[i] = (byte)(i % 256);
        }
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Variable length encoding (verified with test program):
        // 70000: F0-A2-04
        // 65536: 80-80-04
        
        var delta = new byte[]
        {
            0xF0, 0xA2, 0x04,  // base size: 70000
            0x80, 0x80, 0x04,  // result size: 65536
            0x80               // copy opcode: no offset/size bits, defaults to offset=0, size=0x10000
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal(65536, result.Content.Length);
        Assert.Equal(baseContent[..65536], result.Content);
    }

    [Fact]
    public void Apply_WithVariableLengthSizes_ShouldParseCorrectly()
    {
        // Arrange
        var baseContent = new byte[200];
        Array.Fill(baseContent, (byte)'A');
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Delta with variable length encoding for sizes
        var delta = new byte[]
        {
            0xC8, 0x01,  // base size = 200 (variable length: 0xC8 = 200 & 0x7F = 72, continue bit set)
            0x0A,        // result size = 10 (simple)
            0x91,        // copy opcode: bit 0 (offset), bit 4 (size)
            0x00,        // offset = 0
            0x0A         // copy 10 bytes
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal(10, result.Content.Length);
        Assert.All(result.Content, b => Assert.Equal((byte)'A', b));
    }

    [Fact]
    public void Apply_WithComplexCopyOffsets_ShouldHandleMultiByteOffsets()
    {
        // Arrange: Base with 1000 bytes
        var baseContent = new byte[1000];
        for (int i = 0; i < baseContent.Length; i++)
        {
            baseContent[i] = (byte)(i % 256);
        }
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Delta: Copy 10 bytes from offset 500 (0x01F4)
        var delta = new byte[]
        {
            0xE8, 0x07,  // base size = 1000 (variable length)
            0x0A,        // result size = 10
            0x93,        // copy opcode: bit 0 and bit 1 (two offset bytes), bit 4 (size byte)
            0xF4,        // offset low byte = 0xF4
            0x01,        // offset high byte = 0x01 (total offset = 0x01F4 = 500)
            0x0A         // size = 10
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal(10, result.Content.Length);
        Assert.Equal(baseContent[500..510], result.Content);
    }

    [Fact]
    public void Apply_WithZeroOpcode_ShouldThrowInvalidDataException()
    {
        // Arrange
        var baseObject = new GitObjectData(GitObjectType.Blob, "Base"u8.ToArray());

        // Delta with zero opcode (invalid)
        var delta = new byte[]
        {
            4,     // base size
            4,     // result size
            0x00   // invalid opcode
        };

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => 
            GitDeltaApplier.Apply(baseObject, delta));
    }

    [Fact]
    public void Apply_WithBaseSizeMismatch_ShouldThrowInvalidDataException()
    {
        // Arrange
        var baseObject = new GitObjectData(GitObjectType.Blob, "Hello"u8.ToArray()); // 5 bytes

        // Delta claims base is 10 bytes
        var delta = new byte[]
        {
            10,    // base size = 10 (WRONG!)
            5,     // result size
            0x90,  // copy opcode
            0x05   // copy 5 bytes
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(() => 
            GitDeltaApplier.Apply(baseObject, delta));
        Assert.Contains("base size mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WithCopyBeyondBaseSize_ShouldThrowInvalidDataException()
    {
        // Arrange
        var baseObject = new GitObjectData(GitObjectType.Blob, "Small"u8.ToArray()); // 5 bytes

        // Delta tries to copy from offset 10 (beyond base size)
        var delta = new byte[]
        {
            5,           // base size
            5,           // result size
            0x91,        // copy opcode: bit 0 (offset), bit 4 (size)
            0x0A,        // offset = 10 (beyond base!)
            0x05         // size = 5
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(() => 
            GitDeltaApplier.Apply(baseObject, delta));
        Assert.Contains("exceeds base size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WithInsertBeyondDeltaSize_ShouldThrowInvalidDataException()
    {
        // Arrange
        var baseObject = new GitObjectData(GitObjectType.Blob, "Base"u8.ToArray());

        // Delta tries to insert more bytes than available
        var delta = new byte[]
        {
            4,     // base size
            10,    // result size
            10,    // insert 10 bytes (but delta doesn't have them!)
            (byte)'A', (byte)'B'  // only 2 bytes available
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(() => 
            GitDeltaApplier.Apply(baseObject, delta));
        Assert.Contains("exceeds payload", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WithIncorrectResultLength_ShouldThrowInvalidDataException()
    {
        // Arrange
        var baseObject = new GitObjectData(GitObjectType.Blob, "Base"u8.ToArray());

        // Delta claims result will be 10 bytes but only produces 4
        var delta = new byte[]
        {
            4,           // base size
            10,          // result size = 10 (WRONG!)
            4,           // insert 4 bytes
            (byte)'T', (byte)'e', (byte)'s', (byte)'t'
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(() => 
            GitDeltaApplier.Apply(baseObject, delta));
        Assert.Contains("incorrect length", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_PreservesObjectType()
    {
        // Arrange
        var baseObject = new GitObjectData(GitObjectType.Commit, "commit content"u8.ToArray());

        // Delta: simple copy
        var delta = new byte[]
        {
            14,    // base size
            14,    // result size
            0x91,  // copy opcode: bit 0 (offset), bit 4 (size)
            0x00,  // offset = 0
            0x0E   // copy 14 bytes
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal(GitObjectType.Commit, result.Type);
    }

    [Fact]
    public void Apply_WithRealWorldScenario_ModifiedFile()
    {
        // Arrange: Simulate a file modification
        var originalContent = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5\n"u8.ToArray(); // 35 bytes
        var baseObject = new GitObjectData(GitObjectType.Blob, originalContent);

        // Delta: Keep Line 1, insert "Modified Line 2\n", keep lines 3-5
        var modifiedLine2 = "Modified Line 2\n"u8; // 16 bytes
        var delta = new List<byte>();
        
        // Base size: 35 bytes
        delta.Add(35);
        
        // Result size: 7 (Line 1\n) + 16 (Modified Line 2\n) + 21 (Line 3-5) = 44 bytes
        delta.Add(44);
        
        // Copy "Line 1\n" (7 bytes from offset 0)
        delta.Add(0x91);  // copy opcode: bit 0 (offset), bit 4 (size)
        delta.Add(0x00);  // offset = 0
        delta.Add(0x07);  // size = 7
        
        // Insert "Modified Line 2\n" (16 bytes)
        delta.Add(16);    // insert 16 bytes
        delta.AddRange(modifiedLine2.ToArray());
        
        // Copy "Line 3\nLine 4\nLine 5\n" (21 bytes from offset 14)
        delta.Add(0x91);  // copy opcode: bit 0 (offset), bit 4 (size)
        delta.Add(0x0E);  // offset = 14
        delta.Add(0x15);  // size = 21

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta.ToArray());

        // Assert
        Assert.Equal(44, result.Content.Length);
        var resultText = System.Text.Encoding.UTF8.GetString(result.Content);
        Assert.Contains("Line 1", resultText);
        Assert.Contains("Modified Line 2", resultText);
        Assert.Contains("Line 3", resultText);
        Assert.Contains("Line 4", resultText);
        Assert.Contains("Line 5", resultText);
        
        // Verify the exact sequence
        Assert.Equal("Line 1\nModified Line 2\nLine 3\nLine 4\nLine 5\n", resultText);
    }

    [Fact]
    public void Apply_WithEmptyResult_ShouldProduceEmptyContent()
    {
        // Arrange
        var baseObject = new GitObjectData(GitObjectType.Blob, "Base"u8.ToArray());

        // Delta: result size is 0
        var delta = new byte[]
        {
            4,     // base size
            0      // result size = 0
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Empty(result.Content);
    }

    [Fact]
    public void Apply_WithMaximumVariableLengthEncoding_ShouldHandle()
    {
        // Arrange: Test with maximum variable length values
        var baseSize = (1 << 21) - 1; // Maximum 3-byte variable length
        var baseContent = new byte[baseSize];
        var baseObject = new GitObjectData(GitObjectType.Blob, baseContent);

        // Delta with maximum variable length encoding
        var delta = new byte[]
        {
            0xFF, 0xFF, 0x7F,  // base size (maximum 3-byte encoding)
            0x0A,              // result size = 10
            0x91,              // copy opcode: bit 0 (offset), bit 4 (size)
            0x00,              // offset = 0
            0x0A               // copy 10 bytes
        };

        // Act
        var result = GitDeltaApplier.Apply(baseObject, delta);

        // Assert
        Assert.Equal(10, result.Content.Length);
    }
}
