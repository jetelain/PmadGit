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
    private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly GitRemoteClientOptions _options;

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

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
            _httpClient = SharedHttpClient;
            _disposeHttpClient = false;
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

        if (service != "git-upload-pack" && service != "git-receive-pack")
        {
            throw new ArgumentException($"Invalid Git service '{service}'. Expected 'git-upload-pack' or 'git-receive-pack'.", nameof(service));
        }

        var requestUri = BuildServiceUri(remoteUrl, "info/refs", $"service={service}");

        using var timeoutCts = new CancellationTokenSource(_options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        try
        {
            using var response = await SendWithRedirectsAsync(
                (uri, sameOrigin) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, uri);
                    ApplyRequestHeaders(req, remoteUrl, includeCredentials: sameOrigin);
                    return req;
                },
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                effectiveToken).ConfigureAwait(false);

            EnsureSuccessStatusCode(response);
            ValidateContentType(response, $"application/x-{service}-advertisement");

            await using var responseStream = await response.Content.ReadAsStreamAsync(effectiveToken).ConfigureAwait(false);
            return await ParseAdvertisementAsync(responseStream, service, effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Git HTTP request to '{FormatDiagnosticUri(requestUri)}' timed out after {_options.Timeout}.");
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

        var requestUri = BuildServiceUri(remoteUrl, "git-upload-pack");

        var timeoutCts = new CancellationTokenSource(_options.Timeout);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        var requestBodyStream = new MemoryStream();
        try
        {
            var negotiatedSideband = false;

            // Negotiate client capabilities to include in first want line
            var clientCapabilities = new List<string>();
            if (advertisement.Capabilities.Contains("multi_ack_detailed"))
            {
                clientCapabilities.Add("multi_ack_detailed");
            }
            else if (advertisement.Capabilities.Contains("multi_ack"))
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
            if (advertisement.ObjectFormat == GitObjectFormat.Sha256)
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

            var requestBodyBytes = requestBodyStream.ToArray();

            var response = await SendWithRedirectsAsync(
                (uri, sameOrigin) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, uri);
                    ApplyRequestHeaders(req, remoteUrl, includeCredentials: sameOrigin);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-git-upload-pack-result"));
                    req.Content = new ByteArrayContent(requestBodyBytes);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-git-upload-pack-request");
                    return req;
                },
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                effectiveToken).ConfigureAwait(false);
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

                        // Terminal ACK (including "ACK <oid>" or "ACK <oid> ready")
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
            throw new TimeoutException($"Git HTTP upload-pack request to '{FormatDiagnosticUri(requestUri)}' timed out after {_options.Timeout}.");
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

        var requestUri = BuildServiceUri(remoteUrl, "git-receive-pack");
        var zeroHash = new string('0', hashLengthBytes * 2);

        using var timeoutCts = new CancellationTokenSource(_options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        try
        {
            using var commandsPayload = new MemoryStream();

            // Write command pkt-lines
            for (var i = 0; i < commands.Count; i++)
            {
                var cmd = commands[i];
                var oldSha = cmd.OldValue?.Value ?? zeroHash;
                var newSha = cmd.NewValue?.Value ?? zeroHash;

                if (i == 0)
                {
                    var caps = new List<string>();
                    if (advertisement.Capabilities.Contains("report-status-v2"))
                    {
                        caps.Add("report-status-v2");
                    }
                    else if (advertisement.Capabilities.Contains("report-status"))
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
                    await PktLineWriter.WriteStringAsync(commandsPayload, line, effectiveToken).ConfigureAwait(false);
                }
                else
                {
                    var line = $"{oldSha} {newSha} {cmd.RefName}\n";
                    await PktLineWriter.WriteStringAsync(commandsPayload, line, effectiveToken).ConfigureAwait(false);
                }
            }

            await PktLineWriter.WriteFlushAsync(commandsPayload, effectiveToken).ConfigureAwait(false);
            commandsPayload.Position = 0;

            var commandsPayloadBytes = commandsPayload.ToArray();

            using var response = await SendWithRedirectsAsync(
                (uri, sameOrigin) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, uri);
                    ApplyRequestHeaders(req, remoteUrl, includeCredentials: sameOrigin);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-git-receive-pack-result"));

                    var commandsStream = new MemoryStream(commandsPayloadBytes);
                    Stream requestStream;
                    if (packDataStream != null)
                    {
                        if (packDataStream.CanSeek)
                        {
                            packDataStream.Seek(0, SeekOrigin.Begin);
                        }
                        requestStream = new ConcatenatedStream(commandsStream, packDataStream, leaveSecondOpen: true);
                    }
                    else
                    {
                        requestStream = commandsStream;
                    }

                    req.Content = new StreamContent(requestStream);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-git-receive-pack-request");
                    return req;
                },
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                effectiveToken).ConfigureAwait(false);

            EnsureSuccessStatusCode(response);
            ValidateContentType(response, "application/x-git-receive-pack-result");

            await using var responseStream = await response.Content.ReadAsStreamAsync(effectiveToken).ConfigureAwait(false);
            await ParseReceivePackStatusAsync(responseStream, commands, effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Git HTTP receive-pack request to '{FormatDiagnosticUri(requestUri)}' timed out after {_options.Timeout}.");
        }
    }

    internal static string FormatDiagnosticUri(Uri? uri)
    {
        if (uri == null)
        {
            return string.Empty;
        }

        try
        {
            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.ToString();
        }
        catch
        {
            return uri.GetLeftPart(UriPartial.Path);
        }
    }

    private void ApplyRequestHeaders(HttpRequestMessage request, Uri? remoteUrl = null, bool includeCredentials = true)
    {
        if (includeCredentials)
        {
            if (_options.Credentials != null)
            {
                _options.Credentials.Apply(request);
            }
            else if (remoteUrl != null && !string.IsNullOrEmpty(remoteUrl.UserInfo))
            {
                var userInfo = remoteUrl.UserInfo;
                var colonIndex = userInfo.IndexOf(':');
                if (colonIndex >= 0)
                {
                    var username = Uri.UnescapeDataString(userInfo[..colonIndex]);
                    var password = Uri.UnescapeDataString(userInfo[(colonIndex + 1)..]);
                    GitHttpCredentials.Basic(username, password).Apply(request);
                }
                else
                {
                    var username = Uri.UnescapeDataString(userInfo);
                    GitHttpCredentials.Basic(username, string.Empty).Apply(request);
                }
            }
        }

        if (!string.IsNullOrEmpty(_options.Agent))
        {
            request.Headers.UserAgent.ParseAdd(_options.Agent);
        }
    }

    private static Uri BuildServiceUri(Uri remoteUrl, string suffix, string? queryString = null)
    {
        var builder = new UriBuilder(remoteUrl);
        var path = builder.Path.TrimEnd('/');
        builder.Path = $"{path}/{suffix.TrimStart('/')}";
        builder.UserName = string.Empty;
        builder.Password = string.Empty;

        if (!string.IsNullOrEmpty(queryString))
        {
            if (string.IsNullOrEmpty(builder.Query))
            {
                builder.Query = queryString.TrimStart('?');
            }
            else
            {
                var existingQuery = builder.Query.TrimStart('?');
                builder.Query = $"{existingQuery}&{queryString.TrimStart('?')}";
            }
        }

        return builder.Uri;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
                      HttpStatusCode.Found or
                      HttpStatusCode.SeeOther or
                      HttpStatusCode.TemporaryRedirect or
                      (HttpStatusCode)308;

    private static bool IsSameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port;

    private async Task<HttpResponseMessage> SendWithRedirectsAsync(
        Func<Uri, bool, HttpRequestMessage> requestFactory,
        Uri initialUri,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        const int maxRedirects = 10;
        var currentUri = initialUri;
        var redirectCount = 0;

        while (true)
        {
            var isSameOrigin = IsSameOrigin(initialUri, currentUri);
            var request = requestFactory(currentUri, isSameOrigin);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                request.Dispose();
                throw new GitRemoteException($"Failed to communicate with remote repository '{FormatDiagnosticUri(request.RequestUri)}': {ex.Message}", ex);
            }

            if (!IsRedirectStatusCode(response.StatusCode))
            {
                return response;
            }

            redirectCount++;
            if (redirectCount > maxRedirects)
            {
                response.Dispose();
                request.Dispose();
                throw new GitRemoteException($"Too many redirects when requesting '{FormatDiagnosticUri(initialUri)}'. Maximum allowed is {maxRedirects}.");
            }

            var location = response.Headers.Location;
            if (location is null)
            {
                var statusCode = (int)response.StatusCode;
                response.Dispose();
                request.Dispose();
                throw new GitRemoteException($"Redirect response {statusCode} from '{FormatDiagnosticUri(currentUri)}' did not include a Location header.");
            }

            Uri nextUri;
            try
            {
                nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            }
            catch (Exception ex)
            {
                response.Dispose();
                request.Dispose();
                throw new GitRemoteException($"Invalid redirect URI '{location}' from '{FormatDiagnosticUri(currentUri)}': {ex.Message}", ex);
            }

            if (nextUri.Scheme != Uri.UriSchemeHttp && nextUri.Scheme != Uri.UriSchemeHttps)
            {
                response.Dispose();
                request.Dispose();
                throw new NotSupportedException($"Unsupported redirect URL scheme '{nextUri.Scheme}'. The managed Git remote client only supports HTTP and HTTPS protocols.");
            }

            if (currentUri.Scheme == Uri.UriSchemeHttps && nextUri.Scheme == Uri.UriSchemeHttp)
            {
                response.Dispose();
                request.Dispose();
                throw new GitRemoteException($"Insecure redirect from '{FormatDiagnosticUri(currentUri)}' to '{FormatDiagnosticUri(nextUri)}' is not allowed.");
            }

            if (request.Method == HttpMethod.Post &&
                response.StatusCode != HttpStatusCode.TemporaryRedirect &&
                (int)response.StatusCode != 308)
            {
                var statusCode = (int)response.StatusCode;
                response.Dispose();
                request.Dispose();
                throw new GitRemoteException($"Server returned redirect {statusCode} for POST request to '{FormatDiagnosticUri(currentUri)}'. Only 307 and 308 redirects are supported for POST operations.");
            }

            response.Dispose();
            request.Dispose();
            currentUri = nextUri;
        }
    }

    private static void EnsureSuccessStatusCode(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var diagnosticUri = FormatDiagnosticUri(response.RequestMessage?.RequestUri);
        var statusCode = response.StatusCode;
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            throw new GitAuthenticationException($"Authentication failed (401 Unauthorized) for '{diagnosticUri}'.", statusCode);
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            throw new GitAccessDeniedException($"Access denied (403 Forbidden) for '{diagnosticUri}'.", statusCode);
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            throw new GitRepositoryNotFoundException($"Repository not found (404 Not Found) for '{diagnosticUri}'.", statusCode);
        }

        throw new GitRemoteException($"Git HTTP request to '{diagnosticUri}' failed with status {(int)statusCode} ({response.ReasonPhrase}).");
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
        var objectFormat = GitObjectFormat.Sha1;
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

            if (text.StartsWith("ERR ", StringComparison.Ordinal))
            {
                throw new GitRemoteException($"Server returned error: {text[4..]}");
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
                            var formatStr = cap[14..];
                            if (!GitObjectFormatExtensions.TryParse(formatStr, out objectFormat))
                            {
                                throw new GitRemoteException($"Unsupported object format '{formatStr}' advertised by remote.");
                            }
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

    private static async Task ParseReceivePackStatusAsync(Stream stream, IReadOnlyList<GitRefUpdateCommand> commands, CancellationToken cancellationToken)
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
        var confirmedRefs = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var packet = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (packet is null || packet.Value.IsFlush)
            {
                break;
            }

            var line = packet.Value.AsString().TrimEnd('\r', '\n');
            if (line.StartsWith("ERR ", StringComparison.Ordinal))
            {
                throw new GitRemoteException($"Server returned error: {line[4..]}");
            }
            if (line.StartsWith("option ", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("ok ", StringComparison.Ordinal))
            {
                confirmedRefs.Add(line[3..].Trim());
                continue;
            }
            if (line.StartsWith("ng ", StringComparison.Ordinal))
            {
                errors.Add(line[3..]);
            }
        }

        if (errors.Count > 0)
        {
            throw new GitRemoteException($"Remote reference updates failed: {string.Join("; ", errors)}");
        }

        foreach (var cmd in commands)
        {
            if (!confirmedRefs.Contains(cmd.RefName))
            {
                throw new GitRemoteException($"Remote did not confirm reference update for '{cmd.RefName}'.");
            }
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

