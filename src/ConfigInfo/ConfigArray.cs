using System.Collections;

namespace ConfigInfo;

/// <summary>
/// 有序数组，元素可以是任意 <see cref="ConfigValue"/>（标量、数组或对象）。
/// </summary>
public sealed class ConfigArray : IList<ConfigValue>, IReadOnlyList<ConfigValue>
{
    private readonly List<ConfigValue> _items = new();

    /// <summary>元素数量。</summary>
    public int Count => _items.Count;

    public bool IsReadOnly => false;

    /// <summary>按索引读写元素。</summary>
    public ConfigValue this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public void Add(ConfigValue item) => _items.Add(item);

    public void AddRange(IEnumerable<ConfigValue> items) => _items.AddRange(items);

    public void Insert(int index, ConfigValue item) => _items.Insert(index, item);

    public bool Remove(ConfigValue item) => _items.Remove(item);

    public void RemoveAt(int index) => _items.RemoveAt(index);

    public void Clear() => _items.Clear();

    public bool Contains(ConfigValue item) => _items.Contains(item);

    public int IndexOf(ConfigValue item) => _items.IndexOf(item);

    public void CopyTo(ConfigValue[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    // ===== 宽松读取 =====

    /// <summary>读取索引处的 32 位整数；越界或不可转换时返回 <paramref name="defaultValue"/>。</summary>
    public int GetInt32(int index, int defaultValue = 0)
        => index >= 0
           && index < _items.Count
           && _items[index].TryConvertToInt64(out long number)
           && number >= int.MinValue
           && number <= int.MaxValue
            ? (int)number
            : defaultValue;

    /// <summary>读取索引处的 64 位整数；越界或不可转换时返回 <paramref name="defaultValue"/>。</summary>
    public long GetInt64(int index, long defaultValue = 0)
        => index >= 0 && index < _items.Count && _items[index].TryConvertToInt64(out long number) ? number : defaultValue;

    /// <summary>读取索引处的浮点数；越界或不可转换时返回 <paramref name="defaultValue"/>。</summary>
    public double GetDouble(int index, double defaultValue = 0d)
        => index >= 0 && index < _items.Count && _items[index].TryConvertToDouble(out double number) ? number : defaultValue;

    /// <summary>读取索引处的单精度浮点；越界或不可转换时返回 <paramref name="defaultValue"/>。</summary>
    public float GetSingle(int index, float defaultValue = 0f) => (float)GetDouble(index, defaultValue);

    /// <summary>读取索引处的 decimal；越界或不可转换时返回 <paramref name="defaultValue"/>。</summary>
    public decimal GetDecimal(int index, decimal defaultValue = 0m)
        => index >= 0 && index < _items.Count && _items[index].TryConvertToDecimal(out decimal number)
            ? number
            : defaultValue;

    /// <summary>读取索引处的布尔值；越界或不可转换时返回 <paramref name="defaultValue"/>。</summary>
    public bool GetBoolean(int index, bool defaultValue = false)
        => index >= 0 && index < _items.Count && _items[index].TryConvertToBoolean(out bool result) ? result : defaultValue;

    /// <summary>读取索引处的字符串；越界、为 null 或不可转换时返回 <paramref name="defaultValue"/>。</summary>
    public string GetString(int index, string defaultValue = "")
        => index >= 0 && index < _items.Count && _items[index].TryConvertToString(out string? text)
            ? text!
            : defaultValue;

    // ===== 批量转换（不可转换的元素取默认值 0 / 空串） =====

    /// <summary>转换为 int 数组。</summary>
    public int[] ToInt32Array()
    {
        var result = new int[_items.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = GetInt32(i);
        return result;
    }

    /// <summary>转换为 long 数组。</summary>
    public long[] ToInt64Array()
    {
        var result = new long[_items.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = GetInt64(i);
        return result;
    }

    /// <summary>转换为 double 数组。</summary>
    public double[] ToDoubleArray()
    {
        var result = new double[_items.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = GetDouble(i);
        return result;
    }

    /// <summary>转换为 string 数组。</summary>
    public string[] ToStringArray()
    {
        var result = new string[_items.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = GetString(i);
        return result;
    }

    /// <summary>按顺序枚举元素（struct 枚举器，foreach 无额外分配）。</summary>
    public List<ConfigValue>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<ConfigValue> IEnumerable<ConfigValue>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
