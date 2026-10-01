using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.CliEmulator;

/// <summary>
/// Extension methods for instantiating <see cref="GitCliEmulator"/> from managed Git repository instances.
/// </summary>
public static class GitCliEmulatorExtensions
{
    /// <summary>
    /// Creates a new <see cref="GitCliEmulator"/> with a pure managed Smart HTTP remote client
    /// configured on top of this workspace repository.
    /// </summary>
    /// <param name="repository">The workspace repository with active index and working tree.</param>
    /// <param name="remoteOptions">Optional client options for HTTP communication, credentials, and timeouts.</param>
    /// <param name="defaultRemoteUrl">Optional default remote URL.</param>
    /// <param name="disposeRepositories">Whether disposing the emulator should also dispose the repositories. Defaults to <see langword="false"/>.</param>
    /// <returns>A new <see cref="GitCliEmulator"/> instance.</returns>
    public static GitCliEmulator CreateCliEmulator(
        this IGitWorkspaceRepository repository,
        GitRemoteClientOptions? remoteOptions = null,
        string? defaultRemoteUrl = null,
        bool disposeRepositories = false)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var remote = new GitRemoteClientRepository(repository, defaultRemoteUrl, remoteOptions);
        return new GitCliEmulator(repository, remote, disposeWorkspace: disposeRepositories, disposeRemote: true);
    }

    /// <summary>
    /// Creates a new <see cref="GitCliEmulator"/> from an existing managed <see cref="GitRemoteClientRepository"/>.
    /// </summary>
    /// <param name="remoteRepository">The remote client repository with an active workspace.</param>
    /// <param name="disposeRepositories">Whether disposing the emulator should also dispose the remote client repository. Defaults to <see langword="false"/>.</param>
    /// <returns>A new <see cref="GitCliEmulator"/> instance.</returns>
    public static GitCliEmulator CreateCliEmulator(
        this GitRemoteClientRepository remoteRepository,
        bool disposeRepositories = false)
    {
        ArgumentNullException.ThrowIfNull(remoteRepository);

        return new GitCliEmulator(remoteRepository, disposeRepositories);
    }
}
