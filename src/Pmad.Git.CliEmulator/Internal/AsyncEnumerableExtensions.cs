using System.Runtime.CompilerServices;

namespace Pmad.Git.CliEmulator.Internal;

internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> Take<T>(
        this IAsyncEnumerable<T> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            yield break;
        }

        var taken = 0;
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
            taken++;
            if (taken >= count)
            {
                yield break;
            }
        }
    }
}
