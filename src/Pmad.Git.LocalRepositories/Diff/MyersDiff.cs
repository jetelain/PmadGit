using System;
using System.Collections.Generic;

namespace Pmad.Git.LocalRepositories.Diff;

/// <summary>
/// Implements Eugene W. Myers' $O(ND)$ difference algorithm for computing the shortest edit script (SES)
/// between two sequences.
/// </summary>
public static class MyersDiff
{
    /// <summary>
    /// Default maximum edit distance allowed before falling back to whole-chunk replacement.
    /// </summary>
    public const int DefaultMaxEditDistance = 2500;

    /// <summary>
    /// Computes the sequence of differences (keeps, inserts, deletes) between <paramref name="oldItems"/> and <paramref name="newItems"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements to compare.</typeparam>
    /// <param name="oldItems">The original sequence.</param>
    /// <param name="newItems">The modified sequence.</param>
    /// <param name="comparer">Optional equality comparer; defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <param name="maxEditDistance">Optional maximum edit distance threshold. If the edit distance exceeds this threshold, the algorithm falls back to replacing the middle portion. Defaults to <see cref="DefaultMaxEditDistance"/>.</param>
    /// <returns>A list of <see cref="DiffChange{T}"/> representing the edit script.</returns>
    public static IReadOnlyList<DiffChange<T>> Compute<T>(
        IReadOnlyList<T> oldItems,
        IReadOnlyList<T> newItems,
        IEqualityComparer<T>? comparer = null,
        int? maxEditDistance = null)
    {
        ArgumentNullException.ThrowIfNull(oldItems);
        ArgumentNullException.ThrowIfNull(newItems);

        comparer ??= EqualityComparer<T>.Default;

        var n = oldItems.Count;
        var m = newItems.Count;

        // Fast path: both empty
        if (n == 0 && m == 0)
        {
            return Array.Empty<DiffChange<T>>();
        }

        // Fast path: old is empty -> all inserts
        if (n == 0)
        {
            var inserts = new DiffChange<T>[m];
            for (var j = 0; j < m; j++)
            {
                inserts[j] = new DiffChange<T>(DiffChangeType.Insert, newItems[j], -1, j);
            }
            return inserts;
        }

        // Fast path: new is empty -> all deletes
        if (m == 0)
        {
            var deletes = new DiffChange<T>[n];
            for (var i = 0; i < n; i++)
            {
                deletes[i] = new DiffChange<T>(DiffChangeType.Delete, oldItems[i], i, -1);
            }
            return deletes;
        }

        // Common prefix optimization
        var prefixLen = 0;
        while (prefixLen < n && prefixLen < m && comparer.Equals(oldItems[prefixLen], newItems[prefixLen]))
        {
            prefixLen++;
        }

        // Common suffix optimization
        var suffixLen = 0;
        while (suffixLen < (n - prefixLen) && suffixLen < (m - prefixLen) &&
               comparer.Equals(oldItems[n - 1 - suffixLen], newItems[m - 1 - suffixLen]))
        {
            suffixLen++;
        }

        var result = new List<DiffChange<T>>(n + m);

        // Add common prefix
        for (var i = 0; i < prefixLen; i++)
        {
            result.Add(new DiffChange<T>(DiffChangeType.Keep, oldItems[i], i, i));
        }

        // Compute diff for middle portion
        var midN = n - prefixLen - suffixLen;
        var midM = m - prefixLen - suffixLen;

        if (midN > 0 || midM > 0)
        {
            var middleChanges = ComputeMiddle(oldItems, newItems, prefixLen, midN, midM, comparer, maxEditDistance);
            result.AddRange(middleChanges);
        }

        // Add common suffix
        for (var s = 0; s < suffixLen; s++)
        {
            var oldIdx = n - suffixLen + s;
            var newIdx = m - suffixLen + s;
            result.Add(new DiffChange<T>(DiffChangeType.Keep, oldItems[oldIdx], oldIdx, newIdx));
        }

        return result;
    }

    private static List<DiffChange<T>> ComputeMiddle<T>(
        IReadOnlyList<T> oldItems,
        IReadOnlyList<T> newItems,
        int prefixLen,
        int n,
        int m,
        IEqualityComparer<T> comparer,
        int? maxEditDistance)
    {
        if (n == 0)
        {
            var allInserts = new List<DiffChange<T>>(m);
            for (var j = 0; j < m; j++)
            {
                var newIdx = prefixLen + j;
                allInserts.Add(new DiffChange<T>(DiffChangeType.Insert, newItems[newIdx], -1, newIdx));
            }
            return allInserts;
        }

        if (m == 0)
        {
            var allDeletes = new List<DiffChange<T>>(n);
            for (var i = 0; i < n; i++)
            {
                var oldIdx = prefixLen + i;
                allDeletes.Add(new DiffChange<T>(DiffChangeType.Delete, oldItems[oldIdx], oldIdx, -1));
            }
            return allDeletes;
        }

        var max = n + m;
        var limit = Math.Min(max, maxEditDistance ?? DefaultMaxEditDistance);
        if (limit <= 0)
        {
            return CreateFallbackChanges(oldItems, newItems, prefixLen, n, m);
        }

        var vSize = 2 * limit + 3;
        var v = new int[vSize];
        var offset = limit + 1;
        var trace = new List<int[]>();

        v[offset + 1] = 0;

        int finalD = -1;
        for (var d = 0; d <= limit; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                {
                    x = v[offset + k + 1]; // Downward move = Insert
                }
                else
                {
                    x = v[offset + k - 1] + 1; // Rightward move = Delete
                }

                var y = x - k;

                // Follow diagonal snake
                while (x < n && y < m && comparer.Equals(oldItems[prefixLen + x], newItems[prefixLen + y]))
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    finalD = d;
                    break;
                }
            }

            // Only record the active k-window for step d: [-d, d] -> length 2*d + 1
            var vRecord = new int[2 * d + 1];
            Array.Copy(v, offset - d, vRecord, 0, 2 * d + 1);
            trace.Add(vRecord);

            if (finalD >= 0)
            {
                break;
            }
        }

        if (finalD < 0)
        {
            return CreateFallbackChanges(oldItems, newItems, prefixLen, n, m);
        }

        // Backtrack to find the edit path
        var changes = new List<DiffChange<T>>();
        var curX = n;
        var curY = m;

        for (var d = finalD; d > 0; d--)
        {
            var k = curX - curY;
            var prevV = trace[d - 1];

            int prevK;
            if (k == -d || (k != d && prevV[(k - 1) + (d - 1)] < prevV[(k + 1) + (d - 1)]))
            {
                prevK = k + 1;
            }
            else
            {
                prevK = k - 1;
            }

            var prevX = prevV[prevK + (d - 1)];
            var prevY = prevX - prevK;

            // Diagonal moves (snake)
            while (curX > prevX && curY > prevY)
            {
                curX--;
                curY--;
                changes.Add(new DiffChange<T>(DiffChangeType.Keep, oldItems[prefixLen + curX], prefixLen + curX, prefixLen + curY));
            }

            // Edit move
            if (curX > prevX)
            {
                // Delete
                curX--;
                changes.Add(new DiffChange<T>(DiffChangeType.Delete, oldItems[prefixLen + curX], prefixLen + curX, -1));
            }
            else if (curY > prevY)
            {
                // Insert
                curY--;
                changes.Add(new DiffChange<T>(DiffChangeType.Insert, newItems[prefixLen + curY], -1, prefixLen + curY));
            }
        }

        // Any remaining diagonal moves at d = 0
        while (curX > 0 && curY > 0)
        {
            curX--;
            curY--;
            changes.Add(new DiffChange<T>(DiffChangeType.Keep, oldItems[prefixLen + curX], prefixLen + curX, prefixLen + curY));
        }

        changes.Reverse();
        return changes;
    }

    private static List<DiffChange<T>> CreateFallbackChanges<T>(
        IReadOnlyList<T> oldItems,
        IReadOnlyList<T> newItems,
        int prefixLen,
        int n,
        int m)
    {
        var fallback = new List<DiffChange<T>>(n + m);
        for (var i = 0; i < n; i++)
        {
            var oldIdx = prefixLen + i;
            fallback.Add(new DiffChange<T>(DiffChangeType.Delete, oldItems[oldIdx], oldIdx, -1));
        }
        for (var j = 0; j < m; j++)
        {
            var newIdx = prefixLen + j;
            fallback.Add(new DiffChange<T>(DiffChangeType.Insert, newItems[newIdx], -1, newIdx));
        }
        return fallback;
    }
}

