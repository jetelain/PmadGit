using System.Threading;

namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Provides read and write access to git references stored on disk,
/// supporting both loose ref files and packed-refs.
/// </summary>
internal sealed class GitReferenceStore : IGitReferenceStore
{
    private readonly string _gitDirectory;
    private readonly GitRepositoryLockManager _lockManager = new();
    private Lazy<Task<Dictionary<string, GitHash>>> _cache;

    public GitReferenceStore(string gitDirectory)
    {
        _gitDirectory = gitDirectory;
        _cache = CreateCache();
    }

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
    public async Task<GitHash?> TryResolveReferenceAsync(string referencePath, CancellationToken cancellationToken = default)
    {
        var normalized = referencePath.Replace('\\', '/');
        var refs = await _cache.Value.ConfigureAwait(false);
        if (refs.TryGetValue(normalized, out var hash))
        {
            return hash;
        }

        var filePath = Path.Combine(_gitDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
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
            await WriteReferenceAsync(normalized, newValue.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DeleteReference(normalized, cancellationToken);
        }

        Interlocked.Exchange(ref _cache, CreateCache());
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
        var directory = Path.GetDirectoryName(refPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempDirectory = directory ?? _gitDirectory;
        var tempPath = Path.Combine(tempDirectory, $"{Path.GetFileName(refPath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, hash.Value + "\n", cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, refPath, overwrite: true);
    }

    private void DeleteReference(string normalizedReferencePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var refPath = Path.Combine(_gitDirectory, normalizedReferencePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refPath))
        {
            File.Delete(refPath);
        }
    }

    private async Task<Dictionary<string, GitHash>> LoadReferencesAsync()
    {
        var refs = new Dictionary<string, GitHash>(StringComparer.Ordinal);
        var refsRoot = Path.Combine(_gitDirectory, "refs");
        if (Directory.Exists(refsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(refsRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(_gitDirectory, file).Replace('\\', '/');
                var content = (await File.ReadAllTextAsync(file).ConfigureAwait(false)).Trim();
                if (GitHash.TryParse(content, out var hash))
                {
                    refs[relative] = hash;
                }
            }
        }

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

        return refs;
    }

    private Lazy<Task<Dictionary<string, GitHash>>> CreateCache()
        => new(LoadReferencesAsync, LazyThreadSafetyMode.ExecutionAndPublication);

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

        return normalized;
    }
}
