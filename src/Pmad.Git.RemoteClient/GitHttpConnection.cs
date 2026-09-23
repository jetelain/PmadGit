using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Pmad.Git.LocalRepositories;
using Pmad.Git.Protocol;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Handles Git Smart HTTP protocol communication with a remote Git server.
/// </summary>
public sealed class GitHttpConnection : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly GitRemoteClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHttpConnection"/> class.
    /// </summary>
    /// <param name="options">Optional client configuration options.</param>
    public GitHttpConnection(GitRemoteClientOptions? options = null)
    {
        _options = options ?? new GitRemoteClientOptions();
        if (_options.HttpClient is not null)
        {
            _httpClient = _options.HttpClient;
            _disposeHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _disposeHttpClient = true;
        }
    }

    /// <summary>
    /// Discovers references and capabilities advertised by the remote Git repository for the specified service.
    /// </summary>
    /// <param name="remoteUrl">The remote repository URL.</param>
    /// <param name="service">The Git service name (<c>git-upload-pack</c> or <c>git-receive-pack</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="GitRemoteAdvertisement"/> containing advertised references and capabilities.</returns>
    public async Task<GitRemoteAdvertisement> DiscoverReferencesAsync(
        Uri remoteUrl,
        string service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var baseUrl = remoteUrl.ToString().TrimEnd('/');
        var requestUrl = $"{baseUrl}/info/refs?service={service}";

        using var timeoutCts = new CancellationTokenSource(_options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            ApplyRequestHeaders(request);

            using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveToken).ConfigureAwait(false);
            EnsureSuccessStatusCode(response);
            ValidateContentType(response, $"application/x-{service}-advertisement");

            await using var responseStream = await response.Content.ReadAsStreamAsync(effectiveToken).ConfigureAwait(false);
            return await ParseAdvertisementAsync(responseStream, service, effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Git HTTP request to '{requestUrl}' timed out after {_options.Timeout}.");
        }
    }

    /// <summary>
    /// Sends an upload-pack request to fetch/clone objects and returns a readable packfile stream.
    /// </summary>
    /// <param name="remoteUrl">The remote repository URL.</param>
    /// <param name="wants">The list of wanted object hashes.</param>
    /// <param name="haves">The list of local object hashes already present.</param>
    /// <param name="advertisement">The previously discovered remote advertisement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An upload-pack response containing the packfile stream.</returns>
    public async Task<GitUploadPackResponse> UploadPackAsync(
        Uri remoteUrl,
        IReadOnlyList<GitHash> wants,
        IReadOnlyList<GitHash> haves,
        GitRemoteAdvertisement advertisement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);
        ArgumentNullException.ThrowIfNull(wants);
        ArgumentNullException.ThrowIfNull(haves);
        ArgumentNullException.ThrowIfNull(advertisement);

        if (wants.Count == 0)
        {
            throw new ArgumentException("At least one want hash must be specified.", nameof(wants));
        }

        var baseUrl = remoteUrl.ToString().TrimEnd('/');
        var requestUrl = $"{baseUrl}/git-upload-pack";

        var timeoutCts = new CancellationTokenSource(_options.Timeout);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        var requestBodyStream = new MemoryStream();
        try
        {
            var negotiatedSideband = false;

            // Negotiate client capabilities to include in first want line
            var clientCapabilities = new List<string>();
            if (advertisement.Capabilities.Contains("multi_ack"))
            {
                clientCapabilities.Add("multi_ack");
            }
            if (advertisement.Capabilities.Contains("side-band-64k"))
            {
                clientCapabilities.Add("side-band-64k");
                negotiatedSideband = true;
            }
            else if (advertisement.Capabilities.Contains("side-band"))
            {
                clientCapabilities.Add("side-band");
                negotiatedSideband = true;
            }
            if (advertisement.Capabilities.Contains("ofs-delta"))
            {
                clientCapabilities.Add("ofs-delta");
            }
            if (advertisement.Capabilities.Contains("thin-pack"))
            {
                clientCapabilities.Add("thin-pack");
            }
            if (!string.IsNullOrEmpty(_options.Agent))
            {
                clientCapabilities.Add($"agent={_options.Agent}");
            }
            if (advertisement.Capabilities.Contains("object-format=sha256"))
            {
                clientCapabilities.Add("object-format=sha256");
            }

            var capString = clientCapabilities.Count > 0 ? " " + string.Join(' ', clientCapabilities) : string.Empty;

            // Write wants
            for (var i = 0; i < wants.Count; i++)
            {
                var line = i == 0
                    ? $"want {wants[i].Value}{capString}\n"
                    : $"want {wants[i].Value}\n";
                await PktLineWriter.WriteStringAsync(requestBodyStream, line, effectiveToken).ConfigureAwait(false);
            }
            await PktLineWriter.WriteFlushAsync(requestBodyStream, effectiveToken).ConfigureAwait(false);

            // Write haves
            foreach (var have in haves)
            {
                await PktLineWriter.WriteStringAsync(requestBodyStream, $"have {have.Value}\n", effectiveToken).ConfigureAwait(false);
            }
            await PktLineWriter.WriteStringAsync(requestBodyStream, "done\n", effectiveToken).ConfigureAwait(false);

            requestBodyStream.Seek(0, SeekOrigin.Begin);

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            ApplyRequestHeaders(request);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-git-upload-pack-result"));
            request.Content = new StreamContent(requestBodyStream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-git-upload-pack-request");

            var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveToken).ConfigureAwait(false);
            try
            {
                EnsureSuccessStatusCode(response);
                ValidateContentType(response, "application/x-git-upload-pack-result");

                var responseStream = await response.Content.ReadAsStreamAsync(effectiveToken).ConfigureAwait(false);
                var pktReader = new PktLineReader(responseStream);

                // Read ACK/NAK lines
                while (true)
                {
                    var packet = await pktReader.ReadAsync(effectiveToken).ConfigureAwait(false);
                    if (packet is null || packet.Value.IsFlush)
                    {
                        break;
                    }

                    var line = packet.Value.AsString().TrimEnd('\r', '\n');
                    if (line.StartsWith("ERR ", StringComparison.Ordinal))
                    {
                        throw new GitRemoteException($"Server returned error: {line[4..]}");
                    }
                    if (line.Equals("NAK", StringComparison.Ordinal))
                    {
                        break;
                    }
                    if (line.StartsWith("ACK", StringComparison.Ordinal))
                    {
                        // Intermediate ACKs end with "continue" or "common"
                        if (line.EndsWith(" continue", StringComparison.Ordinal) ||
                            line.EndsWith(" common", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Terminal ACK
                        break;
                    }
                }

                Stream packStream;
                if (negotiatedSideband)
                {
                    packStream = new GitSidebandStream(responseStream, _options.OnProgress);
                }
                else
                {
                    packStream = responseStream;
                }

                var timeoutProtectedStream = new TimeoutStream(packStream, timeoutCts, linkedCts, _options.Timeout);
                return new GitUploadPackResponse(timeoutProtectedStream, response, requestBodyStream);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            linkedCts.Dispose();
            timeoutCts.Dispose();
            requestBodyStream.Dispose();
            throw new TimeoutException($"Git HTTP upload-pack request to '{requestUrl}' timed out after {_options.Timeout}.");
        }
        catch
        {
            linkedCts.Dispose();
            timeoutCts.Dispose();
            requestBodyStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends a receive-pack request to push reference updates and optional packfile payload to the remote server.
    /// </summary>
    /// <param name="remoteUrl">The remote repository URL.</param>
    /// <param name="commands">The reference update commands.</param>
    /// <param name="packDataStream">Optional stream containing packfile payload (or <see langword="null"/> if only deletions).</param>
    /// <param name="advertisement">The previously discovered remote advertisement.</param>
    /// <param name="hashLengthBytes">Length of object hashes in bytes (20 for SHA-1, 32 for SHA-256).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReceivePackAsync(
        Uri remoteUrl,
        IReadOnlyList<GitRefUpdateCommand> commands,
        Stream? packDataStream,
        GitRemoteAdvertisement advertisement,
        int hashLengthBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(advertisement);

        if (commands.Count == 0)
        {
            return;
        }

        if (!advertisement.Capabilities.Contains("report-status") && !advertisement.Capabilities.Contains("report-status-v2"))
        {
            throw new GitRemoteException("Remote repository does not support 'report-status' capability required for push verification.");
        }

        var baseUrl = remoteUrl.ToString().TrimEnd('/');
        var requestUrl = $"{baseUrl}/git-receive-pack";
        var zeroHash = new string('0', hashLengthBytes * 2);

        using var timeoutCts = new CancellationTokenSource(_options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        try
        {
            using var requestPayload = new MemoryStream();

            // Write command pkt-lines
            for (var i = 0; i < commands.Count; i++)
            {
                var cmd = commands[i];
                var oldSha = cmd.OldValue?.Value ?? zeroHash;
                var newSha = cmd.NewValue?.Value ?? zeroHash;

                if (i == 0)
                {
                    var caps = new List<string>();
                    if (advertisement.Capabilities.Contains("report-status"))
                    {
                        caps.Add("report-status");
                    }
                    if (advertisement.Capabilities.Contains("delete-refs"))
                    {
                        caps.Add("delete-refs");
                    }
                    if (!string.IsNullOrEmpty(_options.Agent))
                    {
                        caps.Add($"agent={_options.Agent}");
                    }
                    if (advertisement.Capabilities.Contains("object-format=sha256"))
                    {
                        caps.Add("object-format=sha256");
                    }

                    var capString = caps.Count > 0 ? "\0" + string.Join(' ', caps) : string.Empty;
                    var line = $"{oldSha} {newSha} {cmd.RefName}{capString}\n";
                    await PktLineWriter.WriteStringAsync(requestPayload, line, effectiveToken).ConfigureAwait(false);
                }
                else
                {
                    var line = $"{oldSha} {newSha} {cmd.RefName}\n";
                    await PktLineWriter.WriteStringAsync(requestPayload, line, effectiveToken).ConfigureAwait(false);
                }
            }

            await PktLineWriter.WriteFlushAsync(requestPayload, effectiveToken).ConfigureAwait(false);

            // Append packfile if present
            if (packDataStream != null)
            {
                if (packDataStream.CanSeek)
                {
                    packDataStream.Seek(0, SeekOrigin.Begin);
                }
                await packDataStream.CopyToAsync(requestPayload, effectiveToken).ConfigureAwait(false);
            }

            requestPayload.Seek(0, SeekOrigin.Begin);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            ApplyRequestHeaders(request);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-git-receive-pack-result"));
            request.Content = new StreamContent(requestPayload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-git-receive-pack-request");

            using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveToken).ConfigureAwait(false);
            EnsureSuccessStatusCode(response);
            ValidateContentType(response, "application/x-git-receive-pack-result");

            await using var responseStream = await response.Content.ReadAsStreamAsync(effectiveToken).ConfigureAwait(false);
            await ParseReceivePackStatusAsync(responseStream, effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Git HTTP receive-pack request to '{requestUrl}' timed out after {_options.Timeout}.");
        }
    }

    private void ApplyRequestHeaders(HttpRequestMessage request)
    {
        _options.Credentials?.Apply(request);
        if (!string.IsNullOrEmpty(_options.Agent))
        {
            request.Headers.UserAgent.ParseAdd(_options.Agent);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GitRemoteException($"Failed to communicate with remote repository '{request.RequestUri}': {ex.Message}", ex);
        }
    }

    private static void EnsureSuccessStatusCode(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = response.StatusCode;
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            throw new GitAuthenticationException($"Authentication failed (401 Unauthorized) for '{response.RequestMessage?.RequestUri}'.", statusCode);
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            throw new GitAccessDeniedException($"Access denied (403 Forbidden) for '{response.RequestMessage?.RequestUri}'.", statusCode);
        }

        throw new GitRemoteException($"Git HTTP request to '{response.RequestMessage?.RequestUri}' failed with status {(int)statusCode} ({response.ReasonPhrase}).");
    }

    private static void ValidateContentType(HttpResponseMessage response, string expectedMediaType)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, expectedMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitRemoteException($"Invalid Content-Type returned by server: '{contentType}'. Expected '{expectedMediaType}'.");
        }
    }

    private static async Task<GitRemoteAdvertisement> ParseAdvertisementAsync(
        Stream stream,
        string expectedService,
        CancellationToken cancellationToken)
    {
        var reader = new PktLineReader(stream);

        // Line 1: # service=...
        var serviceHeaderPacket = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (serviceHeaderPacket is null || serviceHeaderPacket.Value.IsFlush)
        {
            throw new GitRemoteException("Empty response received during reference discovery.");
        }

        var serviceHeader = serviceHeaderPacket.Value.AsString().TrimEnd('\r', '\n');
        if (serviceHeader.StartsWith("ERR ", StringComparison.Ordinal))
        {
            throw new GitRemoteException($"Server returned error: {serviceHeader[4..]}");
        }

        var expectedHeader = $"# service={expectedService}";
        if (!serviceHeader.Equals(expectedHeader, StringComparison.Ordinal))
        {
            throw new GitRemoteException($"Expected service header '{expectedHeader}', but received '{serviceHeader}'.");
        }

        // Line 2: flush (0000)
        var flushPacket = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (flushPacket is null || !flushPacket.Value.IsFlush)
        {
            throw new GitRemoteException("Expected flush packet after service header.");
        }

        var references = new Dictionary<string, GitHash>(StringComparer.Ordinal);
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var symrefs = new Dictionary<string, string>(StringComparer.Ordinal);
        string? headSymrefTarget = null;
        GitHash? headHash = null;
        var objectFormat = "sha1";
        string? agent = null;
        var first = true;

        while (true)
        {
            var packet = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (packet is null || packet.Value.IsFlush)
            {
                break;
            }

            var text = packet.Value.AsString().TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (first)
            {
                first = false;
                var nullIndex = text.IndexOf('\0');
                if (nullIndex >= 0)
                {
                    var capPart = text[(nullIndex + 1)..];
                    text = text[..nullIndex];

                    foreach (var cap in capPart.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        capabilities.Add(cap);
                        if (cap.StartsWith("symref=", StringComparison.Ordinal))
                        {
                            var sym = cap[7..];
                            var colonIndex = sym.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                var source = sym[..colonIndex];
                                var target = sym[(colonIndex + 1)..];
                                symrefs[source] = target;
                                if (source.Equals("HEAD", StringComparison.Ordinal))
                                {
                                    headSymrefTarget = target;
                                }
                            }
                        }
                        else if (cap.StartsWith("agent=", StringComparison.Ordinal))
                        {
                            agent = cap[6..];
                        }
                        else if (cap.StartsWith("object-format=", StringComparison.Ordinal))
                        {
                            objectFormat = cap[14..];
                        }
                    }
                }
            }

            // text is "{hash} {refName}"
            var spaceIndex = text.IndexOf(' ');
            if (spaceIndex <= 0)
            {
                continue;
            }

            var hashStr = text[..spaceIndex];
            var refName = text[(spaceIndex + 1)..].Trim();

            // Handle empty repository advertisement: "{zeroHash} capabilities^{}"
            if (refName.Equals("capabilities^{}", StringComparison.Ordinal))
            {
                continue;
            }

            // Ignore peeled tag annotations: "refs/tags/v1.0^{}"
            if (refName.EndsWith("^{}", StringComparison.Ordinal))
            {
                continue;
            }

            if (GitHash.TryParse(hashStr, out var hash))
            {
                if (refName.Equals("HEAD", StringComparison.Ordinal))
                {
                    headHash = hash;
                }
                references[refName] = hash;
            }
        }

        return new GitRemoteAdvertisement(references, capabilities, symrefs, headSymrefTarget, headHash, objectFormat, agent);
    }

    private static async Task ParseReceivePackStatusAsync(Stream stream, CancellationToken cancellationToken)
    {
        var reader = new PktLineReader(stream);

        // First packet: unpack ok / unpack <error>
        var unpackPacket = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (unpackPacket is null || unpackPacket.Value.IsFlush)
        {
            throw new GitRemoteException("Empty or premature termination of receive-pack status response.");
        }

        var unpackLine = unpackPacket.Value.AsString().TrimEnd('\r', '\n');
        if (unpackLine.StartsWith("ERR ", StringComparison.Ordinal))
        {
            throw new GitRemoteException($"Server returned error: {unpackLine[4..]}");
        }

        if (!unpackLine.Equals("unpack ok", StringComparison.Ordinal))
        {
            throw new GitRemoteException($"Server unpack failed: {unpackLine}");
        }

        // Subsequent packets: ok {ref} / ng {ref} {msg}
        var errors = new List<string>();
        while (true)
        {
            var packet = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (packet is null || packet.Value.IsFlush)
            {
                break;
            }

            var line = packet.Value.AsString().TrimEnd('\r', '\n');
            if (line.StartsWith("ng ", StringComparison.Ordinal))
            {
                errors.Add(line[3..]);
            }
        }

        if (errors.Count > 0)
        {
            throw new GitRemoteException($"Remote reference updates failed: {string.Join("; ", errors)}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

/// <summary>
/// Encapsulates the response from an upload-pack operation, including the packfile stream and underlying HTTP resources.
/// </summary>
public sealed class GitUploadPackResponse : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the readable packfile payload stream.
    /// </summary>
    public Stream PackStream { get; }

    private readonly HttpResponseMessage _response;
    private readonly MemoryStream _requestBodyStream;

    internal GitUploadPackResponse(Stream packStream, HttpResponseMessage response, MemoryStream requestBodyStream)
    {
        PackStream = packStream;
        _response = response;
        _requestBodyStream = requestBodyStream;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        PackStream.Dispose();
        _response.Dispose();
        _requestBodyStream.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await PackStream.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
        await _requestBodyStream.DisposeAsync().ConfigureAwait(false);
    }
}

