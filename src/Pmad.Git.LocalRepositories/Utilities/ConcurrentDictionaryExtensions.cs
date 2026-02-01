using System.Collections.Concurrent;

namespace Pmad.Git.LocalRepositories.Utilities;

internal static class ConcurrentDictionaryExtensions
{
    /// <summary>
    /// Gets the value associated with the specified key, or adds a new value created by the specified factory function
    /// if the key does not exist, ensuring that only a single value is created for each key.
    /// </summary>
    /// <remarks>Unlike the standard GetOrAdd method, this method guarantees that only one value is created
    /// per key, even if multiple threads attempt to add the same key concurrently. This may introduce contention if
    /// used with a high degree of parallelism.</remarks>
    /// <typeparam name="TKey">The type of keys in the dictionary. Must not be null.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    /// <param name="dictionary">The concurrent dictionary to search for the key or to add the value to. Cannot be null.</param>
    /// <param name="key">The key whose value to get or add.</param>
    /// <param name="valueFactory">A function used to generate a value for the specified key if it does not exist. Cannot be null.</param>
    /// <returns>The value associated with the specified key. If the key does not exist, the value created by the factory
    /// function is added and returned.</returns>
    public static TValue GetOrAddSingleton<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TValue> valueFactory)
        where TKey : notnull
    {
        if (dictionary.TryGetValue(key, out var existingValue))
        {
            return existingValue;
        }

        // GetOrAdd is atomic, but does not guarantee singleton values.
        // Concurrent calls with the same key may result in multiple values being created.
        // To ensure singleton values, we use a lock here.
        lock (dictionary)
        {
            return dictionary.GetOrAdd(key, valueFactory);
        }
    }

}
