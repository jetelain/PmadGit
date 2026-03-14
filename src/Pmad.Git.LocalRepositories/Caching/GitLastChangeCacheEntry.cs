using System.Text.Json.Serialization;

namespace Pmad.Git.LocalRepositories.Caching;

internal sealed class GitLastChangeCacheEntry
{
    [JsonPropertyName("p")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("c")]
    public string Commit { get; set; } = string.Empty;
}
