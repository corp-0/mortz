using System.Collections;

namespace Mortz.Core.Collections;

/// <summary>A live read-only view of values owned by another dictionary.</summary>
public class DictionaryProjection<TKey, TEntry, TValue>(
    IReadOnlyDictionary<TKey, TEntry> entries, Func<TEntry, TValue> project)
    : IReadOnlyDictionary<TKey, TValue> where TKey : notnull
{
    public TValue this[TKey key] => project(entries[key]);
    public int Count => entries.Count;
    public IEnumerable<TKey> Keys => entries.Keys;
    public IEnumerable<TValue> Values => entries.Values.Select(project);
    public bool ContainsKey(TKey key) => entries.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (entries.TryGetValue(key, out TEntry? entry))
        {
            value = project(entry);
            return true;
        }
        value = default!;
        return false;
    }
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => entries
        .Select(pair => new KeyValuePair<TKey, TValue>(pair.Key, project(pair.Value))).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
