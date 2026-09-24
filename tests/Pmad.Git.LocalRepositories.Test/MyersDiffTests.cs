using System.Collections.Generic;
using System.Linq;
using Pmad.Git.LocalRepositories.Diff;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public class MyersDiffTests
{
    [Fact]
    public void Compute_EmptySequences_ReturnsEmpty()
    {
        var result = MyersDiff.Compute(System.Array.Empty<string>(), System.Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void Compute_OldEmpty_ReturnsAllInserts()
    {
        var oldItems = System.Array.Empty<string>();
        var newItems = new[] { "a", "b", "c" };

        var result = MyersDiff.Compute(oldItems, newItems);

        Assert.Equal(3, result.Count);
        Assert.All(result, c => Assert.Equal(DiffChangeType.Insert, c.Type));
        Assert.Equal(new[] { "a", "b", "c" }, result.Select(c => c.Item));
        Assert.Equal(new[] { 0, 1, 2 }, result.Select(c => c.NewIndex));
        Assert.All(result, c => Assert.Equal(-1, c.OldIndex));
    }

    [Fact]
    public void Compute_NewEmpty_ReturnsAllDeletes()
    {
        var oldItems = new[] { "a", "b", "c" };
        var newItems = System.Array.Empty<string>();

        var result = MyersDiff.Compute(oldItems, newItems);

        Assert.Equal(3, result.Count);
        Assert.All(result, c => Assert.Equal(DiffChangeType.Delete, c.Type));
        Assert.Equal(new[] { "a", "b", "c" }, result.Select(c => c.Item));
        Assert.Equal(new[] { 0, 1, 2 }, result.Select(c => c.OldIndex));
        Assert.All(result, c => Assert.Equal(-1, c.NewIndex));
    }

    [Fact]
    public void Compute_IdenticalSequences_ReturnsAllKeeps()
    {
        var items = new[] { "line1", "line2", "line3" };

        var result = MyersDiff.Compute(items, items);

        Assert.Equal(3, result.Count);
        Assert.All(result, c => Assert.Equal(DiffChangeType.Keep, c.Type));
        Assert.Equal(new[] { "line1", "line2", "line3" }, result.Select(c => c.Item));
    }

    [Fact]
    public void Compute_SingleModification_ReturnsDeleteAndInsert()
    {
        var oldItems = new[] { "line1", "oldLine", "line3" };
        var newItems = new[] { "line1", "newLine", "line3" };

        var result = MyersDiff.Compute(oldItems, newItems);

        Assert.Equal(4, result.Count);
        Assert.Equal(DiffChangeType.Keep, result[0].Type);
        Assert.Equal("line1", result[0].Item);

        Assert.Equal(DiffChangeType.Delete, result[1].Type);
        Assert.Equal("oldLine", result[1].Item);

        Assert.Equal(DiffChangeType.Insert, result[2].Type);
        Assert.Equal("newLine", result[2].Item);

        Assert.Equal(DiffChangeType.Keep, result[3].Type);
        Assert.Equal("line3", result[3].Item);
    }

    [Fact]
    public void Compute_ClassicMyersExample_ShortestEditScript()
    {
        // Classic example from Myers paper: A B C A B B A -> C B A B A C
        var oldChars = "ABCABBA".Select(c => c.ToString()).ToArray();
        var newChars = "CBABAC".Select(c => c.ToString()).ToArray();

        var result = MyersDiff.Compute(oldChars, newChars);

        // Reconstruct newChars from oldChars using result
        var reconstructed = new List<string>();
        foreach (var change in result)
        {
            if (change.Type == DiffChangeType.Keep || change.Type == DiffChangeType.Insert)
            {
                reconstructed.Add(change.Item);
            }
        }
        Assert.Equal(newChars, reconstructed);

        // Validate that kept items match the actual characters at OldIndex and NewIndex
        var keepChanges = result.Where(c => c.Type == DiffChangeType.Keep).ToList();
        Assert.NotEmpty(keepChanges);
        foreach (var keep in keepChanges)
        {
            Assert.InRange(keep.OldIndex, 0, oldChars.Length - 1);
            Assert.InRange(keep.NewIndex, 0, newChars.Length - 1);
            Assert.Equal(keep.Item, oldChars[keep.OldIndex]);
            Assert.Equal(keep.Item, newChars[keep.NewIndex]);
        }

        // Validate delete and insert indices against input sequences
        foreach (var del in result.Where(c => c.Type == DiffChangeType.Delete))
        {
            Assert.Equal(-1, del.NewIndex);
            Assert.InRange(del.OldIndex, 0, oldChars.Length - 1);
            Assert.Equal(del.Item, oldChars[del.OldIndex]);
        }

        foreach (var ins in result.Where(c => c.Type == DiffChangeType.Insert))
        {
            Assert.Equal(-1, ins.OldIndex);
            Assert.InRange(ins.NewIndex, 0, newChars.Length - 1);
            Assert.Equal(ins.Item, newChars[ins.NewIndex]);
        }
    }

    [Fact]
    public void Compute_CustomComparer_CaseInsensitive()
    {
        var oldItems = new[] { "HELLO", "WORLD" };
        var newItems = new[] { "hello", "world" };

        var result = MyersDiff.Compute(oldItems, newItems, System.StringComparer.OrdinalIgnoreCase);

        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal(DiffChangeType.Keep, c.Type));
    }

    [Fact]
    public void Compute_LargeFilesWithModifications_AllocatesUnder50MB()
    {
        const int lineCount = 10000;
        var oldLines = new string[lineCount];
        var newLines = new string[lineCount];

        for (var i = 0; i < lineCount; i++)
        {
            oldLines[i] = $"line_{i:D6}_common_content_payload";
            // 10% extensive modifications (1,000 modified lines across the file)
            newLines[i] = (i % 10 == 0)
                ? $"line_{i:D6}_modified_content_payload"
                : oldLines[i];
        }

        // Warm up and force full GC before measuring allocation
        _ = MyersDiff.Compute(new[] { "a" }, new[] { "b" });
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();

        var allocatedBefore = System.GC.GetAllocatedBytesForCurrentThread();
        var diff = MyersDiff.Compute(oldLines, newLines);
        var allocatedAfter = System.GC.GetAllocatedBytesForCurrentThread();

        var totalAllocated = allocatedAfter - allocatedBefore;
        const long maxAllowedBytes = 50L * 1024 * 1024; // 50 MB

        Assert.True(
            totalAllocated < maxAllowedBytes,
            $"Total allocation was {totalAllocated / (1024.0 * 1024.0):F2} MB, which exceeds the 50 MB limit.");

        // Verify diff validity by reconstructing newLines
        var reconstructed = new List<string>();
        foreach (var change in diff)
        {
            if (change.Type == DiffChangeType.Keep || change.Type == DiffChangeType.Insert)
            {
                reconstructed.Add(change.Item);
            }
        }
        Assert.Equal(newLines, reconstructed);
    }

    [Fact]
    public void Compute_ExceedingMaxEditDistance_FallsBackGracefully()
    {
        const int lineCount = 2000;
        var oldLines = new string[lineCount];
        var newLines = new string[lineCount];

        for (var i = 0; i < lineCount; i++)
        {
            oldLines[i] = $"old_line_{i}";
            newLines[i] = $"new_line_{i}";
        }

        // Limit edit distance to 50 edits
        var diff = MyersDiff.Compute(oldLines, newLines, maxEditDistance: 50);

        Assert.NotEmpty(diff);
        // Fallback replaces middle: all deletes then all inserts
        Assert.Contains(diff, c => c.Type == DiffChangeType.Delete);
        Assert.Contains(diff, c => c.Type == DiffChangeType.Insert);

        // Verify reconstruction produces newLines
        var reconstructed = new List<string>();
        foreach (var change in diff)
        {
            if (change.Type == DiffChangeType.Keep || change.Type == DiffChangeType.Insert)
            {
                reconstructed.Add(change.Item);
            }
        }
        Assert.Equal(newLines, reconstructed);
    }
}


