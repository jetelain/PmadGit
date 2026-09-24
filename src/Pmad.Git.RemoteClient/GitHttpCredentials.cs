using System.Net.Http.Headers;
using System.Text;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Base class for HTTP authentication credentials used by the Git remote client.
/// </summary>
public abstract class GitHttpCredentials
{
    /// <summary>
    /// Applies authentication headers to the specified HTTP request message.
    /// </summary>
    /// <param name="request">The HTTP request message to authenticate.</param>
    public abstract void Apply(HttpRequestMessage request);

    /// <summary>
    /// Creates HTTP Basic authentication credentials with the specified username and password.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <returns>A new <see cref="GitHttpCredentials"/> instance.</returns>
    public static GitHttpCredentials Basic(string username, string password)
        => new BasicCredentials(username, password);

    /// <summary>
    /// Creates Bearer token authentication credentials.
    /// </summary>
    /// <param name="token">The bearer token.</param>
    /// <returns>A new <see cref="GitHttpCredentials"/> instance.</returns>
    public static GitHttpCredentials Bearer(string token)
        => new BearerCredentials(token);

    /// <summary>
    /// Creates Personal Access Token (PAT) authentication credentials.
    /// </summary>
    /// <param name="token">The personal access token.</param>
    /// <param name="username">Optional username. If not specified, standard PAT format is used.</param>
    /// <returns>A new <see cref="GitHttpCredentials"/> instance.</returns>
    public static GitHttpCredentials PersonalAccessToken(string token, string? username = null)
        => new PersonalAccessTokenCredentials(token, username);

    /// <summary>
    /// Creates custom header authentication credentials.
    /// </summary>
    /// <param name="headerName">The HTTP header name.</param>
    /// <param name="headerValue">The HTTP header value.</param>
    /// <returns>A new <see cref="GitHttpCredentials"/> instance.</returns>
    public static GitHttpCredentials Custom(string headerName, string headerValue)
        => new CustomHeaderCredentials(headerName, headerValue);
}

internal sealed class BasicCredentials : GitHttpCredentials
{
    private readonly string _authHeaderValue;

    public BasicCredentials(string username, string password)
    {
        var raw = $"{username ?? string.Empty}:{password ?? string.Empty}";
        _authHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public override void Apply(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeaderValue);
    }
}

internal sealed class BearerCredentials : GitHttpCredentials
{
    private readonly string _token;

    public BearerCredentials(string token)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public override void Apply(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }
}

internal sealed class PersonalAccessTokenCredentials : GitHttpCredentials
{
    private readonly string _authHeaderValue;

    public PersonalAccessTokenCredentials(string token, string? username = null)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        var raw = string.IsNullOrEmpty(username)
            ? $"token:{token}"
            : $"{username}:{token}";
        _authHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public override void Apply(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeaderValue);
    }
}

internal sealed class CustomHeaderCredentials : GitHttpCredentials
{
    private readonly string _headerName;
    private readonly string _headerValue;

    public CustomHeaderCredentials(string headerName, string headerValue)
    {
        _headerName = headerName ?? throw new ArgumentNullException(nameof(headerName));
        _headerValue = headerValue ?? throw new ArgumentNullException(nameof(headerValue));
    }

    public override void Apply(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(_headerName, _headerValue);
    }
}

