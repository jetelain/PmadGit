using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Pmad.Git.HttpServer.Pack;
using Pmad.Git.HttpServer.Protocol;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer;

public sealed class GitSmartHttpService
{
    private readonly GitSmartHttpOptions _options;
    private readonly IGitRepositoryService _repositoryService;
    private readonly string _rootFullPath;
    private readonly GitPackBuilder _packBuilder = new();
    private readonly GitPackReader _packReader = new();

    public GitSmartHttpService(IOptions<GitSmartHttpOptions> options, IGitRepositoryService repositoryService)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
        
        if (string.IsNullOrWhiteSpace(_options.RepositoryRoot))
        {
            throw new ArgumentException("Repository root must be provided", nameof(options));
        }

        _rootFullPath = Path.GetFullPath(_options.RepositoryRoot);
    }

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

        var repositoryContext = await TryOpenRepositoryAsync(context, cancellationToken).ConfigureAwait(false);
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

    public async Task HandleUploadPackAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableUploadPack)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status403Forbidden, "Upload-pack disabled", cancellationToken).ConfigureAwait(false);
            return;
        }

        var repositoryContext = await TryOpenRepositoryAsync(context, cancellationToken).ConfigureAwait(false);
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

    public async Task HandleReceivePackAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableReceivePack)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status403Forbidden, "Receive-pack disabled", cancellationToken).ConfigureAwait(false);
            return;
        }

        var repositoryContext = await TryOpenRepositoryAsync(context, cancellationToken).ConfigureAwait(false);
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
        using (await repository.AcquireMultipleReferenceLocksAsync(referencePaths, cancellationToken).ConfigureAwait(false))
        {
            var refSnapshot = new Dictionary<string, GitHash>(await repository.GetReferencesAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
            var refStatuses = new List<RefStatus>(updates.Count);
            foreach (var update in updates)
            {
                var status = await ApplyReferenceUpdateInternalAsync(repository, refSnapshot, update, cancellationToken).ConfigureAwait(false);
                refStatuses.Add(status);
            }

            await WriteReceivePackStatusAsync(context, unpackStatus, refStatuses, capabilities.Contains("report-status"), cancellationToken).ConfigureAwait(false);
        }
        
        // Note: Cache invalidation for reference updates is handled by WriteReferenceWithValidationInternalAsync
        // which is called within ApplyReferenceUpdateInternalAsync for each update
    }

    private async Task<(GitRepository Repository, string Name)?> TryOpenRepositoryAsync(HttpContext context, CancellationToken cancellationToken)
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
            var allowed = await _options.AuthorizeAsync(context, repositoryName, cancellationToken).ConfigureAwait(false);
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

    private async Task AdvertiseReferencesAsync(GitRepository repository, GitServiceKind service, Stream destination, CancellationToken cancellationToken)
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

    private static async Task<HeadInfo> ReadHeadInfoAsync(GitRepository repository, CancellationToken cancellationToken)
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

    private bool IsServiceEnabled(GitServiceKind service) => service switch
    {
        GitServiceKind.UploadPack => _options.EnableUploadPack,
        GitServiceKind.ReceivePack => _options.EnableReceivePack,
        _ => false
    };

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

    private async Task<RefStatus> ApplyReferenceUpdateInternalAsync(
        GitRepository repository,
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
            // Use internal method that doesn't acquire locks (locks are already held by caller)
            await repository.WriteReferenceWithValidationInternalAsync(
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

    private static string NormalizeReferencePath(string name)
    {
        var trimmed = name.Replace('\\', '/').Trim();
        if (!trimmed.StartsWith("refs/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("References must reside under refs/");
        }

        return trimmed;
    }

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

    private static async Task WritePlainErrorAsync(HttpContext context, int statusCode, string message, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private static string SanitizeMessage(string message)
        => message.Replace('\n', ' ').Replace('\r', ' ');

    private enum GitServiceKind
    {
        UploadPack,
        ReceivePack
    }

    private sealed record ReferenceLine(string Name, GitHash Hash);

    private sealed record HeadInfo(GitHash? Hash, string? SymrefTarget);

    private sealed record RefUpdate(GitHash? OldValue, GitHash? NewValue, string Name);

    private sealed record RefStatus(string ReferenceName, bool Success, string Message)
    {
        public static RefStatus Ok(string name) => new(name, true, string.Empty);
        public static RefStatus Error(string name, string message) => new(name, false, message);
    }
}
