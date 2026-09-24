using Pmad.Git.LocalRepositories;
using Pmad.Git.Protocol;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GitHubIntegrationFactAttribute : FactAttribute
{
    public GitHubIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_GITHUB_INTEGRATION_TESTS") != "1")
        {
            Skip = "Opt-in live integration test. Set RUN_GITHUB_INTEGRATION_TESTS=1 to run.";
        }
    }
}

[Trait("Category", "Integration")]
public sealed class GitHubIntegrationTest
{
    private const string GitHubRepoUrl = "https://github.com/jetelain/PmadGit.git";

    private static async Task<bool> IsGitHubReachableAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(GitHubRepoUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureGitHubReachableAsync()
    {
        if (!await IsGitHubReachableAsync().ConfigureAwait(false))
        {
            throw new Xunit.Sdk.XunitException($"GitHub repository '{GitHubRepoUrl}' is unreachable. Live integration tests require network access to GitHub.");
        }
    }

    [GitHubIntegrationFact]
    public async Task DiscoverReferencesAsync_FromGitHub_Succeeds()
    {
        await EnsureGitHubReachableAsync();

        using var connection = new GitHttpConnection();
        var uri = new Uri(GitHubRepoUrl);

        var advertisement = await connection.DiscoverReferencesAsync(uri, "git-upload-pack");

        Assert.NotNull(advertisement);
        Assert.NotEmpty(advertisement.References);
        Assert.Contains("refs/heads/master", advertisement.References.Keys);
        Assert.Equal("refs/heads/master", advertisement.HeadSymrefTarget);
        Assert.True(advertisement.Capabilities.Contains("side-band-64k"));
        Assert.True(advertisement.Capabilities.Contains("multi_ack"));
        Assert.StartsWith("git/github", advertisement.Agent);
    }

    [GitHubIntegrationFact]
    public async Task CloneAsync_FromGitHub_ClonesRepositoryAndReadsCommits()
    {
        await EnsureGitHubReachableAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), "pmad_git_github_clone_" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new GitRemoteClientOptions();
            var progressMessages = new List<string>();
            options.OnProgress = msg => progressMessages.Add(msg);

            using var repo = await GitRemoteClientRepository.CloneAsync(GitHubRepoUrl, tempDir, options, branch: "master");

            Assert.NotNull(repo);
            var headCommit = await repo.LocalRepository.ReferenceStore.ResolveHeadAsync();

            var masterRef = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/heads/master");
            Assert.NotNull(masterRef);
            Assert.Equal(headCommit, masterRef.Value);

            var originMasterRef = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/master");
            Assert.NotNull(originMasterRef);
            Assert.Equal(masterRef.Value, originMasterRef.Value);

            // Verify files exist in the checked out working tree
            Assert.True(File.Exists(Path.Combine(tempDir, "README.md")));
            Assert.True(File.Exists(Path.Combine(tempDir, "Pmad.Git.sln")));

            // Verify tracking status
            var tracking = await repo.GetTrackingStatusAsync("master");
            Assert.Equal(0, tracking.AheadCount);
            Assert.Equal(0, tracking.BehindCount);
            Assert.True(tracking.IsSynchronized);

            // Verify commit pushed check
            var isPushedWithBranch = await repo.IsCommitPushedAsync(headCommit, "origin/master");
            Assert.True(isPushedWithBranch);
            var isPushedAny = await repo.IsCommitPushedAsync(headCommit);
            Assert.True(isPushedAny);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [GitHubIntegrationFact]
    public async Task FetchAsync_FromGitHub_IncrementalFetchSucceeds()
    {
        await EnsureGitHubReachableAsync();

        var tempDir = Path.Combine(Path.GetTempPath(), "pmad_git_github_fetch_" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new GitRemoteClientOptions();
            using var repo = await GitRemoteClientRepository.CloneAsync(GitHubRepoUrl, tempDir, options, branch: "master");

            // Incremental fetch where local repository already has commits
            await repo.FetchAsync();

            var originMasterRef = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/master");
            Assert.NotNull(originMasterRef);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(path, true);
        }
    }
}
