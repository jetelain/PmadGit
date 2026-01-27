using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Pmad.Git.HttpServer.Pack;
using Pmad.Git.HttpServer.Protocol;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Implements the Git Smart HTTP protocol for serving Git repositories over HTTP.
/// Handles info/refs, upload-pack (fetch/clone), and receive-pack (push) operations.
/// </summary>
public sealed class GitSmartHttpService
{
    private readonly GitSmartHttpOptions _options;
    private readonly IGitRepositoryService _repositoryService;
    private readonly string _rootFullPath;
    private readonly GitPackBuilder _packBuilder = new();
    private readonly GitPackReader _packReader = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitSmartHttpService"/> class.
    /// </summary>
    /// <param name="options">The Git Smart HTTP options.</param>
    /// <param name="repositoryService">The repository service for accessing Git repositories.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="repositoryService"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when options value is null.</exception>
    /// <exception cref="ArgumentException">Thrown when repository root is not provided.</exception>
    public GitSmartHttpService(IOptions<GitSmartHttpOptions> options, IGitRepositoryService repositoryService)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _options = options.Value ?? throw new InvalidOperationException("IOptions<GitSmartHttpOptions>.Value must not be null.");
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));

        if (string.IsNullOrWhiteSpace(_options.RepositoryRoot))
        {
            throw new ArgumentException("Repository root must be provided", nameof(options));
        }

        _rootFullPath = Path.GetFullPath(_options.RepositoryRoot);
    }

    /// <summary>
    /// Handles the info/refs request which advertises available references to the Git client.
    /// This is the initial discovery phase of the Git Smart HTTP protocol.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleInfoRefsAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!TryGetServiceName(context.Request, out var serviceName))
        {
            await WritePlainErrorAsync(context, StatusCodes.Status400BadRequest, "Missing service parameter", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryParseService(serviceName, out var service))
        {
            await WritePlainErrorAsync(context, StatusCodes.Status400BadRequest, "Unsupported service", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IsServiceEnabled(service))
        {
            await WritePlainErrorAsync(context, StatusCodes.Status403Forbidden, "Service disabled", cancellationToken).ConfigureAwait(false);
            return;
        }

        var operation = service == GitServiceKind.UploadPack ? GitOperation.Read : GitOperation.Write;
        var repositoryContext = await TryOpenRepositoryAsync(context, operation, cancellationToken).ConfigureAwait(false);
        if (repositoryContext is null)
        {
            return;
        }

        var (repository, _) = repositoryContext.Value;

        // Invalidate caches to ensure we see latest refs from external changes
        repository.InvalidateCaches();

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentType = $"application/x-{serviceName}-advertisement";

        await PktLineWriter.WriteStringAsync(context.Response.Body, $"# service={serviceName}\n", cancellationToken).ConfigureAwait(false);
        await PktLineWriter.WriteFlushAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);

        await AdvertiseReferencesAsync(repository, service, context.Response.Body, cancellationToken).ConfigureAwait(false);
        await PktLineWriter.WriteFlushAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles the upload-pack request which allows clients to fetch/clone repository data.
    /// This operation reads objects from the repository and sends them to the client.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleUploadPackAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableUploadPack)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status403Forbidden, "Upload-pack disabled", cancellationToken).ConfigureAwait(false);
            return;
        }

        var repositoryContext = await TryOpenRepositoryAsync(context, GitOperation.Read, cancellationToken).ConfigureAwait(false);
        if (repositoryContext is null)
        {
            return;
        }

        var (repository, _) = repositoryContext.Value;
        var wants = await ParseUploadPackRequestAsync(context.Request.Body, repository.HashLengthBytes, cancellationToken).ConfigureAwait(false);
        if (wants.Count == 0)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status400BadRequest, "No want commands provided", cancellationToken).ConfigureAwait(false);
            return;
        }

        var walker = new GitObjectWalker(repository);
        var objectClosure = await walker.CollectAsync(wants, cancellationToken).ConfigureAwait(false);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentType = "application/x-git-upload-pack-result";

        await PktLineWriter.WriteStringAsync(context.Response.Body, "NAK\n", cancellationToken).ConfigureAwait(false);
        await _packBuilder.WriteAsync(repository, objectClosure, context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles the receive-pack request which allows clients to push data to the repository.
    /// This operation receives objects from the client and updates references.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleReceivePackAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableReceivePack)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status403Forbidden, "Receive-pack disabled", cancellationToken).ConfigureAwait(false);
            return;
        }

        var repositoryContext = await TryOpenRepositoryAsync(context, GitOperation.Write, cancellationToken).ConfigureAwait(false);
        if (repositoryContext is null)
        {
            return;
        }

        var (repository, repositoryName) = repositoryContext.Value;
        var expectedHashLength = repository.HashLengthBytes * 2;
        var (updates, capabilities) = await ParseReceivePackCommandsAsync(context.Request.Body, expectedHashLength, cancellationToken).ConfigureAwait(false);

        var packNeeded = updates.Any(update => update.NewValue.HasValue);
        string unpackStatus;
        if (packNeeded)
        {
            try
            {
                await _packReader.ReadAsync(repository, context.Request.Body, cancellationToken).ConfigureAwait(false);
                unpackStatus = "unpack ok";

                // Invalidate object caches after receiving new objects
                // Reference cache will be invalidated after all reference updates
                repository.InvalidateCaches();
            }
            catch (Exception ex)
            {
                unpackStatus = $"unpack error {SanitizeMessage(ex.Message)}";
                await WriteReceivePackStatusAsync(context, unpackStatus, Array.Empty<RefStatus>(), capabilities.Contains("report-status"), cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        else
        {
            unpackStatus = "unpack ok";
        }

        // Acquire locks for all affected references to prevent concurrent modifications
        // Locks are acquired in sorted order to prevent deadlocks
        var referencePaths = updates.Select(u => NormalizeReferencePath(u.Name)).ToList();
        List<RefStatus> refStatuses;
        using (var locks = await repository.AcquireMultipleReferenceLocksAsync(referencePaths, cancellationToken).ConfigureAwait(false))
        {
            var refSnapshot = new Dictionary<string, GitHash>(await repository.GetReferencesAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
            refStatuses = new List<RefStatus>(updates.Count);
            foreach (var update in updates)
            {
                var status = await ApplyReferenceUpdateInternalAsync(locks, refSnapshot, update, cancellationToken).ConfigureAwait(false);
                refStatuses.Add(status);
            }

            await WriteReceivePackStatusAsync(context, unpackStatus, refStatuses, capabilities.Contains("report-status"), cancellationToken).ConfigureAwait(false);
        }

        // Note: Cache invalidation for reference updates is handled by WriteReferenceWithValidationAsync
        // which is called within ApplyReferenceUpdateInternalAsync for each update

        // Fire-and-forget the callback after the response is written to avoid impacting the Git protocol response.
        // If the callback throws or is slow, it won't cause the push to appear to fail to the client.
        if (_options.OnReceivePackCompleted is not null)
        {
            var successfulUpdates = refStatuses
                .Where(static s => s.Success)
                .Select(static s => s.ReferenceName)
                .ToList();

            if (successfulUpdates.Count > 0)
            {
                // Execute callback in background without awaiting (fire and forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _options.OnReceivePackCompleted(context, repositoryName, successfulUpdates).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Swallow exceptions to prevent unobserved task exceptions
                        // Host application should handle logging within the callback if needed
                    }
                });
            }
        }
    }

    /// <summary>
    /// Attempts to open a repository from the HTTP context, performing authorization checks.
    /// </summary>
    /// <param name="context">The HTTP context containing repository information.</param>
    /// <param name="operation">The type of operation being performed (Read or Write).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tuple containing the repository and its name, or null if the repository cannot be accessed.</returns>
    private async Task<(IGitRepository Repository, string Name)?> TryOpenRepositoryAsync(HttpContext context, GitOperation operation, CancellationToken cancellationToken)
    {
        string? rawValue = null;
        if (_options.RepositoryResolver is not null)
        {
            rawValue = _options.RepositoryResolver(context);
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            await WritePlainErrorAsync(context, StatusCodes.Status404NotFound, "Repository not found", cancellationToken).ConfigureAwait(false);
            return null;
        }

        string repositoryName;
        try
        {
            repositoryName = NormalizeRepositoryName(rawValue!);
        }
        catch
        {
            await WritePlainErrorAsync(context, StatusCodes.Status400BadRequest, "Invalid repository name", cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (_options.AuthorizeAsync is not null)
        {
            var allowed = await _options.AuthorizeAsync(context, repositoryName, operation, cancellationToken).ConfigureAwait(false);
            if (!allowed)
            {
                await WritePlainErrorAsync(context, StatusCodes.Status403Forbidden, "Access denied", cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        try
        {
            var repositoryPath = ResolveRepositoryPath(repositoryName);
            var repository = _repositoryService.GetRepository(repositoryPath);
            return (repository, repositoryName);
        }
        catch
        {
            await WritePlainErrorAsync(context, StatusCodes.Status404NotFound, "Repository not found", cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Resolves the file system path for a repository name, checking for both direct and .git suffixed paths.
    /// </summary>
    /// <param name="repositoryName">The normalized repository name.</param>
    /// <returns>The full file system path to the repository.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository cannot be found.</exception>
    private string ResolveRepositoryPath(string repositoryName)
    {
        var candidates = new[]
        {
            Path.Combine(_options.RepositoryRoot, repositoryName),
            Path.Combine(_options.RepositoryRoot, repositoryName + ".git")
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (!full.StartsWith(_rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(full))
            {
                return full;
            }
        }

        throw new DirectoryNotFoundException();
    }

    /// <summary>
    /// Normalizes a repository name by removing slashes, .git suffix, and applying custom normalization.
    /// </summary>
    /// <param name="raw">The raw repository name from the HTTP context.</param>
    /// <returns>The normalized repository name.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the repository name is invalid or contains path traversal attempts.</exception>
    private string NormalizeRepositoryName(string raw)
    {
        var value = raw.Replace('\\', '/').Trim('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        if (string.IsNullOrEmpty(value) || value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid repository name");
        }

        var normalized = _options.RepositoryNameNormalizer?.Invoke(value) ?? value;
        return normalized;
    }

    /// <summary>
    /// Advertises available references (branches, tags) to the Git client in the info/refs response.
    /// </summary>
    /// <param name="repository">The repository to advertise references from.</param>
    /// <param name="service">The type of service being advertised (upload-pack or receive-pack).</param>
    /// <param name="destination">The stream to write the advertisement to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AdvertiseReferencesAsync(IGitRepository repository, GitServiceKind service, Stream destination, CancellationToken cancellationToken)
    {
        var references = await repository.GetReferencesAsync(cancellationToken).ConfigureAwait(false);
        var headInfo = await ReadHeadInfoAsync(repository, cancellationToken).ConfigureAwait(false);
        var entries = new List<ReferenceLine>();

        if (headInfo.Hash.HasValue)
        {
            entries.Add(new ReferenceLine("HEAD", headInfo.Hash.Value));
        }

        foreach (var entry in references.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            entries.Add(new ReferenceLine(entry.Key, entry.Value));
        }

        var capabilities = BuildCapabilities(service, headInfo.SymrefTarget);
        if (entries.Count == 0)
        {
            var emptyLine = $"0000000000000000000000000000000000000000 capabilities^{{}}\0{capabilities}\n";
            await PktLineWriter.WriteStringAsync(destination, emptyLine, cancellationToken).ConfigureAwait(false);
            return;
        }

        var builder = new StringBuilder();
        var first = true;
        foreach (var entry in entries)
        {
            builder.Clear();
            builder.Append(entry.Hash.Value).Append(' ').Append(entry.Name);
            if (first)
            {
                builder.Append('\0').Append(capabilities);
                first = false;
            }

            builder.Append('\n');
            await PktLineWriter.WriteStringAsync(destination, builder.ToString(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads HEAD information from the repository, including symbolic references and direct hash references.
    /// </summary>
    /// <param name="repository">The repository to read HEAD from.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Information about the HEAD reference.</returns>
    private static async Task<HeadInfo> ReadHeadInfoAsync(IGitRepository repository, CancellationToken cancellationToken)
    {
        var headPath = Path.Combine(repository.GitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            return new HeadInfo(null, null);
        }

        var content = (await File.ReadAllTextAsync(headPath, cancellationToken).ConfigureAwait(false)).Trim();
        if (content.StartsWith("ref: ", StringComparison.Ordinal))
        {
            var target = content[5..].Trim();
            var refs = await repository.GetReferencesAsync(cancellationToken).ConfigureAwait(false);
            if (refs.TryGetValue(target, out var hash))
            {
                return new HeadInfo(hash, target);
            }

            return new HeadInfo(null, target);
        }

        if (GitHash.TryParse(content, out var direct))
        {
            return new HeadInfo(direct, null);
        }

        return new HeadInfo(null, null);
    }

    /// <summary>
    /// Attempts to extract the service name from the HTTP request query string.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="value">The service name if found.</param>
    /// <returns>True if the service name was found; otherwise, false.</returns>
    private static bool TryGetServiceName(HttpRequest request, out string value)
    {
        if (request.Query.TryGetValue("service", out StringValues serviceValues))
        {
            value = serviceValues.ToString();
            return !string.IsNullOrEmpty(value);
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to parse a service name string into a <see cref="GitServiceKind"/> enum value.
    /// </summary>
    /// <param name="serviceName">The service name to parse (e.g., "git-upload-pack" or "git-receive-pack").</param>
    /// <param name="service">The parsed service kind if successful.</param>
    /// <returns>True if the service name was recognized; otherwise, false.</returns>
    private static bool TryParseService(string serviceName, out GitServiceKind service)
    {
        if (serviceName.Equals("git-upload-pack", StringComparison.Ordinal))
        {
            service = GitServiceKind.UploadPack;
            return true;
        }

        if (serviceName.Equals("git-receive-pack", StringComparison.Ordinal))
        {
            service = GitServiceKind.ReceivePack;
            return true;
        }

        service = default;
        return false;
    }

    /// <summary>
    /// Checks if a specific Git service is enabled based on the current options.
    /// </summary>
    /// <param name="service">The service to check.</param>
    /// <returns>True if the service is enabled; otherwise, false.</returns>
    private bool IsServiceEnabled(GitServiceKind service) => service switch
    {
        GitServiceKind.UploadPack => _options.EnableUploadPack,
        GitServiceKind.ReceivePack => _options.EnableReceivePack,
        _ => false
    };

    /// <summary>
    /// Builds the capabilities string to advertise to the Git client.
    /// </summary>
    /// <param name="service">The type of service being advertised.</param>
    /// <param name="headSymref">The symbolic reference target of HEAD, if any.</param>
    /// <returns>A space-separated string of capabilities.</returns>
    private string BuildCapabilities(GitServiceKind service, string? headSymref)
    {
        var capabilities = new List<string>();
        if (!string.IsNullOrEmpty(headSymref))
        {
            capabilities.Add($"symref=HEAD:{headSymref}");
        }

        capabilities.Add($"agent={_options.Agent}");

        if (service == GitServiceKind.ReceivePack)
        {
            capabilities.Add("report-status");
            capabilities.Add("delete-refs");
        }

        return string.Join(' ', capabilities);
    }

    /// <summary>
    /// Parses an upload-pack request body to extract the list of wanted objects.
    /// </summary>
    /// <param name="body">The request body stream.</param>
    /// <param name="hashLengthBytes">The expected length of hash values in bytes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of wanted object hashes.</returns>
    private static async Task<List<GitHash>> ParseUploadPackRequestAsync(Stream body, int hashLengthBytes, CancellationToken cancellationToken)
    {
        var expectedLength = hashLengthBytes * 2;
        var reader = new PktLineReader(body);
        var wants = new List<GitHash>();
        var readingWants = true;

        while (true)
        {
            var packet = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
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

            if (packet.Value.IsDelimiter)
            {
                readingWants = false;
                continue;
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
                    // Handle space-separated capabilities (modern git)
                    var spaceIndex = hashPart.IndexOf(' ');
                    if (spaceIndex >= 0)
                    {
                        hashPart = hashPart[..spaceIndex];
                    }
                }

                if (hashPart.Length == expectedLength && GitHash.TryParse(hashPart, out var hash))
                {
                    wants.Add(hash);
                }
            }
            else if (text.Equals("done", StringComparison.Ordinal))
            {
                break;
            }
        }

        return wants;
    }

    /// <summary>
    /// Parses a receive-pack request body to extract reference updates and client capabilities.
    /// </summary>
    /// <param name="body">The request body stream.</param>
    /// <param name="expectedHashLength">The expected length of hash values in characters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tuple containing the list of reference updates and the set of client capabilities.</returns>
    private static async Task<(List<RefUpdate> Updates, HashSet<string> Capabilities)> ParseReceivePackCommandsAsync(Stream body, int expectedHashLength, CancellationToken cancellationToken)
    {
        var reader = new PktLineReader(body);
        var updates = new List<RefUpdate>();
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var first = true;

        while (true)
        {
            var packet = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (packet is null)
            {
                break;
            }

            if (packet.Value.IsFlush)
            {
                break;
            }

            var text = packet.Value.AsString().TrimEnd('\n', '\r');
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (first)
            {
                var zeroIndex = text.IndexOf('\0');
                if (zeroIndex >= 0)
                {
                    var caps = text[(zeroIndex + 1)..];
                    foreach (var capability in caps.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        capabilities.Add(capability);
                    }

                    text = text[..zeroIndex];
                }

                first = false;
            }

            var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                continue;
            }

            if (!TryParseCommandHash(parts[0], expectedHashLength, out var oldValue) ||
                !TryParseCommandHash(parts[1], expectedHashLength, out var newValue))
            {
                continue;
            }

            updates.Add(new RefUpdate(oldValue, newValue, parts[2]));
        }

        return (updates, capabilities);
    }

    /// <summary>
    /// Attempts to parse a hash string from a receive-pack command, handling zero hashes specially.
    /// </summary>
    /// <param name="value">The hash string to parse.</param>
    /// <param name="expectedLength">The expected length of the hash string.</param>
    /// <param name="hash">The parsed hash value, or null for zero hashes or deletions.</param>
    /// <returns>True if the hash was parsed successfully; otherwise, false.</returns>
    private static bool TryParseCommandHash(string value, int expectedLength, out GitHash? hash)
    {
        if (value.Length != expectedLength)
        {
            hash = null;
            return false;
        }

        if (IsZeroHash(value))
        {
            hash = null;
            return true;
        }

        if (GitHash.TryParse(value, out var parsed))
        {
            hash = parsed;
            return true;
        }

        hash = null;
        return false;
    }

    /// <summary>
    /// Checks if a hash string consists entirely of zeros.
    /// </summary>
    /// <param name="value">The hash string to check.</param>
    /// <returns>True if the hash is all zeros; otherwise, false.</returns>
    private static bool IsZeroHash(string value)
    {
        foreach (var c in value)
        {
            if (c != '0')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies a single reference update within a locked context, validating expected old values.
    /// </summary>
    /// <param name="locks">The locks for the references being updated.</param>
    /// <param name="snapshot">A snapshot of the current reference state.</param>
    /// <param name="update">The update to apply.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The status of the reference update operation.</returns>
    private async Task<RefStatus> ApplyReferenceUpdateInternalAsync(
        IGitMultipleReferenceLocks locks,
        IDictionary<string, GitHash> snapshot,
        RefUpdate update,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeReferencePath(update.Name);
        snapshot.TryGetValue(normalized, out var currentValue);

        if (update.OldValue.HasValue)
        {
            if (!currentValue.Equals(update.OldValue.Value))
            {
                return RefStatus.Error(update.Name, "non-fast-forward");
            }
        }
        else if (snapshot.ContainsKey(normalized))
        {
            return RefStatus.Error(update.Name, "reference exists");
        }

        try
        {
            await locks.WriteReferenceWithValidationAsync(
                normalized,
                update.OldValue,
                update.NewValue,
                cancellationToken).ConfigureAwait(false);

            if (update.NewValue.HasValue)
            {
                snapshot[normalized] = update.NewValue.Value;
            }
            else
            {
                snapshot.Remove(normalized);
            }
        }
        catch (Exception ex)
        {
            return RefStatus.Error(update.Name, SanitizeMessage(ex.Message));
        }

        return RefStatus.Ok(update.Name);
    }

    /// <summary>
    /// Normalizes a reference path, ensuring it starts with "refs/".
    /// </summary>
    /// <param name="name">The reference name to normalize.</param>
    /// <returns>The normalized reference path.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the reference does not start with "refs/".</exception>
    private static string NormalizeReferencePath(string name)
    {
        var trimmed = name.Replace('\\', '/').Trim();
        if (!trimmed.StartsWith("refs/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("References must reside under refs/");
        }

        return trimmed;
    }

    /// <summary>
    /// Writes the receive-pack status response to the client, including unpack status and reference update results.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="unpackStatus">The status of the pack unpacking operation.</param>
    /// <param name="refStatuses">The status of each reference update.</param>
    /// <param name="includeDetails">Whether to include detailed status for each reference.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task WriteReceivePackStatusAsync(
        HttpContext context,
        string unpackStatus,
        IReadOnlyList<RefStatus> refStatuses,
        bool includeDetails,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentType = "application/x-git-receive-pack-result";

        await PktLineWriter.WriteStringAsync(context.Response.Body, unpackStatus + "\n", cancellationToken).ConfigureAwait(false);

        if (includeDetails)
        {
            foreach (var status in refStatuses)
            {
                var line = status.Success
                    ? $"ok {status.ReferenceName}\n"
                    : $"ng {status.ReferenceName} {status.Message}\n";
                await PktLineWriter.WriteStringAsync(context.Response.Body, line, cancellationToken).ConfigureAwait(false);
            }
        }

        await PktLineWriter.WriteFlushAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a plain text error response to the client.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="statusCode">The HTTP status code to return.</param>
    /// <param name="message">The error message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task WritePlainErrorAsync(HttpContext context, int statusCode, string message, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sanitizes an error message by removing newline characters.
    /// </summary>
    /// <param name="message">The message to sanitize.</param>
    /// <returns>The sanitized message.</returns>
    private static string SanitizeMessage(string message)
        => message.Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>
    /// Represents the type of Git service being requested.
    /// </summary>
    private enum GitServiceKind
    {
        /// <summary>
        /// Upload-pack service for fetch/clone operations.
        /// </summary>
        UploadPack,
        
        /// <summary>
        /// Receive-pack service for push operations.
        /// </summary>
        ReceivePack
    }

    /// <summary>
    /// Represents a reference advertisement line containing a name and hash.
    /// </summary>
    /// <param name="Name">The reference name (e.g., "refs/heads/main").</param>
    /// <param name="Hash">The hash value the reference points to.</param>
    private sealed record ReferenceLine(string Name, GitHash Hash);

    /// <summary>
    /// Represents information about the HEAD reference.
    /// </summary>
    /// <param name="Hash">The hash that HEAD points to, or null if HEAD is unresolved.</param>
    /// <param name="SymrefTarget">The symbolic reference target (e.g., "refs/heads/main"), or null if HEAD is a direct reference.</param>
    private sealed record HeadInfo(GitHash? Hash, string? SymrefTarget);

    /// <summary>
    /// Represents a reference update command from a push operation.
    /// </summary>
    /// <param name="OldValue">The expected old value of the reference, or null for creation.</param>
    /// <param name="NewValue">The new value for the reference, or null for deletion.</param>
    /// <param name="Name">The name of the reference being updated.</param>
    private sealed record RefUpdate(GitHash? OldValue, GitHash? NewValue, string Name);

    /// <summary>
    /// Represents the status of a reference update operation.
    /// </summary>
    /// <param name="ReferenceName">The name of the reference that was updated.</param>
    /// <param name="Success">True if the update succeeded; otherwise, false.</param>
    /// <param name="Message">An error message if the update failed; otherwise, empty.</param>
    private sealed record RefStatus(string ReferenceName, bool Success, string Message)
    {
        /// <summary>
        /// Creates a successful reference status.
        /// </summary>
        /// <param name="name">The reference name.</param>
        /// <returns>A successful status.</returns>
        public static RefStatus Ok(string name) => new(name, true, string.Empty);
        
        /// <summary>
        /// Creates a failed reference status with an error message.
        /// </summary>
        /// <param name="name">The reference name.</param>
        /// <param name="message">The error message.</param>
        /// <returns>A failed status.</returns>
        public static RefStatus Error(string name, string message) => new(name, false, message);
    }
}
