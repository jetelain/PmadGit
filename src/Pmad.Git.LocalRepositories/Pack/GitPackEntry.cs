namespace Pmad.Git.LocalRepositories.Pack;

internal sealed class GitPackEntry
{
    private readonly GitPackIndex _index;
    private readonly string _packPath;
    private readonly int _hashLengthBytes;

    private GitPackEntry(string packPath, int hashLengthBytes, GitPackIndex index)
    {
        _packPath = packPath;
        _hashLengthBytes = hashLengthBytes;
        _index = index;
    }

    public static async Task<GitPackEntry> CreateAsync(
        string idxPath,
        string packPath,
        int hashLengthBytes,
        CancellationToken cancellationToken = default)
    {
        var index = await GitPackIndex.LoadAsync(idxPath, hashLengthBytes, cancellationToken).ConfigureAwait(false);
        var entry = new GitPackEntry(packPath, hashLengthBytes, index);
        entry.ValidatePackFile();
        return entry;
    }

    public async Task<GitObjectData?> TryReadObject(
        GitHash hash,
        Func<GitHash, CancellationToken, Task<GitObjectData>> resolve,
        CancellationToken cancellationToken)
    {
        if (!_index.TryGetOffset(hash, out var offset))
        {
            return null;
        }

        return await ReadAtOffset(offset, resolve, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitObjectData> ReadAtOffset(
        long offset,
        Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        await using var stream = new FileStream(_packPath, options);
        return await ReadObject(stream, offset, resolveByHash, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitObjectData> ReadObject(
        FileStream stream,
        long offset,
        Func<GitHash, CancellationToken, Task<GitObjectData>> resolveByHash,
        CancellationToken cancellationToken)
    {
        stream.Position = offset;
        return await GitPackObjectReader.ReadObjectAsync(
            stream,
            offset,
            _hashLengthBytes,
            resolveByHash,
            (off, ct) => ReadAtOffset(off, resolveByHash, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private void ValidatePackFile()
    {
        using var stream = new FileStream(_packPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (header[0] != 'P' || header[1] != 'A' || header[2] != 'C' || header[3] != 'K')
        {
            throw new InvalidDataException($"Pack file '{_packPath}' does not start with PACK signature");
        }
    }
}

