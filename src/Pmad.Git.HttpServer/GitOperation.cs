namespace Pmad.Git.HttpServer;

/// <summary>
/// Represents the type of Git operation being performed.
/// </summary>
public enum GitOperation
{
    /// <summary>
    /// Read operation (fetch, clone, or info/refs for upload-pack).
    /// </summary>
    Read,

    /// <summary>
    /// Write operation (push or info/refs for receive-pack).
    /// </summary>
    Write
}
