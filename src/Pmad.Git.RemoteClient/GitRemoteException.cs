using System.Net;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Exception thrown when a Git remote operation fails.
/// </summary>
public class GitRemoteException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteException"/> class.
    /// </summary>
    public GitRemoteException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GitRemoteException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The inner exception that caused the error.</param>
    public GitRemoteException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when authentication to a Git remote fails (HTTP 401).
/// </summary>
public sealed class GitAuthenticationException : GitRemoteException
{
    /// <summary>
    /// Gets the HTTP status code returned by the server.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public GitAuthenticationException(string message, HttpStatusCode statusCode = HttpStatusCode.Unauthorized)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Exception thrown when access to a Git remote repository is forbidden (HTTP 403).
/// </summary>
public sealed class GitAccessDeniedException : GitRemoteException
{
    /// <summary>
    /// Gets the HTTP status code returned by the server.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitAccessDeniedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public GitAccessDeniedException(string message, HttpStatusCode statusCode = HttpStatusCode.Forbidden)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Exception thrown when a Git remote repository is not found (HTTP 404).
/// </summary>
public sealed class GitRepositoryNotFoundException : GitRemoteException
{
    /// <summary>
    /// Gets the HTTP status code returned by the server.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public GitRepositoryNotFoundException(string message, HttpStatusCode statusCode = HttpStatusCode.NotFound)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

