using System.Text.Json.Serialization;

namespace Pmad.Git.LocalRepositories.Caching;

[JsonSerializable(typeof(GitLastChangeCacheData))]
internal partial class GitLastChangeCacheContext : JsonSerializerContext { }
