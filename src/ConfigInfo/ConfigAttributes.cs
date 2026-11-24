namespace ConfigInfo;

/// <summary>
/// 指定自动映射时成员使用的键名。名称中包含 <c>.</c> 时按点路径写出/读取（如 <c>"serial.port"</c>），
/// 可让扁平类直接生成分组配置。未标注时使用成员名（首字母小写，如 <c>ComPort</c> → <c>comPort</c>）。
/// <para>
/// 点路径必须合法（不能以 <c>.</c> 开头/结尾、不能出现 <c>..</c>），且同一类型内不能与其他成员的键互为前缀
/// （如同时存在 <c>serial</c> 与 <c>serial.port</c> 会报错，避免写出时互相覆盖）。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ConfigNameAttribute : Attribute
{
    /// <summary>创建键名标注。</summary>
    /// <param name="name">键名或点路径（如 <c>"serial.port"</c>）。</param>
    public ConfigNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>键名或点路径。</summary>
    public string Name { get; }
}

/// <summary>自动映射时忽略该成员：既不写出，也不读取。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ConfigIgnoreAttribute : Attribute
{
}
