using System.Text;
using Pmad.Git.HttpServer.Protocol;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Protocol;

public sealed class UploadPackProtocolTest
{
    [Fact]
    public async Task ParseUploadPackRequest_WithSimpleWant_ShouldParseCorrectly()
    {
        // Simulate what git sends during upload-pack
        var stream = new MemoryStream();
        
        // want <hash> <capabilities>
        var hash = "1234567890abcdef1234567890abcdef12345678";
        await PktLineWriter.WriteStringAsync(stream, $"want {hash} multi_ack_detailed side-band-64k thin-pack ofs-delta\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);
        await PktLineWriter.WriteStringAsync(stream, "done\n", CancellationToken.None);
        
        stream.Position = 0;

        // Simulate what ParseUploadPackRequestAsync does
        var reader = new PktLineReader(stream);
        var wants = new List<GitHash>();
        var readingWants = true;

        while (true)
        {
            var packet = await reader.ReadAsync(CancellationToken.None);
            if (packet is null)
            {
                break;
            }

            if (packet.Value.IsFlush)
            {
                if (readingWants)
                {
                    readingWants = false;
                    continue;
                }
                break;
            }

            var text = packet.Value.AsString().TrimEnd('\n', '\r');
            
            if (readingWants)
            {
                if (!text.StartsWith("want ", StringComparison.Ordinal))
                {
                    continue;
                }

                var hashPart = text[5..];
                var capsIndex = hashPart.IndexOf('\0');
                if (capsIndex >= 0)
                {
                    hashPart = hashPart[..capsIndex];
                }
                else
                {
                    // Space-separated capabilities
                    var spaceIndex = hashPart.IndexOf(' ');
                    if (spaceIndex >= 0)
                    {
                        hashPart = hashPart[..spaceIndex];
                    }
                }

                if (hashPart.Length == 40 && GitHash.TryParse(hashPart, out var parsedHash))
                {
                    wants.Add(parsedHash);
                }
            }
            else if (text.Equals("done", StringComparison.Ordinal))
            {
                break;
            }
        }

        Assert.Single(wants);
        Assert.Equal(hash, wants[0].Value);
    }

    [Fact]
    public async Task ParseUploadPackRequest_WithMultipleWants_ShouldParseAll()
    {
        var stream = new MemoryStream();
        
        var hash1 = "1111111111111111111111111111111111111111";
        var hash2 = "2222222222222222222222222222222222222222";
        var hash3 = "3333333333333333333333333333333333333333";
        
        await PktLineWriter.WriteStringAsync(stream, $"want {hash1} multi_ack\n", CancellationToken.None);
        await PktLineWriter.WriteStringAsync(stream, $"want {hash2}\n", CancellationToken.None);
        await PktLineWriter.WriteStringAsync(stream, $"want {hash3}\n", CancellationToken.None);
        await PktLineWriter.WriteFlushAsync(stream, CancellationToken.None);
        await PktLineWriter.WriteStringAsync(stream, "done\n", CancellationToken.None);
        
        stream.Position = 0;

        var reader = new PktLineReader(stream);
        var wants = new List<GitHash>();
        var readingWants = true;

        while (true)
        {
            var packet = await reader.ReadAsync(CancellationToken.None);
            if (packet is null)
            {
                break;
            }

            if (packet.Value.IsFlush)
            {
                if (readingWants)
                {
                    readingWants = false;
                    continue;
                }
                break;
            }

            var text = packet.Value.AsString().TrimEnd('\n', '\r');
            
            if (readingWants && text.StartsWith("want ", StringComparison.Ordinal))
            {
                var hashPart = text[5..];
                var capsIndex = hashPart.IndexOf('\0');
                if (capsIndex >= 0)
                {
                    hashPart = hashPart[..capsIndex];
                }
                else
                {
                    var spaceIndex = hashPart.IndexOf(' ');
                    if (spaceIndex >= 0)
                    {
                        hashPart = hashPart[..spaceIndex];
                    }
                }

                if (hashPart.Length == 40 && GitHash.TryParse(hashPart, out var parsedHash))
                {
                    wants.Add(parsedHash);
                }
            }
            else if (text.Equals("done", StringComparison.Ordinal))
            {
                break;
            }
        }

        Assert.Equal(3, wants.Count);
        Assert.Contains(wants, w => w.Value == hash1);
        Assert.Contains(wants, w => w.Value == hash2);
        Assert.Contains(wants, w => w.Value == hash3);
    }
}
