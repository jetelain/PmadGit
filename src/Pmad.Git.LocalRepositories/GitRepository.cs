using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// High-level entry point for querying commits, trees, and blobs from a local git repository.
/// </summary>
public sealed class GitRepository
{
    private readonly GitObjectStore _objectStore;
    private readonly Dictionary<GitHash, GitCommit> _commitCache = new();
    private readonly Dictionary<GitHash, GitTree> _treeCache = new();
    private readonly object _commitLock = new();
    private readonly object _treeLock = new();
    private readonly Lazy<Dictionary<string, GitHash>> _references;

    private GitRepository(string rootPath, string gitDirectory)
    {
        RootPath = rootPath;
        GitDirectory = gitDirectory;
        _objectStore = new GitObjectStore(gitDirectory);
        _references = new Lazy<Dictionary<string, GitHash>>(LoadReferences, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Absolute path to the repository working tree root.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Absolute path to the repository .git directory.
    /// </summary>
    public string GitDirectory { get; }

    /// <summary>
    /// Opens a git repository located at <paramref name="path"/> or its parent folders.
    /// </summary>
    /// <param name="path">Path pointing to a working tree or .git directory.</param>
    /// <returns>An initialized <see cref="GitRepository"/>.</returns>
    public static GitRepository Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Path.GetFileName(fullPath).Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(fullPath)?.FullName ?? throw new DirectoryNotFoundException("Unable to determine repository root");
            return new GitRepository(parent, fullPath);
        }

        var gitDir = Path.Combine(fullPath, ".git");
        if (Directory.Exists(gitDir))
        {
            return new GitRepository(fullPath, gitDir);
        }

        if (File.Exists(Path.Combine(fullPath, "HEAD")) && File.Exists(Path.Combine(fullPath, "config")))
        {
            return new GitRepository(fullPath, fullPath);
        }

        throw new DirectoryNotFoundException($"Unable to locate a git repository starting from '{path}'");
    }

    /// <summary>
    /// Resolves <paramref name="reference"/> (defaults to HEAD) and returns the corresponding commit.
    /// </summary>
    /// <param name="reference">Commit hash or reference name; HEAD if omitted.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The resolved <see cref="GitCommit"/>.</returns>
    public Task<GitCommit> GetCommitAsync(string? reference = null, CancellationToken cancellationToken = default)
        => GetCommitAsync(ResolveReference(reference ?? "HEAD"), cancellationToken);

    /// <summary>
    /// Enumerates commits reachable from <paramref name="reference"/> in depth-first order.
    /// </summary>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of commits, newest first.</returns>
    public async IAsyncEnumerable<GitCommit> EnumerateCommitsAsync(string? reference = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var start = ResolveReference(reference ?? "HEAD");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<GitHash>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (!visited.Add(current.Value))
            {
                continue;
            }

            var commit = await GetCommitAsync(current, cancellationToken).ConfigureAwait(false);
            yield return commit;

            for (var i = commit.Parents.Count - 1; i >= 0; i--)
            {
                stack.Push(commit.Parents[i]);
            }
        }
    }

    /// <summary>
    /// Enumerates the tree contents of a commit, optionally scoped to a sub-path.
    /// </summary>
    /// <param name="reference">Commit hash or ref to inspect; defaults to HEAD.</param>
    /// <param name="path">Optional directory path inside the tree to enumerate.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of tree items rooted at the specified path.</returns>
    public async IAsyncEnumerable<GitTreeItem> EnumerateCommitTreeAsync(
        string? reference = null,
        string? path = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var commit = await GetCommitAsync(reference, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
        {
            await foreach (var item in EnumerateTreeAsync(commit.Tree, string.Empty, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        var normalized = NormalizePath(path);
        if (string.IsNullOrEmpty(normalized))
        {
            await foreach (var item in EnumerateTreeAsync(commit.Tree, string.Empty, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var currentTreeHash = commit.Tree;
        GitTreeEntry? entry = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var currentTree = await GetTreeAsync(currentTreeHash, cancellationToken).ConfigureAwait(false);
            entry = currentTree.Entries.FirstOrDefault(e => e.Name.Equals(segments[i], StringComparison.Ordinal));
            if (entry is null)
            {
                throw new DirectoryNotFoundException($"Path '{normalized}' not found in commit {commit.Id}");
            }

            var isLast = i == segments.Length - 1;
            if (isLast)
            {
                if (entry.Kind == GitTreeEntryKind.Tree)
                {
                    await foreach (var item in EnumerateTreeAsync(entry.Hash, normalized, cancellationToken).ConfigureAwait(false))
                    {
                        yield return item;
                    }
                }
                else
                {
                    yield return new GitTreeItem(normalized, entry);
                }

                yield break;
            }

            if (entry.Kind != GitTreeEntryKind.Tree)
            {
                throw new InvalidOperationException($"Segment '{segments[i]}' is not a directory");
            }

            currentTreeHash = entry.Hash;
        }
    }

    /// <summary>
    /// Reads the blob content at <paramref name="filePath"/> from the specified <paramref name="reference"/>.
    /// </summary>
    /// <param name="filePath">Repository-relative file path using / separators.</param>
    /// <param name="reference">Commit hash or ref to read from; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The blob payload as a byte array.</returns>
    public async Task<byte[]> ReadFileAsync(string filePath, string? reference = null, CancellationToken cancellationToken = default)
    {
        var commit = await GetCommitAsync(reference, cancellationToken).ConfigureAwait(false);
        var normalized = NormalizePath(filePath);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("File path must reference a file", nameof(filePath));
        }

        var blobHash = await GetBlobHashAsync(commit.Tree, normalized, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File '{filePath}' not found in commit {commit.Id}");

        var blob = await _objectStore.ReadObjectNoCacheAsync(blobHash, cancellationToken).ConfigureAwait(false);
        if (blob.Type != GitObjectType.Blob)
        {
            throw new InvalidOperationException($"Object {blobHash} is not a blob");
        }

        return blob.Content;
    }

    /// <summary>
    /// Streams commits where <paramref name="filePath"/> changed, newest first.
    /// </summary>
    /// <param name="filePath">Repository-relative file path to inspect.</param>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of commits affecting the file.</returns>
    public async IAsyncEnumerable<GitCommit> GetFileHistoryAsync(
        string filePath,
        string? reference = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(filePath);
        if (string.IsNullOrEmpty(normalizedPath))
        {
            throw new ArgumentException("File path must reference a file", nameof(filePath));
        }

        GitHash? lastBlob = null;
        await foreach (var commit in EnumerateCommitsAsync(reference, cancellationToken).ConfigureAwait(false))
        {
            var blobHash = await GetBlobHashAsync(commit.Tree, normalizedPath, cancellationToken).ConfigureAwait(false);
            if (blobHash is null)
            {
                continue;
            }

            if (!lastBlob.HasValue || !lastBlob.Value.Equals(blobHash.Value))
            {
                lastBlob = blobHash;
                yield return commit;
            }
        }
    }

    private async Task<GitCommit> GetCommitAsync(GitHash hash, CancellationToken cancellationToken)
    {
        lock (_commitLock)
        {
            if (_commitCache.TryGetValue(hash, out var cached))
            {
                return cached;
            }
        }

        var data = await _objectStore.ReadObjectAsync(hash, cancellationToken).ConfigureAwait(false);
        if (data.Type != GitObjectType.Commit)
        {
            throw new InvalidOperationException($"Object {hash} is not a commit");
        }

        var commit = GitCommit.Parse(hash, data.Content);
        lock (_commitLock)
        {
            _commitCache[hash] = commit;
        }

        return commit;
    }

    private async Task<GitTree> GetTreeAsync(GitHash hash, CancellationToken cancellationToken)
    {
        lock (_treeLock)
        {
            if (_treeCache.TryGetValue(hash, out var cached))
            {
                return cached;
            }
        }

        var data = await _objectStore.ReadObjectAsync(hash, cancellationToken).ConfigureAwait(false);
        if (data.Type != GitObjectType.Tree)
        {
            throw new InvalidOperationException($"Object {hash} is not a tree");
        }

        var tree = GitTree.Parse(hash, data.Content, _objectStore.HashLengthBytes);
        lock (_treeLock)
        {
            _treeCache[hash] = tree;
        }

        return tree;
    }

    private GitHash ResolveReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference!.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveHead();
        }

        if (GitHash.TryParse(reference, out var hash))
        {
            return hash;
        }

        if (TryResolveReferencePath(reference, out hash))
        {
            return hash;
        }

        if (TryResolveReferencePath($"refs/heads/{reference}", out hash))
        {
            return hash;
        }

        if (TryResolveReferencePath($"refs/tags/{reference}", out hash))
        {
            return hash;
        }

        if (TryResolveReferencePath($"refs/remotes/{reference}", out hash))
        {
            return hash;
        }

        throw new InvalidOperationException($"Unknown reference '{reference}'");
    }

    private GitHash ResolveHead()
    {
        var headPath = Path.Combine(GitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            throw new FileNotFoundException("HEAD reference not found", headPath);
        }

        var content = File.ReadAllText(headPath).Trim();
        if (content.StartsWith("ref: ", StringComparison.Ordinal))
        {
            var target = content[5..].Trim();
            if (TryResolveReferencePath(target, out var hash))
            {
                return hash;
            }

            throw new InvalidOperationException($"Unable to resolve ref '{target}' pointed by HEAD");
        }

        if (GitHash.TryParse(content, out var direct))
        {
            return direct;
        }

        throw new InvalidDataException("HEAD does not contain a valid reference");
    }

    private bool TryResolveReferencePath(string referencePath, out GitHash hash)
    {
        var normalized = referencePath.Replace('\\', '/');
        var refs = _references.Value;
        if (refs.TryGetValue(normalized, out hash))
        {
            return true;
        }

        var filePath = Path.Combine(GitDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(filePath))
        {
            var content = File.ReadAllText(filePath).Trim();
            if (GitHash.TryParse(content, out hash))
            {
                return true;
            }
        }

        hash = default;
        return false;
    }

    private Dictionary<string, GitHash> LoadReferences()
    {
        var refs = new Dictionary<string, GitHash>(StringComparer.Ordinal);
        var refsRoot = Path.Combine(GitDirectory, "refs");
        if (Directory.Exists(refsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(refsRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(GitDirectory, file).Replace('\\', '/');
                var content = File.ReadAllText(file).Trim();
                if (GitHash.TryParse(content, out var hash))
                {
                    refs[relative] = hash;
                }
            }
        }

        var packedRefs = Path.Combine(GitDirectory, "packed-refs");
        if (File.Exists(packedRefs))
        {
            foreach (var line in File.ReadLines(packedRefs))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('^'))
                {
                    continue;
                }

                var separator = trimmed.IndexOf(' ');
                if (separator <= 0)
                {
                    continue;
                }

                var hashString = trimmed[..separator];
                var name = trimmed[(separator + 1)..];
                if (GitHash.TryParse(hashString, out var hash))
                {
                    refs[name] = hash;
                }
            }
        }

        return refs;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        var normalized = path.Replace('\\', '/');
        normalized = normalized.Trim();
        normalized = normalized.Trim('/');
        return normalized;
    }

    private async IAsyncEnumerable<GitTreeItem> EnumerateTreeAsync(
        GitHash treeHash,
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tree = await GetTreeAsync(treeHash, cancellationToken).ConfigureAwait(false);
        foreach (var entry in tree.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";
            yield return new GitTreeItem(path, entry);
            if (entry.Kind == GitTreeEntryKind.Tree)
            {
                await foreach (var child in EnumerateTreeAsync(entry.Hash, path, cancellationToken).ConfigureAwait(false))
                {
                    yield return child;
                }
            }
        }
    }

    private async Task<GitHash?> GetBlobHashAsync(GitHash treeHash, string normalizedPath, CancellationToken cancellationToken)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var currentTree = await GetTreeAsync(treeHash, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var entry = currentTree.Entries.FirstOrDefault(e => e.Name.Equals(segment, StringComparison.Ordinal));
            if (entry is null)
            {
                return null;
            }

            var isLast = i == segments.Length - 1;
            if (isLast)
            {
                return entry.Kind == GitTreeEntryKind.Tree ? null : entry.Hash;
            }

            if (entry.Kind != GitTreeEntryKind.Tree)
            {
                return null;
            }

            currentTree = await GetTreeAsync(entry.Hash, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

}

