namespace Pmad.Git.LocalRepositories.Helpers;

internal static class GitFileLastChangeHelper
{
    internal static IReadOnlyList<GitFileLastChange> ApplyFilters(
        IReadOnlyList<GitFileLastChange> source,
        string prefix,
        SearchOption searchOption,
        Func<string, bool>? predicate)
    {
        if (string.IsNullOrEmpty(prefix) && searchOption == SearchOption.AllDirectories && predicate == null)
        {
            return source;
        }

        var pathPrefix = string.IsNullOrEmpty(prefix) ? null : prefix + "/";
        var query = source.AsEnumerable();

        if (pathPrefix != null)
        {
            query = query.Where(f => f.Path.StartsWith(pathPrefix, StringComparison.Ordinal));

            if (searchOption == SearchOption.TopDirectoryOnly)
            {
                query = query.Where(f => f.Path.IndexOf('/', pathPrefix.Length) < 0);
            }
        }
        else if (searchOption == SearchOption.TopDirectoryOnly)
        {
            query = query.Where(f => !f.Path.Contains('/'));
        }

        if (predicate != null)
        {
            query = query.Where(f => predicate(f.Path));
        }

        return query.ToList();
    }
}
