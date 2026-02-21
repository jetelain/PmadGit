namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Provides low-level read and write access to the git object database.
/// </summary>
public interface IGitObjectStore
{
    /// <summary>
    /// Reads a raw git object from the object database and returns its fully buffered payload.
    /// </summary>
    /// <param name="hash">Identifier of the object to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The decoded object type and payload as a byte array.</returns>
    /// <exception cref="System.IO.FileNotFoundException">Thrown when no object with the given hash exists.</exception>
    Task<GitObjectData> ReadObjectAsync(GitHash hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a raw git object from the object database as a stream.
    /// For loose objects the stream reads directly from disk without buffering the entire content.
    /// For pack objects the stream is backed by a <see cref="System.IO.MemoryStream"/> with the decompressed content.
    /// The caller is responsible for disposing the returned <see cref="GitObjectStream"/>.
    /// </summary>
    /// <param name="hash">Identifier of the object to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The decoded object as a stream.</returns>
    /// <exception cref="System.IO.FileNotFoundException">Thrown when no object with the given hash exists.</exception>
    Task<GitObjectStream> ReadObjectStreamAsync(GitHash hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a raw git object to the object database from an in-memory buffer.
    /// If an object with the computed hash already exists it is reused without writing.
    /// </summary>
    /// <param name="type">Object kind to persist.</param>
    /// <param name="content">Raw payload without headers.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash assigned to the stored object.</returns>
    Task<GitHash> WriteObjectAsync(GitObjectType type, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a raw git object to the object database by reading exactly <paramref name="contentLength"/> bytes from
    /// <paramref name="content"/>.
    /// If an object with the computed hash already exists it is reused without writing.
    /// </summary>
    /// <param name="type">Object kind to persist.</param>
    /// <param name="content">Stream providing the raw payload without headers.</param>
    /// <param name="contentLength">Number of bytes to read from <paramref name="content"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash assigned to the stored object.</returns>
    /// <exception cref="System.IO.InvalidDataException">Thrown when the stream yields fewer or more bytes than <paramref name="contentLength"/>.</exception>
    Task<GitHash> WriteObjectAsync(GitObjectType type, Stream content, long contentLength, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a raw git object to the object database from a seekable stream.
    /// The number of bytes to read is derived from the stream's remaining length (<c>Length - Position</c>).
    /// If an object with the computed hash already exists it is reused without writing.
    /// </summary>
    /// <param name="type">Object kind to persist.</param>
    /// <param name="content">Seekable stream providing the raw payload without headers.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <returns>The hash assigned to the stored object.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="content"/> does not support seeking.</exception>
    Task<GitHash> WriteObjectAsync(GitObjectType type, Stream content, CancellationToken cancellationToken);
}