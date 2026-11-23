namespace ConfigInfo;

/// <summary>
/// 配置值的种类。枚举顺序参与判断（&lt;= String 均为标量），不要随意调整。
/// </summary>
public enum ConfigValueKind : byte
{
    /// <summary>空值（null）。</summary>
    Null = 0,

    /// <summary>布尔值 true / false。</summary>
    Boolean,

    /// <summary>64 位有符号整数（支持十进制与 0x/0b/0o 前缀）。</summary>
    Integer,

    /// <summary>64 位双精度浮点数。</summary>
    Float,

    /// <summary>UTF-8 字符串。</summary>
    String,

    /// <summary>有序数组。</summary>
    Array,

    /// <summary>有序键值对象。</summary>
    Object,
}
