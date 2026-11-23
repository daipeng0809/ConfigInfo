using System.Collections;
using System.Runtime.InteropServices;

namespace ConfigInfo;

/// <summary>
/// 有序键值对象：保持写入顺序、按序输出；键名区分大小写。
/// 提供点路径访问（如 "serial.frame.head"）与宽松类型读取。
/// </summary>
public sealed class ConfigObject : IEnumerable<KeyValuePair<string, ConfigValue>>
{
    private const int IndexThreshold = 12;

    private readonly List<Entry> _entries = new();
    private Dictionary<string, int>? _index;
    private int _version;

    private struct Entry
    {
        public string Key;
        public ConfigValue Value;
    }

    /// <summary>键值对数量。</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 按键名读写值。<paramref name="key"/> 为字面键名（不做路径拆解）。
    /// 写入是"存在则覆盖（保留原位置），不存在则追加"。
    /// 删除请调用 <see cref="Remove"/>；写入 null 请使用 <c>ConfigValue.Null</c>。
    /// </summary>
    public ConfigValue this[string key]
    {
        get => Get(key);
        set => Set(key, value);
    }

    /// <summary>读取字面键名对应的值，不存在时抛出 <see cref="KeyNotFoundException"/>。</summary>
    public ConfigValue Get(string key)
        => TryGetValue(key, out ConfigValue value)
            ? value
            : throw new KeyNotFoundException($"配置中不存在键 \"{key}\"。");

    /// <summary>尝试读取字面键名对应的值。</summary>
    public bool TryGetValue(string key, out ConfigValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_index is not null)
        {
            if (_index.TryGetValue(key, out int indexed))
            {
                value = _entries[indexed].Value;
                return true;
            }

            value = default;
            return false;
        }

        var entries = CollectionsMarshal.AsSpan(_entries);
        for (int i = 0; i < entries.Length; i++)
        {
            if (string.Equals(entries[i].Key, key, StringComparison.Ordinal))
            {
                value = entries[i].Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>设置键值（存在则覆盖并保留原有位置，不存在则追加到末尾）。</summary>
    public void Set(string key, ConfigValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _version++;
        if (TryFindIndex(key, out int existing))
        {
            var entry = _entries[existing];
            entry.Value = value;
            _entries[existing] = entry;
            return;
        }

        _entries.Add(new Entry { Key = key, Value = value });
        if (_index is not null)
            _index[key] = _entries.Count - 1;
        else if (_entries.Count > IndexThreshold)
            BuildIndex();
    }

    /// <summary>添加键值；键已存在时抛出 <see cref="ArgumentException"/>。</summary>
    public void Add(string key, ConfigValue value)
    {
        if (ContainsKey(key))
            throw new ArgumentException($"配置中已存在键 \"{key}\"。", nameof(key));
        Set(key, value);
    }

    /// <summary>是否包含指定键。</summary>
    public bool ContainsKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryFindIndex(key, out _);
    }

    /// <summary>删除指定键，返回是否删除成功。</summary>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!TryFindIndex(key, out int index))
            return false;
        _entries.RemoveAt(index);
        _index = null; // 索引失效，下次超过阈值时重建
        _version++;
        return true;
    }

    /// <summary>清空所有键值。</summary>
    public void Clear()
    {
        _entries.Clear();
        _index = null;
        _version++;
    }

    private bool TryFindIndex(string key, out int index)
    {
        if (_index is not null)
            return _index.TryGetValue(key, out index);

        var entries = CollectionsMarshal.AsSpan(_entries);
        for (int i = 0; i < entries.Length; i++)
        {
            if (string.Equals(entries[i].Key, key, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private void BuildIndex()
    {
        _index = new Dictionary<string, int>(_entries.Count, StringComparer.Ordinal);
        for (int i = 0; i < _entries.Count; i++)
            _index[_entries[i].Key] = i;
    }

    // ===== 宽松读取（缺失或类型不可转换时返回默认值） =====

    /// <summary>
    /// 读取用：优先按字面键名查找；不存在时再按点路径回退查找（如 "serial.baudRate"）。
    /// </summary>
    private bool TryGetValueOrPath(string keyOrPath, out ConfigValue value)
    {
        if (TryGetValue(keyOrPath, out value))
            return true;
        return keyOrPath.AsSpan().IndexOf('.') >= 0 && TryGetPath(keyOrPath, out value);
    }

    /// <summary>读取字符串；缺失、为 null 或不可转换时返回 <paramref name="defaultValue"/>。支持点路径。</summary>
    public string GetString(string key, string defaultValue = "")
        => TryGetValueOrPath(key, out ConfigValue value) && value.TryConvertToString(out string? text)
            ? text!
            : defaultValue;

    /// <summary>读取布尔值；缺失或不可转换时返回 <paramref name="defaultValue"/>。支持点路径。</summary>
    public bool GetBoolean(string key, bool defaultValue = false)
        => TryGetValueOrPath(key, out ConfigValue value) && value.TryConvertToBoolean(out bool result) ? result : defaultValue;

    /// <summary>读取 32 位整数；缺失、不可转换或超出范围时返回 <paramref name="defaultValue"/>。支持点路径。</summary>
    public int GetInt32(string key, int defaultValue = 0)
        => TryGetValueOrPath(key, out ConfigValue value)
           && value.TryConvertToInt64(out long number)
           && number >= int.MinValue
           && number <= int.MaxValue
            ? (int)number
            : defaultValue;

    /// <summary>读取 64 位整数；缺失或不可转换时返回 <paramref name="defaultValue"/>。支持点路径。</summary>
    public long GetInt64(string key, long defaultValue = 0)
        => TryGetValueOrPath(key, out ConfigValue value) && value.TryConvertToInt64(out long number) ? number : defaultValue;

    /// <summary>读取双精度浮点；缺失或不可转换时返回 <paramref name="defaultValue"/>。支持点路径。</summary>
    public double GetDouble(string key, double defaultValue = 0d)
        => TryGetValueOrPath(key, out ConfigValue value) && value.TryConvertToDouble(out double number) ? number : defaultValue;

    /// <summary>读取单精度浮点；缺失或不可转换时返回 <paramref name="defaultValue"/>。支持点路径。</summary>
    public float GetSingle(string key, float defaultValue = 0f) => (float)GetDouble(key, defaultValue);

    /// <summary>读取 decimal；缺失或不可转换时返回 <paramref name="defaultValue"/>。支持点路径。</summary>
    public decimal GetDecimal(string key, decimal defaultValue = 0m)
        => TryGetValueOrPath(key, out ConfigValue value) && value.TryConvertToDecimal(out decimal number) ? number : defaultValue;

    // ===== 点路径访问（键名本身含 '.' 时请使用 Get/Set 字面量接口） =====

    /// <summary>按点路径读取值，不存在时抛出 <see cref="KeyNotFoundException"/>。</summary>
    public ConfigValue GetPath(string path)
        => TryGetPath(path, out ConfigValue value)
            ? value
            : throw new KeyNotFoundException($"配置中不存在路径 \"{path}\"。");

    /// <summary>尝试按点路径读取值。</summary>
    public bool TryGetPath(string path, out ConfigValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        ConfigObject node = this;
        ReadOnlySpan<char> remaining = path.AsSpan();
        while (true)
        {
            int dot = remaining.IndexOf('.');
            ReadOnlySpan<char> segment = dot < 0 ? remaining : remaining[..dot];
            if (segment.IsEmpty)
                return false;

            if (!node.TryGetValue(segment.ToString(), out value))
                return false;
            if (dot < 0)
                return true;
            if (value.Kind != ConfigValueKind.Object)
            {
                value = default;
                return false;
            }

            node = value.AsObject();
            remaining = remaining[(dot + 1)..];
        }
    }

    /// <summary>按点路径写入值（中间层不存在时自动创建对象）。</summary>
    public void SetPath(string path, ConfigValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        int lastDot = path.LastIndexOf('.');
        if (lastDot == path.Length - 1)
            throw new ArgumentException($"配置路径 \"{path}\" 以 '.' 结尾。", nameof(path));
        if (lastDot < 0)
        {
            Set(path, value);
            return;
        }

        GetObject(path[..lastDot]).Set(path[(lastDot + 1)..], value);
    }

    /// <summary>
    /// 按点路径获取对象（不存在则逐级创建；已存在且为 null 时替换为对象）。
    /// 若路径中间或末端的键已存在但既不是对象也不是 null，将抛出 <see cref="InvalidOperationException"/>（不静默覆盖已有数据）。
    /// </summary>
    public ConfigObject GetObject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ConfigObject node = this;
        ReadOnlySpan<char> remaining = path.AsSpan();
        while (true)
        {
            int dot = remaining.IndexOf('.');
            ReadOnlySpan<char> segment = dot < 0 ? remaining : remaining[..dot];
            if (segment.IsEmpty)
                throw new ArgumentException($"配置路径 \"{path}\" 中存在空段。", nameof(path));

            node = node.EnterObject(segment.ToString(), path);
            if (dot < 0)
                return node;
            remaining = remaining[(dot + 1)..];
        }
    }

    private ConfigObject EnterObject(string key, string path)
    {
        if (TryGetValue(key, out ConfigValue existing))
        {
            if (existing.Kind == ConfigValueKind.Object)
                return existing.AsObject();
            if (existing.IsNull)
            {
                var created = new ConfigObject();
                Set(key, new ConfigValue(created));
                return created;
            }

            throw new InvalidOperationException(
                $"路径 \"{path}\" 中的键 \"{key}\" 已存在且为 {existing.Kind}，无法作为分组对象。请先 Remove 该键。");
        }

        var node = new ConfigObject();
        Set(key, new ConfigValue(node));
        return node;
    }

    /// <summary>
    /// 按点路径获取数组（不存在则创建；已存在且为 null 时替换为数组）。
    /// 键已存在但既不是数组也不是 null 时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public ConfigArray GetArray(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        int lastDot = path.LastIndexOf('.');
        if (lastDot == path.Length - 1)
            throw new ArgumentException($"配置路径 \"{path}\" 以 '.' 结尾。", nameof(path));

        ConfigObject parent = lastDot < 0 ? this : GetObject(path[..lastDot]);
        string name = lastDot < 0 ? path : path[(lastDot + 1)..];
        if (parent.TryGetValue(name, out ConfigValue existing))
        {
            if (existing.Kind == ConfigValueKind.Array)
                return existing.AsArray();
            if (existing.IsNull)
            {
                var created = new ConfigArray();
                parent.Set(name, new ConfigValue(created));
                return created;
            }

            throw new InvalidOperationException(
                $"路径 \"{path}\" 中的键 \"{name}\" 已存在且为 {existing.Kind}，无法作为数组。请先 Remove 该键。");
        }

        var array = new ConfigArray();
        parent.Set(name, new ConfigValue(array));
        return array;
    }

    /// <summary>按写入顺序枚举键值对（struct 枚举器，foreach 无额外分配）。</summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<string, ConfigValue>> IEnumerable<KeyValuePair<string, ConfigValue>>.GetEnumerator()
        => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>有序键值对的 struct 枚举器。枚举期间修改对象会抛出 <see cref="InvalidOperationException"/>。</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<string, ConfigValue>>
    {
        private readonly ConfigObject _owner;
        private readonly int _version;
        private int _index;
        private KeyValuePair<string, ConfigValue> _current;

        internal Enumerator(ConfigObject owner)
        {
            _owner = owner;
            _version = owner._version;
            _index = -1;
            _current = default;
        }

        public KeyValuePair<string, ConfigValue> Current => _current;

        object IEnumerator.Current => _current;

        public bool MoveNext()
        {
            if (_version != _owner._version)
                throw new InvalidOperationException("枚举期间 ConfigObject 已被修改，请重新获取枚举器。");

            if (_index + 1 < _owner._entries.Count)
            {
                _index++;
                Entry entry = _owner._entries[_index];
                _current = new KeyValuePair<string, ConfigValue>(entry.Key, entry.Value);
                return true;
            }

            return false;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}
