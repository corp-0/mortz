namespace Mortz.Client.Announcements;

/// <summary>Random variant per key, never the same one twice in a row.</summary>
public sealed class VariantPicker
{
    private readonly Random _random;
    private readonly Dictionary<object, int> _lastByKey = new();

    public VariantPicker(Random? random = null) => _random = random ?? new Random();

    public int Next(object key, int count)
    {
        if (count <= 1)
            return 0;
        int index;
        if (_lastByKey.TryGetValue(key, out int last) && last < count)
        {
            index = _random.Next(count - 1);
            if (index >= last)
                index++;
        }
        else
        {
            index = _random.Next(count);
        }
        _lastByKey[key] = index;
        return index;
    }
}
