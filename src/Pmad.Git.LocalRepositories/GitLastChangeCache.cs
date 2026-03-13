using System.Text.Json;
using System.Text.Json.Serialization;
using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Incremental on-disk cache for <see cref="GitRepository.GetFilesWithLastChangeAsync"/>.
/// One JSON file is written per commit hash under <c>{gitDirectory}/pmad-cache/last-change/</c>,
/// storing the full file list for that commit.  Path, search-depth, and predicate filters are
/// applied at read time.  Because git commits are immutable, a cache entry is permanently valid
/// once written.
/// </summary>
internal sealed class GitLastChangeCache
{
    private readonly string _cacheDirectory;
    private const int CacheVersion = 1;

    internal GitLastChangeCache(string gitDirectory)
    {
        _cacheDirectory = Path.Combine(gitDirectory, "pmad-cache", "last-change");
    }

    /// <summary>
    /// Tries to load a previously computed result from disk.
    /// </summary>
    /// <param name="commitHash">Resolved (hex) commit hash that was used as the start point.</param>
    /// <param name="getCommit">Callback used to reconstruct <see cref="GitCommit"/> objects from their hashes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full cached list without any filtering, or <see langword="null"/> if no valid cache entry exists.</returns>
    internal async Task<IReadOnlyList<GitFileLastChange>?> TryReadAsync(
        GitHash commitHash,
        Func<GitHash, Task<GitCommit>> getCommit,
        CancellationToken cancellationToken)
    {
        var data = await TryDeserializeAsync(commitHash, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return null;
        }

        var result = new List<GitFileLastChange>(data.Files!.Count);
        foreach (var entry in data.Files)
        {
            var hash = new GitHash(entry.Commit);
            var commit = await getCommit(hash).ConfigureAwait(false);
            result.Add(new GitFileLastChange(entry.Path, commit));
        }

        return result;
    }

    /// <summary>
    /// Returns a raw path→commit-hash mapping for the specified commit, or <see langword="null"/>
    /// if no valid cache entry exists.  Used by the incremental traversal to short-circuit history
    /// walking when a cached ancestor is encountered.
    /// </summary>
    internal async Task<Dictionary<string, string>?> TryReadRawAsync(
        GitHash commitHash,
        CancellationToken cancellationToken)
    {
        var data = await TryDeserializeAsync(commitHash, cancellationToken).ConfigureAwait(false);
        return data?.Files?.ToDictionary(e => e.Path, e => e.Commit, StringComparer.Ordinal);
    }

    /// <summary>
    /// Persists a computed result to disk.  Failures are silently swallowed — the cache is
    /// best-effort and its absence never affects correctness.
    /// </summary>
    internal async Task WriteAsync(
        GitHash commitHash,
        IReadOnlyList<GitFileLastChange> entries,
        CancellationToken cancellationToken)
    {
        var filePath = GetCacheFilePath(commitHash);
        var tmpPath = filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var data = new GitLastChangeCacheData
            {
                Version = CacheVersion,
                Files = entries.Select(e => new GitLastChangeCacheEntry
                {
                    Path = e.Path,
                    Commit = e.Commit.Id.Value
                }).ToList()
            };

            // Write to a temp file then rename for atomicity so a concurrent reader never
            // sees a partially-written file.

            using (var stream = File.Create(tmpPath))
            {
                using var gz = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest);
                await JsonSerializer.SerializeAsync(gz, data, GitLastChangeCacheContext.Default.GitLastChangeCacheData, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FileHelper.SafeDelete(tmpPath);
            // Cache write is best-effort; ignore all non-cancellation failures.
        }
    }

    private async Task<GitLastChangeCacheData?> TryDeserializeAsync(GitHash commitHash, CancellationToken cancellationToken)
    {
        var filePath = GetCacheFilePath(commitHash);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            using var gz = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
            var data = await JsonSerializer.DeserializeAsync(gz, GitLastChangeCacheContext.Default.GitLastChangeCacheData, cancellationToken).ConfigureAwait(false);
            return data?.Version == CacheVersion && data.Files is not null ? data : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // If the cache file is corrupt or unreadable, fall back to recomputing.
            return null;
        }
    }

    private string GetCacheFilePath(GitHash commitHash)
    {
        var prefix = commitHash.Value[..2];
        var rest = commitHash.Value[2..];
        return Path.Combine(_cacheDirectory, prefix, $"{rest}.json.gz");
    }
}

internal sealed class GitLastChangeCacheData
{
    [JsonPropertyName("v")]
    public int Version { get; set; }

    [JsonPropertyName("f")]
    public List<GitLastChangeCacheEntry>? Files { get; set; }
}

internal sealed class GitLastChangeCacheEntry
{
    [JsonPropertyName("p")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("c")]
    public string Commit { get; set; } = string.Empty;
}

[JsonSerializable(typeof(GitLastChangeCacheData))]
internal partial class GitLastChangeCacheContext : JsonSerializerContext { }
