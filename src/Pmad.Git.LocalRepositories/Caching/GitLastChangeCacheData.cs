using System.Text.Json.Serialization;

namespace Pmad.Git.LocalRepositories.Caching;

internal sealed class GitLastChangeCacheData
{
    [JsonPropertyName("v")]
    public int Version { get; set; }

    [JsonPropertyName("f")]
    public List<GitLastChangeCacheEntry>? Files { get; set; }
}
