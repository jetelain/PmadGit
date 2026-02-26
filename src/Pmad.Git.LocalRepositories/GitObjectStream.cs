namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a git object payload as a stream from the object store.
/// </summary>
public sealed class GitObjectStream : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Create a new instance of GitObjectStream with the specified type, content stream, and length.
    /// </summary>
    /// <param name="type">The decoded type stored in the object header.</param>
    /// <param name="content">The decompressed object content as a stream.</param>
    /// <param name="length">The length of the content in bytes.</param>
    /// <remarks>
    /// The content stream is owned by this GitObjectStream instance and will be disposed when this instance is disposed.
    /// </remarks>
    public GitObjectStream(GitObjectType type, Stream content, long length)
    {
        Type = type;
        Content = content;
        Length = length;
    }

    /// <summary>
    /// The decoded type stored in the object header.
    /// </summary>
    public GitObjectType Type { get; }

    /// <summary>
    /// The decompressed object content as a stream.
    /// </summary>
    public Stream Content { get; }

    /// <summary>
    /// The length of the content in bytes.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// Disposes the content stream.
    /// </summary>
    public void Dispose()
    {
        Content.Dispose();
    }

    /// <summary>
    /// Asynchronously disposes the content stream.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);
    }
}
