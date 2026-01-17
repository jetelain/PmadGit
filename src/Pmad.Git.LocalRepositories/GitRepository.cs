using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

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
    private Lazy<Task<Dictionary<string, GitHash>>> _references;
    private const int RegularFileMode = 33188; // 100644 in octal
    private const int DirectoryMode = 16384;   // 040000 in octal

    private GitRepository(string rootPath, string gitDirectory)
    {
        RootPath = rootPath;
        GitDirectory = gitDirectory;
        _objectStore = new GitObjectStore(gitDirectory);
        _references = CreateReferenceCache();
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
    public async Task<GitCommit> GetCommitAsync(string? reference = null, CancellationToken cancellationToken = default)
    {
        var hash = await ResolveReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        return await GetCommitAsync(hash, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Enumerates commits reachable from <paramref name="reference"/> in depth-first order.
    /// </summary>
    /// <param name="reference">Starting reference or commit hash; defaults to HEAD.</param>
    /// <param name="cancellationToken">Token used to cancel the async iteration.</param>
    /// <returns>An async stream of commits, newest first.</returns>
    public async IAsyncEnumerable<GitCommit> EnumerateCommitsAsync(string? reference = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var start = await ResolveReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Creates a new commit on the specified branch by applying the provided operations.
    /// </summary>
    /// <param name="branchName">Branch to update (short name or fully qualified ref).</param>
    /// <param name="operations">Sequence of file-system operations to apply.</param>
    /// <param name="metadata">Commit metadata (message, author, committer).</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash of the newly created commit.</returns>
    public async Task<GitHash> CreateCommitAsync(
        string branchName,
        IEnumerable<GitCommitOperation> operations,
        GitCommitMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("Branch name cannot be empty", nameof(branchName));
        }

        if (operations is null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        var referencePath = NormalizeReference(branchName);
        var parentHash = await TryResolveReferencePathAsync(referencePath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Branch '{branchName}' does not exist.");

        var parentCommit = await GetCommitAsync(parentHash, cancellationToken).ConfigureAwait(false);
        var entries = await LoadLeafEntriesAsync(parentCommit.Tree, cancellationToken).ConfigureAwait(false);
        var changed = false;

        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation is null)
            {
                throw new ArgumentException("Operations cannot contain null entries", nameof(operations));
            }

            var normalizedPath = NormalizePath(operation.Path);
            switch (operation)
            {
                case AddFileOperation add:
                    changed |= await ApplyAddFileAsync(entries, normalizedPath, add.Content, cancellationToken).ConfigureAwait(false);
                    break;
                case UpdateFileOperation update:
                    changed |= await ApplyUpdateFileAsync(entries, normalizedPath, update.Content, cancellationToken).ConfigureAwait(false);
                    break;
                case RemoveFileOperation remove:
                    changed |= ApplyRemoveFile(entries, normalizedPath);
                    break;
                case MoveFileOperation move:
                    changed |= ApplyMoveFile(entries, normalizedPath, NormalizePath(move.DestinationPath));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported operation type '{operation.GetType().Name}'.");
            }
        }

        if (!changed)
        {
            throw new InvalidOperationException("The requested operations do not change the repository state.");
        }

        var newTreeHash = await BuildTreeAsync(entries, cancellationToken).ConfigureAwait(false);
        if (newTreeHash.Equals(parentCommit.Tree))
        {
            throw new InvalidOperationException("The resulting tree matches the parent commit.");
        }

        var commitPayload = BuildCommitPayload(newTreeHash, parentHash, metadata);
        var commitHash = await WriteObjectAsync(GitObjectType.Commit, commitPayload, cancellationToken).ConfigureAwait(false);

        var parsedCommit = GitCommit.Parse(commitHash, commitPayload);
        lock (_commitLock)
        {
            _commitCache[commitHash] = parsedCommit;
        }

        await UpdateReferenceAsync(referencePath, commitHash, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _references, CreateReferenceCache());

        return commitHash;
    }

    /// <summary>
    /// Clears cached git metadata so subsequent operations reflect the current repository state.
    /// </summary>
    /// <param name="clearAllData">Clears all cached data, including data that should not change on normal git operations</param>
    public void InvalidateCaches(bool clearAllData = false)
    {
        _objectStore.InvalidateCaches(clearAllData);

        if (clearAllData)
        {
            lock (_commitLock)
            {
                _commitCache.Clear();
            }

            lock (_treeLock)
            {
                _treeCache.Clear();
            }
        }

        Interlocked.Exchange(ref _references, CreateReferenceCache());
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

    private async Task<GitHash> ResolveReferenceAsync(string? reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference!.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveHeadAsync(cancellationToken).ConfigureAwait(false);
        }

        var nonEmptyReference = reference!;
        if (GitHash.TryParse(nonEmptyReference, out var hash))
        {
            return hash;
        }

        var candidates = new[]
        {
            nonEmptyReference,
            $"refs/heads/{nonEmptyReference}",
            $"refs/tags/{nonEmptyReference}",
            $"refs/remotes/{nonEmptyReference}"
        };

        foreach (var candidate in candidates)
        {
            var resolved = await TryResolveReferencePathAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (resolved.HasValue)
            {
                return resolved.Value;
            }
        }

        throw new InvalidOperationException($"Unknown reference '{reference}'");
    }

    private async Task<GitHash> ResolveHeadAsync(CancellationToken cancellationToken)
    {
        var headPath = Path.Combine(GitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            throw new FileNotFoundException("HEAD reference not found", headPath);
        }

        var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
        if (content.StartsWith("ref: ", StringComparison.Ordinal))
        {
            var target = content[5..].Trim();
            var resolved = await TryResolveReferencePathAsync(target, cancellationToken).ConfigureAwait(false);
            if (resolved.HasValue)
            {
                return resolved.Value;
            }

            throw new InvalidOperationException($"Unable to resolve ref '{target}' pointed by HEAD");
        }

        if (GitHash.TryParse(content, out var direct))
        {
            return direct;
        }

        throw new InvalidDataException("HEAD does not contain a valid reference");
    }

    private async Task<GitHash?> TryResolveReferencePathAsync(string referencePath, CancellationToken cancellationToken)
    {
        var normalized = referencePath.Replace('\\', '/');
        var refs = await _references.Value.ConfigureAwait(false);
        if (refs.TryGetValue(normalized, out var hash))
        {
            return hash;
        }

        var filePath = Path.Combine(GitDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(filePath))
        {
            var content = (await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false)).Trim();
            if (GitHash.TryParse(content, out hash))
            {
                return hash;
            }
        }

        return null;
    }

    private static string NormalizeReference(string branchName)
    {
        var trimmed = branchName.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Branch name cannot be empty", nameof(branchName));
        }

        if (trimmed.StartsWith("refs/", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (trimmed.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Specify a branch name instead of HEAD", nameof(branchName));
        }

        return $"refs/heads/{trimmed}";
    }

    private async Task<Dictionary<string, TreeLeaf>> LoadLeafEntriesAsync(GitHash treeHash, CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, TreeLeaf>(StringComparer.Ordinal);
        await foreach (var item in EnumerateTreeAsync(treeHash, string.Empty, cancellationToken).ConfigureAwait(false))
        {
            if (item.Entry.Kind == GitTreeEntryKind.Tree)
            {
                continue;
            }

            entries[item.Path] = new TreeLeaf(item.Entry.Mode, item.Entry.Hash);
        }

        return entries;
    }

    private async Task<bool> ApplyAddFileAsync(Dictionary<string, TreeLeaf> entries, string path, byte[] content, CancellationToken cancellationToken)
    {
        if (entries.ContainsKey(path))
        {
            throw new InvalidOperationException($"File '{path}' already exists.");
        }

        var blobHash = await WriteObjectAsync(GitObjectType.Blob, content, cancellationToken).ConfigureAwait(false);
        entries[path] = new TreeLeaf(RegularFileMode, blobHash);
        return true;
    }

    private async Task<bool> ApplyUpdateFileAsync(Dictionary<string, TreeLeaf> entries, string path, byte[] content, CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(path, out var existing))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.");
        }

        var blobHash = await WriteObjectAsync(GitObjectType.Blob, content, cancellationToken).ConfigureAwait(false);
        if (existing.Hash.Equals(blobHash))
        {
            return false;
        }

        entries[path] = existing with { Hash = blobHash };
        return true;
    }

    private static bool ApplyRemoveFile(Dictionary<string, TreeLeaf> entries, string path)
    {
        if (!entries.Remove(path))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.");
        }

        return true;
    }

    private static bool ApplyMoveFile(Dictionary<string, TreeLeaf> entries, string sourcePath, string destinationPath)
    {
        if (sourcePath.Equals(destinationPath, StringComparison.Ordinal))
        {
            return false;
        }

        if (!entries.TryGetValue(sourcePath, out var leaf))
        {
            throw new FileNotFoundException($"File '{sourcePath}' does not exist.");
        }

        if (entries.ContainsKey(destinationPath))
        {
            throw new InvalidOperationException($"File '{destinationPath}' already exists.");
        }

        entries.Remove(sourcePath);
        entries[destinationPath] = leaf;
        return true;
    }

    private async Task<GitHash> BuildTreeAsync(IReadOnlyDictionary<string, TreeLeaf> leaves, CancellationToken cancellationToken)
    {
        var root = new TreeBuilderNode();
        foreach (var (path, leaf) in leaves)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (!current.Directories.TryGetValue(segments[i], out var child))
                {
                    child = new TreeBuilderNode();
                    current.Directories[segments[i]] = child;
                }

                current = child;
            }

            current.Leaves[segments[^1]] = leaf;
        }

        return await WriteTreeNodeAsync(root, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitHash> WriteTreeNodeAsync(TreeBuilderNode node, CancellationToken cancellationToken)
    {
        var entries = new List<TreeEntryData>();

        foreach (var (name, child) in node.Directories)
        {
            var hash = await WriteTreeNodeAsync(child, cancellationToken).ConfigureAwait(false);
            entries.Add(new TreeEntryData(name, DirectoryMode, hash));
        }

        foreach (var (name, leaf) in node.Leaves)
        {
            entries.Add(new TreeEntryData(name, leaf.Mode, leaf.Hash));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        using var buffer = new MemoryStream();
        foreach (var entry in entries)
        {
            var modeString = Convert.ToString(entry.Mode, 8) ?? string.Empty;
            var modeBytes = Encoding.ASCII.GetBytes(modeString);
            buffer.Write(modeBytes, 0, modeBytes.Length);
            buffer.WriteByte((byte)' ');
            var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            buffer.Write(nameBytes, 0, nameBytes.Length);
            buffer.WriteByte(0);
            var hashBytes = entry.Hash.ToByteArray();
            buffer.Write(hashBytes, 0, hashBytes.Length);
        }

        return await WriteObjectAsync(GitObjectType.Tree, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static byte[] BuildCommitPayload(GitHash treeHash, GitHash parentHash, GitCommitMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.Append("tree ").Append(treeHash.Value).Append('\n');
        builder.Append("parent ").Append(parentHash.Value).Append('\n');
        builder.Append("author ")
            .Append(metadata.Author.ToHeaderValue())
            .Append('\n');
        builder.Append("committer ")
            .Append(metadata.Committer.ToHeaderValue())
            .Append('\n');
        builder.Append('\n');
        builder.Append(metadata.Message);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private async Task UpdateReferenceAsync(string referencePath, GitHash commitHash, CancellationToken cancellationToken)
    {
        var refPath = Path.Combine(GitDirectory, referencePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(refPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempDirectory = directory ?? GitDirectory;
        var tempPath = Path.Combine(tempDirectory, $"{Path.GetFileName(refPath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, commitHash.Value + "\n", cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, refPath, overwrite: true);
    }

    private async Task<GitHash> WriteObjectAsync(GitObjectType type, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var header = Encoding.ASCII.GetBytes($"{GitObjectTypeHelper.GetObjectTypeName(type)} {content.Length}\0");
        var buffer = new byte[header.Length + content.Length];
        Buffer.BlockCopy(header, 0, buffer, 0, header.Length);
        content.Span.CopyTo(buffer.AsSpan(header.Length));

        using var algorithm = CreateHashAlgorithm();
        var hashBytes = algorithm.ComputeHash(buffer);
        var hash = GitHash.FromBytes(hashBytes);

        var objectPath = Path.Combine(GitDirectory, "objects", hash.Value[..2], hash.Value[2..]);
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

    private HashAlgorithm CreateHashAlgorithm() => _objectStore.HashLengthBytes switch
    {
        GitHash.Sha1ByteLength => SHA1.Create(),
        GitHash.Sha256ByteLength => SHA256.Create(),
        _ => throw new NotSupportedException("Unsupported git object hash length.")
    };

    private async Task<Dictionary<string, GitHash>> LoadReferencesAsync()
    {
        var refs = new Dictionary<string, GitHash>(StringComparer.Ordinal);
        var refsRoot = Path.Combine(GitDirectory, "refs");
        if (Directory.Exists(refsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(refsRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(GitDirectory, file).Replace('\\', '/');
                var content = (await File.ReadAllTextAsync(file).ConfigureAwait(false)).Trim();
                if (GitHash.TryParse(content, out var hash))
                {
                    refs[relative] = hash;
                }
            }
        }

        var packedRefs = Path.Combine(GitDirectory, "packed-refs");
        if (File.Exists(packedRefs))
        {
            var lines = await File.ReadAllLinesAsync(packedRefs).ConfigureAwait(false);
            foreach (var line in lines)
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

    private Lazy<Task<Dictionary<string, GitHash>>> CreateReferenceCache()
        => new Lazy<Task<Dictionary<string, GitHash>>>(LoadReferencesAsync, LazyThreadSafetyMode.ExecutionAndPublication);

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
