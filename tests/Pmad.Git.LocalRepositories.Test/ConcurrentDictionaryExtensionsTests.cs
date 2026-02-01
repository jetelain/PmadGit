using System.Collections.Concurrent;
using Pmad.Git.LocalRepositories.Utilities;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class ConcurrentDictionaryExtensionsTests
{
    [Fact]
    public void GetOrAddSingleton_ReturnsExistingValue()
    {
        var dict = new ConcurrentDictionary<string, int>();
        dict["a"] = 42;

        var value = dict.GetOrAddSingleton("a", _ => 100);

        Assert.Equal(42, value);
    }

    [Fact]
    public async Task GetOrAddSingleton_CreatesSingleValueUnderConcurrency()
    {
        var dict = new ConcurrentDictionary<int, object>();
        var created = 0;

        object Factory(int key)
        {
            Interlocked.Increment(ref created);
            // small delay to make races more likely
            Thread.Sleep(10);
            return new object();
        }

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => dict.GetOrAddSingleton(1, Factory))).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, created);
        Assert.Equal(await tasks[0], dict[1]);
    }

    [Fact]
    public async Task GetOrAddSingleton_AllowsMultipleKeysInParallel()
    {
        var dict = new ConcurrentDictionary<int, int>();

        int Factory(int k)
        {
            // return distinct value for key
            return k * 10;
        }

        var tasks = Enumerable.Range(1, 10).Select(k => Task.Run(() => dict.GetOrAddSingleton(k, Factory))).ToArray();
        await Task.WhenAll(tasks);

        for (int k = 1; k <= 10; k++)
        {
            Assert.Equal(k * 10, dict[k]);
        }
    }
}
