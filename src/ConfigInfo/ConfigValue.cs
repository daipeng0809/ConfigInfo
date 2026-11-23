using System.Globalization;

namespace ConfigInfo;

/// <summary>
/// 配置值：一个不产生装箱的轻量结构体，可承载 null / 布尔 / 整数 / 浮点 / 字符串 / 数组 / 对象。
/// 提供宽松转换（如字符串 "115200" 可直接读取为 int），便于容忍手写配置。
/// </summary>
/// <remarks>
/// 存储约定：整数统一为 64 位有符号整数；浮点统一为 <see cref="double"/>；
/// <see cref="decimal"/> 会先转成 <see cref="double"/>（有效数字超过 15~17 位时可能丢精度）；
/// <see cref="ulong"/> 以补码位模式存入，大于 <see cref="long.MaxValue"/> 的值用 <see cref="AsInt64"/> 读回为负数，请改用 <see cref="AsUInt64"/>。
/// </remarks>
public readonly struct ConfigValue : IEquatable<ConfigValue>
{
    private readonly ConfigValueKind _kind;
    private readonly long _number;       // Integer / Boolean(0|1) / Float(位模式)
    private readonly string? _text;      // String
    private readonly object? _reference; // ConfigObject / ConfigArray

    private ConfigValue(ConfigValueKind kind, long number, string? text, object? reference)
    {
        _kind = kind;
        _number = number;
        _text = text;
        _reference = reference;
    }

    // ===== 构造函数 =====

    public ConfigValue(bool value) : this(ConfigValueKind.Boolean, value ? 1L : 0L, null, null) { }
    public ConfigValue(long value) : this(ConfigValueKind.Integer, value, null, null) { }
    public ConfigValue(int value) : this((long)value) { }
    public ConfigValue(uint value) : this((long)value) { }
    public ConfigValue(short value) : this((long)value) { }
    public ConfigValue(ushort value) : this((long)value) { }
    public ConfigValue(byte value) : this((long)value) { }
    public ConfigValue(sbyte value) : this((long)value) { }
    public ConfigValue(ulong value) : this(unchecked((long)value)) { }

    public ConfigValue(double value) : this(ConfigValueKind.Float, BitConverter.DoubleToInt64Bits(value), null, null) { }
    public ConfigValue(float value) : this((double)value) { }
    public ConfigValue(decimal value) : this((double)value) { }

    public ConfigValue(string? value)
        : this(value is null ? ConfigValueKind.Null : ConfigValueKind.String, 0L, value, null) { }

    public ConfigValue(ConfigArray value)
        : this(ConfigValueKind.Array, 0L, null, value ?? throw new ArgumentNullException(nameof(value))) { }

    public ConfigValue(ConfigObject value)
        : this(ConfigValueKind.Object, 0L, null, value ?? throw new ArgumentNullException(nameof(value))) { }

    // ===== 隐式转换：让赋值写起来接近 JSON / C# =====
    // 每个数值类型都直接定义，避免 byte → int/long 组合产生的转换二义性（CS0457）

    public static implicit operator ConfigValue(bool value) => new(value);
    public static implicit operator ConfigValue(byte value) => new(value);
    public static implicit operator ConfigValue(sbyte value) => new(value);
    public static implicit operator ConfigValue(short value) => new(value);
    public static implicit operator ConfigValue(ushort value) => new(value);
    public static implicit operator ConfigValue(int value) => new(value);
    public static implicit operator ConfigValue(uint value) => new(value);
    public static implicit operator ConfigValue(long value) => new(value);
    public static implicit operator ConfigValue(ulong value) => new(value);
    public static implicit operator ConfigValue(float value) => new(value);
    public static implicit operator ConfigValue(double value) => new(value);
    public static implicit operator ConfigValue(decimal value) => new(value);
    public static implicit operator ConfigValue(string? value) => new(value);
    public static implicit operator ConfigValue(ConfigArray value) => new(value);
    public static implicit operator ConfigValue(ConfigObject value) => new(value);

    // ===== 类型判断 =====

    /// <summary>值的种类。</summary>
    public ConfigValueKind Kind => _kind;

    /// <summary>是否为 null。</summary>
    public bool IsNull => _kind == ConfigValueKind.Null;

    /// <summary>是否为标量（null / 布尔 / 整数 / 浮点 / 字符串）。</summary>
    public bool IsScalar => _kind <= ConfigValueKind.String;

    public bool IsBoolean => _kind == ConfigValueKind.Boolean;
    public bool IsInteger => _kind == ConfigValueKind.Integer;
    public bool IsFloat => _kind == ConfigValueKind.Float;
    public bool IsString => _kind == ConfigValueKind.String;
    public bool IsArray => _kind == ConfigValueKind.Array;
    public bool IsObject => _kind == ConfigValueKind.Object;

    /// <summary>null 值单例。</summary>
    public static ConfigValue Null => default;

    // ===== 强类型读取（不可转换抛 InvalidCastException；数值溢出抛 OverflowException） =====

    public bool AsBoolean()
        => TryConvertToBoolean(out bool result) ? result : throw CannotConvert("布尔值");

    public long AsInt64()
        => TryConvertToInt64(out long result) ? result : throw CannotConvert("64 位整数");

    /// <exception cref="InvalidCastException">值无法转换为整数。</exception>
    /// <exception cref="OverflowException">值超出 <see cref="int"/> 范围。</exception>
    public int AsInt32() => checked((int)AsInt64());
    public short AsInt16() => checked((short)AsInt64());
    public sbyte AsSByte() => checked((sbyte)AsInt64());
    public byte AsByte() => checked((byte)AsInt64());
    public ushort AsUInt16() => checked((ushort)AsInt64());
    public uint AsUInt32() => checked((uint)AsInt64());

    /// <summary>按 64 位补码位模式读取无符号整数（-1 读回 <see cref="ulong.MaxValue"/>）。</summary>
    public ulong AsUInt64() => unchecked((ulong)AsInt64());

    public double AsDouble()
        => TryConvertToDouble(out double result) ? result : throw CannotConvert("浮点数");

    public float AsSingle() => (float)AsDouble();
    public decimal AsDecimal() => TryConvertToDecimal(out decimal result) ? result : throw CannotConvert("decimal");

    public string AsString()
        => TryConvertToString(out string? result) ? result! : throw CannotConvert("字符串");

    public ConfigArray AsArray()
        => _reference as ConfigArray ?? throw CannotConvert("数组");

    public ConfigObject AsObject()
        => _reference as ConfigObject ?? throw CannotConvert("对象");

    private InvalidCastException CannotConvert(string target)
        => new($"配置值（{_kind}）无法转换为{target}。");

    // ===== 宽松转换（供 ConfigObject / ConfigArray 的 Get 系列使用） =====

    internal bool TryConvertToBoolean(out bool value)
    {
        switch (_kind)
        {
            case ConfigValueKind.Boolean:
            case ConfigValueKind.Integer:
                value = _number != 0;
                return true;
            case ConfigValueKind.Float:
                value = BitConverter.Int64BitsToDouble(_number) != 0d;
                return true;
            case ConfigValueKind.String:
                return TryParseBoolean(_text!.AsSpan(), out value);
            default:
                value = false;
                return false;
        }
    }

    internal bool TryConvertToInt64(out long value)
    {
        switch (_kind)
        {
            case ConfigValueKind.Integer:
            case ConfigValueKind.Boolean:
                value = _number;
                return true;
            case ConfigValueKind.Float:
                double d = BitConverter.Int64BitsToDouble(_number);
                // (double)long.MaxValue 等于 2^63，本身已超出 long 范围，必须用严格小于；
                // 否则 d == 2^63 会被静默截断为 long.MinValue。
                if (!double.IsNaN(d) && d >= (double)long.MinValue && d < (double)long.MaxValue)
                {
                    value = (long)d;
                    return true;
                }

                value = 0;
                return false;
            case ConfigValueKind.String:
                return TryParseInteger(_text!.AsSpan(), out value);
            default:
                value = 0;
                return false;
        }
    }

    internal bool TryConvertToDouble(out double value)
    {
        switch (_kind)
        {
            case ConfigValueKind.Float:
                value = BitConverter.Int64BitsToDouble(_number);
                return true;
            case ConfigValueKind.Integer:
            case ConfigValueKind.Boolean:
                value = _number;
                return true;
            case ConfigValueKind.String:
                return double.TryParse(_text!.AsSpan(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                value = 0;
                return false;
        }
    }

    internal bool TryConvertToDecimal(out decimal value)
    {
        switch (_kind)
        {
            case ConfigValueKind.Integer:
            case ConfigValueKind.Boolean:
                value = _number;
                return true;
            case ConfigValueKind.Float:
                double d = BitConverter.Int64BitsToDouble(_number);
                // (double)decimal.MaxValue / MinValue 恰好落在 ±2^96，已越过 decimal 的边界，
                // 必须使用严格不等号；否则边界值会让 (decimal)d 抛 OverflowException。
                if (!double.IsNaN(d) && d > (double)decimal.MinValue && d < (double)decimal.MaxValue)
                {
                    value = (decimal)d;
                    return true;
                }

                value = 0;
                return false;
            case ConfigValueKind.String:
                return decimal.TryParse(_text!.AsSpan(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                value = 0;
                return false;
        }
    }

    internal bool TryConvertToString(out string? value)
    {
        switch (_kind)
        {
            case ConfigValueKind.String:
                value = _text;
                return true;
            case ConfigValueKind.Null:
                // null 表示"没有值"：读取字符串时回退到调用方给的默认值，而不是空串
                value = null;
                return false;
            case ConfigValueKind.Boolean:
                value = _number != 0 ? "true" : "false";
                return true;
            case ConfigValueKind.Integer:
                value = _number.ToString(CultureInfo.InvariantCulture);
                return true;
            case ConfigValueKind.Float:
                value = FormatDouble(BitConverter.Int64BitsToDouble(_number));
                return true;
            default:
                value = null;
                return false;
        }
    }

    // ===== 文本解析辅助（解析器与宽松转换共用） =====

    /// <summary>
    /// 解析整数文本：支持正负号、十进制、0x / 0b / 0o 前缀以及 "_" 数字分隔符。
    /// "_" 只能出现在两个数字之间（"1_000" 合法，"1_" / "1__0" / "0x_1" 不合法）。
    /// 0x / 0b / 0o 前缀按 64 位无符号位模式解释：超过 64 位的字面量解析失败，最高位为 1 时得到负数。
    /// </summary>
    internal static bool TryParseInteger(ReadOnlySpan<char> span, out long value)
    {
        span = span.Trim();
        value = 0;
        if (span.IsEmpty)
            return false;

        int index = 0;
        bool negative = false;
        if (span[0] == '+' || span[0] == '-')
        {
            negative = span[0] == '-';
            index = 1;
            if (span.Length == 1)
                return false;
        }

        // 进制前缀：0x / 0b / 0o
        if (span.Length - index >= 3 && span[index] == '0')
        {
            int radix = span[index + 1] switch
            {
                'x' or 'X' => 16,
                'b' or 'B' => 2,
                'o' or 'O' => 8,
                _ => 0,
            };
            if (radix != 0)
            {
                ulong accumulator = 0;
                bool anyDigit = false;
                bool previousDigit = false;
                for (int i = index + 2; i < span.Length; i++)
                {
                    char c = span[i];
                    if (c == '_')
                    {
                        if (!previousDigit || i + 1 >= span.Length)
                            return false;
                        previousDigit = false;
                        continue;
                    }

                    int digit = DigitValue(c);
                    if (digit < 0 || digit >= radix)
                        return false;

                    // 超过 64 位的字面量直接判为"不是整数"，最终按字符串保留，不静默截断
                    if (accumulator > (ulong.MaxValue - (ulong)digit) / (ulong)radix)
                        return false;

                    accumulator = accumulator * (ulong)radix + (ulong)digit;
                    anyDigit = true;
                    previousDigit = true;
                }

                if (!anyDigit)
                    return false;
                if (negative)
                {
                    if (accumulator > 0x8000_0000_0000_0000UL)
                        return false;
                    value = unchecked(-(long)accumulator);
                }
                else
                {
                    value = unchecked((long)accumulator);
                }

                return true;
            }
        }

        // 十进制
        long result = 0;
        bool hasDigit = false;
        bool lastWasDigit = false;
        for (int i = index; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '_')
            {
                if (!lastWasDigit || i + 1 >= span.Length)
                    return false;
                lastWasDigit = false;
                continue;
            }

            if (c < '0' || c > '9')
                return false;
            int digit = c - '0';
            hasDigit = true;
            lastWasDigit = true;
            if (negative)
            {
                if (result < (long.MinValue + digit) / 10)
                    return false;
                result = result * 10 - digit;
            }
            else
            {
                if (result > (long.MaxValue - digit) / 10)
                    return false;
                result = result * 10 + digit;
            }
        }

        if (!hasDigit)
            return false;
        value = result;
        return true;
    }

    private static bool TryParseBoolean(ReadOnlySpan<char> span, out bool value)
    {
        span = span.Trim();
        if (span.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("1", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        if (span.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("0", StringComparison.Ordinal))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    internal static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// 浮点输出：保证往返精度，并让整数值的浮点带上 ".0" 以保持类型（"R" 格式不丢精度）。
    /// </summary>
    internal static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
            return "NaN";
        if (double.IsPositiveInfinity(value))
            return "Infinity";
        if (double.IsNegativeInfinity(value))
            return "-Infinity";
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.AsSpan().IndexOfAny('.', 'E', 'e') < 0)
            text += ".0";
        return text;
    }

    // ===== 相等性 =====

    public bool Equals(ConfigValue other)
    {
        if (_kind != other._kind)
            return false;
        return _kind switch
        {
            ConfigValueKind.Null => true,
            ConfigValueKind.Boolean or ConfigValueKind.Integer or ConfigValueKind.Float => _number == other._number,
            ConfigValueKind.String => string.Equals(_text, other._text, StringComparison.Ordinal),
            _ => ReferenceEquals(_reference, other._reference),
        };
    }

    public override bool Equals(object? obj) => obj is ConfigValue other && Equals(other);

    public override int GetHashCode() => _kind switch
    {
        ConfigValueKind.Null => 0,
        ConfigValueKind.String => _text?.GetHashCode() ?? 0,
        _ => HashCode.Combine(_kind, _number),
    };

    public static bool operator ==(ConfigValue left, ConfigValue right) => left.Equals(right);
    public static bool operator !=(ConfigValue left, ConfigValue right) => !left.Equals(right);

    // ===== 调试输出 =====

    /// <summary>调试文本；过深的结构会被截断为 "…"，避免深嵌套导致栈溢出。</summary>
    public override string ToString() => DebugText(32);

    private string DebugText(int depth)
    {
        if (depth <= 0)
            return "…";

        return _kind switch
        {
            ConfigValueKind.Null => "null",
            ConfigValueKind.Boolean => AsBoolean() ? "true" : "false",
            ConfigValueKind.Integer => AsInt64().ToString(CultureInfo.InvariantCulture),
            ConfigValueKind.Float => FormatDouble(AsDouble()),
            ConfigValueKind.String => '"' + _text + '"',
            ConfigValueKind.Array => $"[{string.Join(", ", EnumerateDebug(AsArray(), depth - 1))}]",
            ConfigValueKind.Object => $"{{ {string.Join(", ", EnumerateDebug(AsObject(), depth - 1))} }}",
            _ => string.Empty,
        };
    }

    private static IEnumerable<string> EnumerateDebug(ConfigArray array, int depth)
    {
        for (int i = 0; i < array.Count; i++)
            yield return array[i].DebugText(depth);
    }

    private static IEnumerable<string> EnumerateDebug(ConfigObject obj, int depth)
    {
        foreach (var pair in obj)
            yield return pair.Key + " = " + pair.Value.DebugText(depth);
    }
}
