using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Pmad.Git.LocalRepositories.Pack;

namespace Pmad.Git.LocalRepositories;

internal sealed class GitObjectStore
{
    private readonly string _gitDirectory;
    private Task<GitPackEntry[]> _packsTask;
    private readonly int _hashLengthBytes;

    public GitObjectStore(string gitDirectory)
    {
        _gitDirectory = gitDirectory;
        _hashLengthBytes = DetectHashLength(gitDirectory);
        _packsTask = LoadPackEntriesAsync();
    }

    /// <summary>
    /// Gets the number of bytes used to represent object hashes in this repository.
    /// </summary>
    public int HashLengthBytes => _hashLengthBytes;

    public void InvalidateCaches()
    {
        Interlocked.Exchange(ref _packsTask, LoadPackEntriesAsync());
    }

    public async Task<GitObjectData> ReadObjectAsync(GitHash hash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var loose = await TryReadLooseObjectAsync(hash, cancellationToken).ConfigureAwait(false);
        if (loose is not null)
        {
            return loose;
        }

        var packs = await _packsTask.ConfigureAwait(false);

        foreach (var pack in packs)
        {
            var packed = await pack.TryReadObject(hash, ReadObjectAsync, cancellationToken).ConfigureAwait(false);
            if (packed is not null)
            {
                return packed;
            }
        }

        throw new FileNotFoundException($"Git object {hash} could not be found");
    }

    private async Task<GitObjectData?> TryReadLooseObjectAsync(GitHash hash, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_gitDirectory, "objects", hash.Value[..2], hash.Value[2..]);
        if (!File.Exists(path))
        {
            return null;
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        await using var stream = new FileStream(path, options);
        using var zlib = new ZLibStream(stream, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        await zlib.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var content = buffer.ToArray();
        var separator = Array.IndexOf(content, (byte)0);
        if (separator < 0)
        {
            throw new InvalidDataException("Invalid loose object: missing header");
        }

        var header = System.Text.Encoding.ASCII.GetString(content, 0, separator);
        var spaceIndex = header.IndexOf(' ');
        if (spaceIndex < 0)
        {
            throw new InvalidDataException("Invalid loose object header");
        }

        var typeString = header[..spaceIndex];
        var payload = content[(separator + 1)..];
        return new GitObjectData(GitObjectTypeHelper.ParseType(typeString), payload);
    }

    private async Task<GitPackEntry[]> LoadPackEntriesAsync()
    {
        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        if (!Directory.Exists(packDir))
        {
            return Array.Empty<GitPackEntry>();
        }

        var creationTasks = new List<Task<GitPackEntry>>();
        foreach (var idxPath in Directory.GetFiles(packDir, "*.idx"))
        {
            var packPath = Path.ChangeExtension(idxPath, ".pack");
            if (File.Exists(packPath))
            {
                creationTasks.Add(GitPackEntry.CreateAsync(idxPath, packPath, _hashLengthBytes));
            }
        }

        if (creationTasks.Count == 0)
        {
            return Array.Empty<GitPackEntry>();
        }

        return await Task.WhenAll(creationTasks).ConfigureAwait(false);
    }

    private static int DetectHashLength(string gitDirectory)
    {
        var configPath = Path.Combine(gitDirectory, "config");
        if (File.Exists(configPath))
        {
            var format = ReadObjectFormat(configPath);
            if (string.Equals(format, "sha256", StringComparison.OrdinalIgnoreCase))
            {
                return GitHash.Sha256ByteLength;
            }
        }

        return GitHash.Sha1ByteLength;
    }

    private static string? ReadObjectFormat(string configPath)
    {
        string? currentSection = null;
        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (string.Equals(currentSection, "extensions", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(key, "objectformat", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }




    public async Task<GitHash> WriteObjectAsync(GitObjectType type, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var header = Encoding.ASCII.GetBytes($"{GitObjectTypeHelper.GetObjectTypeName(type)} {content.Length}\0");
        var buffer = new byte[header.Length + content.Length];
        Buffer.BlockCopy(header, 0, buffer, 0, header.Length);
        content.Span.CopyTo(buffer.AsSpan(header.Length));

        using var algorithm = CreateHashAlgorithm();
        var hashBytes = algorithm.ComputeHash(buffer);
        var hash = GitHash.FromBytes(hashBytes);

        var objectPath = Path.Combine(_gitDirectory, "objects", hash.Value[..2], hash.Value[2..]);
        if (!File.Exists(objectPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };

            try
            {
                await using var stream = new FileStream(objectPath, options);
                await using var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: false);
                await zlib.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(objectPath))
            {
                // Object already exists; reuse it.
            }
        }

        return hash;
    }

    private HashAlgorithm CreateHashAlgorithm() => _hashLengthBytes switch
    {
        GitHash.Sha1ByteLength => SHA1.Create(),
        GitHash.Sha256ByteLength => SHA256.Create(),
        _ => throw new NotSupportedException("Unsupported git object hash length.")
    };

}
