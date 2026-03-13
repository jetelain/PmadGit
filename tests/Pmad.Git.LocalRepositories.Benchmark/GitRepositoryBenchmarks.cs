using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Pmad.Git.LocalRepositories.Benchmark;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[SimpleJob(RuntimeMoniker.NativeAot80)]
[SimpleJob(RuntimeMoniker.NativeAot10_0)]
public class GitRepositoryBenchmarks
{
    private GitRepository _repo = null!;
    private string _diskCachePath = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var repoPath = Environment.GetEnvironmentVariable("GIT_REPO_PATH")
            ?? throw new InvalidOperationException("GIT_REPO_PATH environment variable is not set.");

        _repo = GitRepository.Open(repoPath);
        _diskCachePath = Path.Combine(_repo.GitDirectory, "pmad-cache");

        // Warm up JIT and ensure the disk cache is populated for DiskCacheOnly / AllCaches
        _repo.InvalidateCaches(true);
        await _repo.GetFilesWithLastChangeAsync();
    }

    [IterationSetup(Target = nameof(NoDiskCache))]
    public void SetupNoDiskCache()
    {
        if (Directory.Exists(_diskCachePath))
        {
            Directory.Delete(_diskCachePath, recursive: true);
        }
        _repo.InvalidateCaches(true);
    }

    [IterationSetup(Target = nameof(DiskCacheOnly))]
    public void SetupDiskCacheOnly()
    {
        _repo.InvalidateCaches(true);
    }

    /// <summary>No disk cache, no in-memory cache — full traversal every time.</summary>
    [Benchmark(Baseline = true)]
    [IterationCount(5)]
    [WarmupCount(1)]
    public Task<IReadOnlyList<GitFileLastChange>> NoDiskCache() => _repo.GetFilesWithLastChangeAsync();

    /// <summary>Disk cache present, in-memory cache cleared — reads JSON from disk.</summary>
    [Benchmark]
    public Task<IReadOnlyList<GitFileLastChange>> DiskCacheOnly() => _repo.GetFilesWithLastChangeAsync();

    /// <summary>Both caches hot — returns the in-memory result immediately.</summary>
    [Benchmark]
    public Task<IReadOnlyList<GitFileLastChange>> AllCaches() => _repo.GetFilesWithLastChangeAsync();
}
