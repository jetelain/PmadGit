namespace Pmad.Git.LocalRepositories;

internal static class GitObjectTypeHelper
{
    internal static GitObjectType ParseType(string type) => type switch
    {
        "commit" => GitObjectType.Commit,
        "tree" => GitObjectType.Tree,
        "blob" => GitObjectType.Blob,
        "tag" => GitObjectType.Tag,
        _ => throw new NotSupportedException($"Unsupported git object type '{type}'")
    };

    internal static string GetObjectTypeName(GitObjectType type) => type switch
    {
        GitObjectType.Commit => "commit",
        GitObjectType.Tree => "tree",
        GitObjectType.Blob => "blob",
        GitObjectType.Tag => "tag",
        _ => throw new NotSupportedException($"Unsupported git object type '{type}'.")
    };
}
