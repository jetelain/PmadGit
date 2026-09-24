using System.Buffers.Binary;
using Pmad.Git.LocalRepositories.Pack;

namespace Pmad.Git.LocalRepositories.Test.Pack;

public sealed class GitPackIndexTest
{
    [Fact]
    public async Task LoadAsync_PackIndexV2_WithLarge64BitOffsets_NonSequentialLookup_Succeeds()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var hash1Bytes = new byte[20];
            hash1Bytes[0] = 0x01;
            hash1Bytes[19] = 0x11;
            var hash1 = GitHash.FromBytes(hash1Bytes);

            var hash2Bytes = new byte[20];
            hash2Bytes[0] = 0x02;
            hash2Bytes[19] = 0x22;
            var hash2 = GitHash.FromBytes(hash2Bytes);

            const long largeOffset0 = 0x1_0000_0000L; // 4 GB (> 2 GB)
            const long largeOffset1 = 0x2_8000_0000L; // 10 GB (> 2 GB)

            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                // Magic: \xFFtOc
                fs.Write(new byte[] { 0xFF, (byte)'t', (byte)'O', (byte)'c' });
                // Version 2
                var versionBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(versionBytes, 2);
                fs.Write(versionBytes);

                // Fanout: 256 uints
                var fanoutEntry = new byte[4];
                for (var i = 0; i < 256; i++)
                {
                    uint count = i switch
                    {
                        0 => 0,
                        1 => 1,
                        _ => 2
                    };
                    BinaryPrimitives.WriteUInt32BigEndian(fanoutEntry, count);
                    fs.Write(fanoutEntry);
                }

                // Hashes: 2 * 20 bytes
                fs.Write(hash1Bytes);
                fs.Write(hash2Bytes);

                // CRC32s: 2 * 4 bytes
                fs.Write(new byte[8]);

                // 4-byte offsets:
                // Entry 0 points to large index 1 (0x80000001)
                // Entry 1 points to large index 0 (0x80000000)
                var offsetEntry = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(offsetEntry, 0x8000_0001u);
                fs.Write(offsetEntry);

                BinaryPrimitives.WriteUInt32BigEndian(offsetEntry, 0x8000_0000u);
                fs.Write(offsetEntry);

                // Large offset table: 2 * 8 bytes
                // Index 0: largeOffset0 (4 GB)
                // Index 1: largeOffset1 (10 GB)
                var largeEntry = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(largeEntry, largeOffset0);
                fs.Write(largeEntry);

                BinaryPrimitives.WriteInt64BigEndian(largeEntry, largeOffset1);
                fs.Write(largeEntry);

                // Trailer: 2 * 20 bytes checksums
                fs.Write(new byte[40]);
            }

            var packIndex = await GitPackIndex.LoadAsync(tempFile, 20, CancellationToken.None);

            Assert.True(packIndex.TryGetOffset(hash1, out var offset1));
            Assert.Equal(largeOffset1, offset1); // Entry 0 mapped to large index 1

            Assert.True(packIndex.TryGetOffset(hash2, out var offset2));
            Assert.Equal(largeOffset0, offset2); // Entry 1 mapped to large index 0
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_PackIndexV2_LargeIndexOutOfBounds_ThrowsInvalidDataException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var hash1Bytes = new byte[20];
            hash1Bytes[0] = 0x01;

            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[] { 0xFF, (byte)'t', (byte)'O', (byte)'c' });
                var b4 = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(b4, 2);
                fs.Write(b4);

                for (var i = 0; i < 256; i++)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(b4, 1);
                    fs.Write(b4);
                }

                fs.Write(hash1Bytes);
                fs.Write(new byte[4]); // CRC32

                // Points to large index 5, but table only has 1 entry
                BinaryPrimitives.WriteUInt32BigEndian(b4, 0x8000_0005u);
                fs.Write(b4);

                var b8 = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(b8, 0x1_0000_0000L);
                fs.Write(b8);

                fs.Write(new byte[40]); // Trailer
            }

            var ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => GitPackIndex.LoadAsync(tempFile, 20, CancellationToken.None));
            Assert.Contains("out of bounds", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_PackIndexV2_Standard32BitOffsets_Succeeds()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var hash1Bytes = new byte[20];
            hash1Bytes[0] = 0x0A;
            var hash1 = GitHash.FromBytes(hash1Bytes);

            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[] { 0xFF, (byte)'t', (byte)'O', (byte)'c' });
                var b4 = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(b4, 2);
                fs.Write(b4);

                for (var i = 0; i < 256; i++)
                {
                    uint count = i < 0x0A ? 0u : 1u;
                    BinaryPrimitives.WriteUInt32BigEndian(b4, count);
                    fs.Write(b4);
                }

                fs.Write(hash1Bytes);
                fs.Write(new byte[4]); // CRC32

                // Regular 32-bit offset (12345)
                BinaryPrimitives.WriteUInt32BigEndian(b4, 12345u);
                fs.Write(b4);

                fs.Write(new byte[40]); // Trailer
            }

            var packIndex = await GitPackIndex.LoadAsync(tempFile, 20, CancellationToken.None);

            Assert.True(packIndex.TryGetOffset(hash1, out var offset));
            Assert.Equal(12345L, offset);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
