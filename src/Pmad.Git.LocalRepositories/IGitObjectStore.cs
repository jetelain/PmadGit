namespace Pmad.Git.LocalRepositories;

internal interface IGitObjectStore
{
    Task<GitObjectData> ReadObjectAsync(GitHash hash, CancellationToken cancellationToken = default);
    
    Task<GitObjectStream> ReadObjectStreamAsync(GitHash hash, CancellationToken cancellationToken = default);

    Task<GitHash> WriteObjectAsync(GitObjectType type, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Task<GitHash> WriteObjectAsync(GitObjectType type, Stream content, CancellationToken cancellationToken);
}