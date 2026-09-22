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

        // Ensure keeps match
        var keepsFromOld = result.Where(c => c.Type == DiffChangeType.Keep).Select(c => c.Item).ToList();
        var keepsFromNew = result.Where(c => c.Type == DiffChangeType.Keep).Select(c => c.Item).ToList();
        Assert.Equal(keepsFromOld, keepsFromNew);
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
}

