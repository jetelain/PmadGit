using System.Threading;
using Pmad.Git.LocalRepositories.Config;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Provides read and write access to git references stored on disk,
/// supporting both loose ref files and packed-refs.
/// </summary>
internal sealed class GitReferenceStore : IGitReferenceStore
{
    private readonly string _gitDirectory;
    private readonly IGitRepositoryLockManager _lockManager;
    private Lazy<Task<Dictionary<string, GitHash>>> _cache;

    public GitReferenceStore(string gitDirectory)
        : this(gitDirectory, new GitRepositoryLockManager())
    {
    }

    public GitReferenceStore(string gitDirectory, IGitRepositoryLockManager lockManager)
    {
        _gitDirectory = gitDirectory;
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        _cache = CreateCache();
    }

    /// <summary>
    /// Gets the lock manager used to synchronize reference/object writes for this repository.
    /// </summary>
    public IGitRepositoryLockManager LockManager => _lockManager;

    /// <inheritdoc/>
    public void InvalidateCaches()
    {
        Interlocked.Exchange(ref _cache, CreateCache());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, GitHash>> GetReferencesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await _cache.Value.ConfigureAwait(false);
        return new Dictionary<string, GitHash>(snapshot, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public Task<GitHash?> TryResolveReferenceAsync(string referencePath, CancellationToken cancellationToken = default)
    {
        return TryResolveReferenceCoreAsync(referencePath, visited: null, depth: 0, cancellationToken);
    }

    private async Task<GitHash?> TryResolveReferenceCoreAsync(
        string referencePath,
        HashSet<string>? visited,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth >= 10)
        {
            throw new InvalidOperationException($"Symbolic reference cycle or maximum depth exceeded while resolving '{referencePath}'.");
        }

        var normalized = referencePath.Replace('\\', '/');
        visited ??= new HashSet<string>(StringComparer.Ordinal);
        if (!visited.Add(normalized))
        {
            throw new InvalidOperationException($"Symbolic reference cycle detected: '{normalized}' was already visited.");
        }

        var refs = await _cache.Value.ConfigureAwait(false);
        if (refs.TryGetValue(normalized, out var hash))
        {
            return hash;
        }

        var filePath = Path.Combine(_gitDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(filePath))
        {
            var content = (await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false)).Trim();
            if (content.StartsWith("ref: ", StringComparison.Ordinal))
            {
                var target = content[5..].Trim();
                return await TryResolveReferenceCoreAsync(target, visited, depth + 1, cancellationToken).ConfigureAwait(false);
            }

            if (GitHash.TryParse(content, out hash))
            {
                return hash;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<GitHash> ResolveHeadAsync(CancellationToken cancellationToken = default)
    {
        var headPath = Path.Combine(_gitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            throw new FileNotFoundException("HEAD reference not found", headPath);
        }

        var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
        if (content.StartsWith("ref: ", StringComparison.Ordinal))
        {
            var target = content[5..].Trim();
            var resolved = await TryResolveReferenceAsync(target, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async Task<string?> GetCurrentBranchNameAsync(CancellationToken cancellationToken = default)
    {
        var headPath = Path.Combine(_gitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            return null;
        }

        var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
        const string prefix = "ref: refs/heads/";
        if (content.StartsWith(prefix, StringComparison.Ordinal))
        {
            var branch = content[prefix.Length..].Trim();
            if (string.IsNullOrEmpty(branch))
            {
                return null;
            }

            var refPath = "refs/heads/" + branch;
            var resolved = await TryResolveReferenceAsync(refPath, cancellationToken).ConfigureAwait(false);
            if (!resolved.HasValue)
            {
                return null;
            }

            return branch;
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<bool> IsHeadDetachedAsync(CancellationToken cancellationToken = default)
    {
        var headPath = Path.Combine(_gitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            return false;
        }

        var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
        return !content.StartsWith("ref: ", StringComparison.Ordinal) && GitHash.TryParse(content, out _);
    }

    /// <inheritdoc/>
    public async Task CreateReferenceAsync(
        string referencePath,
        GitHash targetCommit,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAbsoluteReferencePath(referencePath);
        using (await _lockManager.AcquireReferenceLockAsync(normalized, cancellationToken).ConfigureAwait(false))
        {
            await CheckDirectoryFileConflictAsync(normalized, cancellationToken).ConfigureAwait(false);

            if (!overwrite)
            {
                var existing = await TryResolveReferenceAsync(normalized, cancellationToken).ConfigureAwait(false);
                if (existing.HasValue)
                {
                    throw new InvalidOperationException($"Reference '{normalized}' already exists with value {existing.Value.Value}");
                }
            }

            await WriteReferenceAsync(normalized, targetCommit, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _cache, CreateCache());
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, GitHash>> GetReferencesByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        if (prefix is null)
        {
            throw new ArgumentNullException(nameof(prefix));
        }

        var normalizedPrefix = prefix.Replace('\\', '/');
        var allRefs = await GetReferencesAsync(cancellationToken).ConfigureAwait(false);
        var filtered = new Dictionary<string, GitHash>(StringComparer.Ordinal);
        foreach (var (key, value) in allRefs)
        {
            if (key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            {
                filtered[key] = value;
            }
        }
        return filtered;
    }

    /// <inheritdoc/>
    public async Task DeleteReferenceAsync(
        string referencePath,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAbsoluteReferencePath(referencePath);
        using (await _lockManager.AcquireMultipleReferenceLocksAsync(new[] { normalized, "packed-refs" }, cancellationToken).ConfigureAwait(false))
        {
            await DeleteReferenceAsyncInternal(normalized, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _cache, CreateCache());
        }
    }

    /// <inheritdoc/>
    public async Task RenameBranchAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var oldRef = NormalizeBranchRef(oldName);
        var newRef = NormalizeBranchRef(newName);

        if (string.Equals(oldRef, newRef, StringComparison.Ordinal))
        {
            return;
        }

        var normalizedOldBranch = oldRef["refs/heads/".Length..];
        var normalizedNewBranch = newRef["refs/heads/".Length..];

        using (await _lockManager.LockAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var targetCommit = await TryResolveReferenceAsync(oldRef, cancellationToken).ConfigureAwait(false);
            if (!targetCommit.HasValue)
            {
                throw new InvalidOperationException($"Branch '{normalizedOldBranch}' does not exist.");
            }

            var existingNew = await TryResolveReferenceAsync(newRef, cancellationToken).ConfigureAwait(false);
            if (existingNew.HasValue)
            {
                throw new InvalidOperationException($"A branch named '{normalizedNewBranch}' already exists.");
            }

            await CheckDirectoryFileConflictAsync(newRef, cancellationToken).ConfigureAwait(false);
            await WriteReferenceAsync(newRef, targetCommit.Value, cancellationToken).ConfigureAwait(false);
            await DeleteReferenceAsyncInternal(oldRef, cancellationToken).ConfigureAwait(false);

            var headPath = Path.Combine(_gitDirectory, "HEAD");
            if (File.Exists(headPath))
            {
                var headContent = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
                if (headContent == $"ref: {oldRef}")
                {
                    await File.WriteAllTextAsync(headPath, $"ref: {newRef}\n", cancellationToken).ConfigureAwait(false);
                }
            }

            var configPath = Path.Combine(_gitDirectory, "config");
            if (File.Exists(configPath))
            {
                var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                if (config.RenameSubsection("branch", normalizedOldBranch, normalizedNewBranch))
                {
                    await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                }
            }

            Interlocked.Exchange(ref _cache, CreateCache());
        }
    }

    /// <inheritdoc/>
    public async Task DeleteBranchAsync(
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        var refPath = NormalizeBranchRef(branchName);
        var normalizedBranch = refPath["refs/heads/".Length..];

        using (await _lockManager.LockAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var currentBranch = await GetCurrentBranchNameAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(currentBranch, normalizedBranch, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Cannot delete branch '{normalizedBranch}' used by worktree at '{_gitDirectory}'.");
            }

            var exists = await TryResolveReferenceAsync(refPath, cancellationToken).ConfigureAwait(false);
            if (!exists.HasValue)
            {
                throw new InvalidOperationException($"Branch '{normalizedBranch}' not found.");
            }

            await DeleteReferenceAsyncInternal(refPath, cancellationToken).ConfigureAwait(false);

            var configPath = Path.Combine(_gitDirectory, "config");
            if (File.Exists(configPath))
            {
                var config = await GitConfigFile.ReadFromFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                if (config.RemoveSection("branch", normalizedBranch))
                {
                    await config.WriteToFileAsync(configPath, cancellationToken).ConfigureAwait(false);
                }
            }

            Interlocked.Exchange(ref _cache, CreateCache());
        }
    }

    private static string NormalizeBranchRef(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        var normalized = branchName.Replace('\\', '/').Trim();
        if (!normalized.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            normalized = "refs/heads/" + normalized;
        }
        return NormalizeAbsoluteReferencePath(normalized);
    }

    /// <inheritdoc/>
    public async Task WriteReferenceWithValidationAsync(
        string referencePath,
        GitHash? expectedOldValue,
        GitHash? newValue,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeAbsoluteReferencePath(referencePath);
        using (await _lockManager.AcquireReferenceLockAsync(normalized, cancellationToken).ConfigureAwait(false))
        {
            await WriteReferenceWithValidationInternalAsync(normalized, expectedOldValue, newValue, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IGitMultipleReferenceLocks> AcquireMultipleReferenceLocksAsync(
        IEnumerable<string> referencePaths,
        CancellationToken cancellationToken = default)
    {
        if (referencePaths is null)
        {
            throw new ArgumentNullException(nameof(referencePaths));
        }

        var normalizedPaths = referencePaths.Select(NormalizeAbsoluteReferencePath).ToList();
        var lockDisposable = await _lockManager.AcquireMultipleReferenceLocksAsync(normalizedPaths, cancellationToken).ConfigureAwait(false);
        return new GitMultipleReferenceLocks(this, normalizedPaths, lockDisposable);
    }

    /// <summary>
    /// Acquires a lock for a single reference. Used internally by <see cref="GitRepository"/> for commit operations.
    /// </summary>
    internal Task<IDisposable> AcquireReferenceLockAsync(string referencePath, CancellationToken cancellationToken)
        => _lockManager.AcquireReferenceLockAsync(referencePath, cancellationToken);

    /// <summary>
    /// Writes a reference with validation without acquiring a lock.
    /// The caller must already hold the lock for <paramref name="normalized"/>.
    /// </summary>
    internal async Task WriteReferenceWithValidationInternalAsync(
        string normalized,
        GitHash? expectedOldValue,
        GitHash? newValue,
        CancellationToken cancellationToken)
    {
        await ValidateReferenceOldValueAsync(normalized, expectedOldValue, cancellationToken).ConfigureAwait(false);

        if (newValue.HasValue)
        {
            await CheckDirectoryFileConflictAsync(normalized, cancellationToken).ConfigureAwait(false);
            await WriteReferenceAsync(normalized, newValue.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DeleteReferenceAsyncInternal(normalized, cancellationToken).ConfigureAwait(false);
        }

        Interlocked.Exchange(ref _cache, CreateCache());
    }

    /// <summary>
    /// Writes a reference directly without acquiring a lock.
    /// The caller must already hold the reference lock for <paramref name="referencePath"/>.
    /// </summary>
    internal async Task WriteReferenceWithoutLockAsync(
        string referencePath,
        GitHash targetCommit,
        CancellationToken cancellationToken)
    {
        var trimmed = referencePath?.Replace('\\', '/').Trim();
        if (string.Equals(trimmed, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            var headPath = Path.Combine(_gitDirectory, "HEAD");
            var tempPath = Path.Combine(_gitDirectory, $"HEAD.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(tempPath, targetCommit.ToString() + "\n", cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, headPath, overwrite: true);
            Interlocked.Exchange(ref _cache, CreateCache());
            return;
        }

        var normalized = NormalizeAbsoluteReferencePath(referencePath!);
        await CheckDirectoryFileConflictAsync(normalized, cancellationToken).ConfigureAwait(false);
        await WriteReferenceAsync(normalized, targetCommit, cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _cache, CreateCache());
    }

    private async Task CheckDirectoryFileConflictAsync(string normalized, CancellationToken cancellationToken)
    {
        var allRefs = await GetReferencesAsync(cancellationToken).ConfigureAwait(false);

        var prefix = normalized;
        int lastSlash;
        while ((lastSlash = prefix.LastIndexOf('/')) > 0)
        {
            prefix = prefix[..lastSlash];
            if (allRefs.ContainsKey(prefix))
            {
                throw new InvalidOperationException($"Cannot create reference '{normalized}' because '{prefix}' exists as a reference.");
            }
        }

        var prefixWithSlash = normalized + "/";
        foreach (var existingRef in allRefs.Keys)
        {
            if (existingRef.StartsWith(prefixWithSlash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Cannot create reference '{normalized}' because '{existingRef}' exists under it.");
            }
        }

        var refPath = Path.Combine(_gitDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(refPath))
        {
            throw new InvalidOperationException($"Cannot create reference '{normalized}' because a directory with that name already exists.");
        }

        CheckAncestorFileConflict(normalized);
    }

    private void CheckAncestorFileConflict(string normalized)
    {
        var segments = normalized.Split('/');
        var current = _gitDirectory;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (File.Exists(current))
            {
                var ancestorRef = string.Join('/', segments.Take(i + 1));
                throw new InvalidOperationException($"Cannot create reference '{normalized}' because '{ancestorRef}' exists as a file.");
            }
        }
    }

    private async Task ValidateReferenceOldValueAsync(string normalized, GitHash? expectedOldValue, CancellationToken cancellationToken)
    {
        var currentValue = await TryResolveReferenceAsync(normalized, cancellationToken).ConfigureAwait(false);

        if (expectedOldValue.HasValue)
        {
            if (!currentValue.HasValue)
            {
                throw new InvalidOperationException($"Reference '{normalized}' does not exist, but was expected to have value {expectedOldValue.Value.Value}");
            }
            if (!currentValue.Value.Equals(expectedOldValue.Value))
            {
                throw new InvalidOperationException($"Reference '{normalized}' has value {currentValue.Value.Value}, but was expected to have value {expectedOldValue.Value.Value}");
            }
        }
        else
        {
            if (currentValue.HasValue)
            {
                throw new InvalidOperationException($"Reference '{normalized}' already exists with value {currentValue.Value.Value}");
            }
        }
    }

    private async Task WriteReferenceAsync(string referencePath, GitHash hash, CancellationToken cancellationToken)
    {
        var refPath = Path.Combine(_gitDirectory, referencePath.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(refPath))
        {
            throw new InvalidOperationException($"Cannot create reference '{referencePath}' because a directory with that name already exists.");
        }

        var directory = Path.GetDirectoryName(refPath);
        if (!string.IsNullOrEmpty(directory))
        {
            CheckAncestorFileConflict(referencePath);
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Cannot create reference '{referencePath}' due to a directory/file conflict.", ex);
            }
        }

        var tempDirectory = directory ?? _gitDirectory;
        var tempPath = Path.Combine(tempDirectory, $"{Path.GetFileName(refPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, hash.Value + "\n", cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(tempPath, refPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (Directory.Exists(refPath))
                {
                    throw new InvalidOperationException($"Cannot create reference '{referencePath}' because a directory with that name already exists.", ex);
                }
                throw new InvalidOperationException($"Cannot write reference '{referencePath}' due to a directory/file conflict.", ex);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    private async Task DeleteReferenceAsyncInternal(string normalizedReferencePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var refPath = Path.Combine(_gitDirectory, normalizedReferencePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refPath))
        {
            File.Delete(refPath);
        }

        var packedRefsPath = Path.Combine(_gitDirectory, "packed-refs");
        if (File.Exists(packedRefsPath))
        {
            var lines = await File.ReadAllLinesAsync(packedRefsPath, cancellationToken).ConfigureAwait(false);
            var updatedLines = new List<string>(lines.Length);
            var modified = false;
            var skippingPeeled = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (skippingPeeled)
                {
                    skippingPeeled = false;
                    if (trimmed.StartsWith('^'))
                    {
                        continue;
                    }
                }

                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                {
                    updatedLines.Add(line);
                    continue;
                }

                if (trimmed.StartsWith('^'))
                {
                    updatedLines.Add(line);
                    continue;
                }

                var separator = trimmed.IndexOf(' ');
                if (separator > 0)
                {
                    var name = trimmed[(separator + 1)..];
                    if (string.Equals(name, normalizedReferencePath, StringComparison.Ordinal))
                    {
                        modified = true;
                        skippingPeeled = true;
                        continue;
                    }
                }

                updatedLines.Add(line);
            }

            if (modified)
            {
                var tempPath = Path.Combine(_gitDirectory, $"packed-refs.{Guid.NewGuid():N}.tmp");
                await File.WriteAllLinesAsync(tempPath, updatedLines, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, packedRefsPath, overwrite: true);
            }
        }
    }

    private async Task<Dictionary<string, GitHash>> LoadReferencesAsync()
    {
        var refs = new Dictionary<string, GitHash>(StringComparer.Ordinal);

        var packedRefs = Path.Combine(_gitDirectory, "packed-refs");
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

        var refsRoot = Path.Combine(_gitDirectory, "refs");
        if (Directory.Exists(refsRoot))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            foreach (var file in Directory.EnumerateFiles(refsRoot, "*", options))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(_gitDirectory, file).Replace('\\', '/');
                try
                {
                    var content = (await File.ReadAllTextAsync(file).ConfigureAwait(false)).Trim();
                    if (GitHash.TryParse(content, out var hash))
                    {
                        refs[relative] = hash;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // File may be concurrently written or deleted; ignore transient lock/missing file
                }
            }
        }

        return refs;
    }

    private Lazy<Task<Dictionary<string, GitHash>>> CreateCache()
        => new(LoadReferencesAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static string NormalizeReferenceOrHead(string referencePath)
    {
        if (string.IsNullOrWhiteSpace(referencePath))
        {
            throw new ArgumentException("Reference path cannot be empty", nameof(referencePath));
        }

        var trimmed = referencePath.Replace('\\', '/').Trim();
        if (string.Equals(trimmed, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return "HEAD";
        }

        return NormalizeAbsoluteReferencePath(trimmed);
    }

    internal static string NormalizeAbsoluteReferencePath(string referencePath)
    {
        if (string.IsNullOrWhiteSpace(referencePath))
        {
            throw new ArgumentException("Reference path cannot be empty", nameof(referencePath));
        }

        var normalized = referencePath.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Reference path cannot be empty", nameof(referencePath));
        }

        if (!normalized.StartsWith("refs/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Absolute reference path must start with 'refs/', got '{referencePath}'", nameof(referencePath));
        }

        var segments = normalized.Split('/');
        if (segments.Length < 2)
        {
            throw new ArgumentException($"Reference path '{referencePath}' is invalid.", nameof(referencePath));
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..")
            {
                throw new ArgumentException($"Reference path '{referencePath}' contains invalid or traversal segments.", nameof(referencePath));
            }
            if (segment.Contains(".."))
            {
                throw new ArgumentException($"Reference component '{segment}' in path '{referencePath}' contains '..'.", nameof(referencePath));
            }
            if (segment.StartsWith('.') || segment.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Reference component '{segment}' in path '{referencePath}' is invalid.", nameof(referencePath));
            }
            if (segment.Any(c => char.IsControl(c) || c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '@'))
            {
                throw new ArgumentException($"Reference component '{segment}' in path '{referencePath}' contains invalid characters.", nameof(referencePath));
            }
        }

        return normalized;
    }
}
