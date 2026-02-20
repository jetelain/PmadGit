namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a git object payload as a stream from the object store.
/// </summary>
/// <param name="Type">The decoded type stored in the object header.</param>
/// <param name="Content">The decompressed object content as a stream.</param>
/// <param name="Length">The length of the content in bytes, or null if unknown.</param>
public sealed record GitObjectStream(GitObjectType Type, Stream Content, long Length) : IDisposable, IAsyncDisposable
{
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
