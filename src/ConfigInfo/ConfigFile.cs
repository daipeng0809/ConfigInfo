using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace ConfigInfo;

/// <summary>
/// 自动对象映射入口：把普通类交给本类即可保存为 .cif；读取文件时直接生成类实例。
/// <para>
/// 常用写法：
/// <code>
/// ConfigFile.Save("app.cif", settings);              // 类 → 文件
/// AppSettings loaded = ConfigFile.Load&lt;AppSettings&gt;("app.cif");   // 文件 → 类
/// AppSettings safe = ConfigFile.LoadOrDefault("app.cif", out string? error);
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// 映射规则（零配置即可使用）：
/// <list type="bullet">
/// <item>公共实例字段与公共可读写属性参与映射；默认键名为成员名首字母小写（<c>ComPort</c> → <c>comPort</c>）。</item>
/// <item>嵌套类成员写成子对象；一维数组与 <see cref="List{T}"/> 写成数组；元素可以是标量或嵌套对象。</item>
/// <item>支持 string / bool / 整数 / float / double / decimal / 枚举 / 可空值类型 / 数组 / List&lt;T&gt; / 嵌套类。</item>
/// <item>用 <see cref="ConfigNameAttribute"/> 改名或指定点路径（<c>"serial.port"</c>）把扁平类写成分组；
/// 用 <see cref="ConfigIgnoreAttribute"/> 排除成员。</item>
/// <item>读取时缺失的键保留 <c>new T()</c> 的默认值；键名优先精确匹配，其次忽略大小写匹配；未知键忽略。</item>
/// <item>根对象写出使用运行时类型；嵌套成员与集合元素按声明类型写出（需要派生字段时请把派生类型作为根对象或扁平化）。</item>
/// <item>数字转换沿用库的宽松规则：字符串 "115200"、十六进制 <c>0x1F90</c> 都能读成整数；收窄时按目标范围截断。</item>
/// <item>自动注释：默认把成员的 [Description] 或 XML 文档 summary 写成条目注释（类自身的说明写入文件头）；
/// 可用 <c>includeModelComments: false</c> 关闭。XML summary 需要模型程序集生成并部署同名的 .xml 文档文件。</item>
/// </list>
/// <para>
/// 本类基于反射，已标注 <see cref="RequiresUnreferencedCodeAttribute"/>：裁剪 / NativeAOT 项目请改用
/// <c>SaveTo</c> / <c>LoadFrom</c> 手写映射（见 README），调用本类会在 AOT 分析中产生 IL2026 提示。
/// 核心解析与写出（<see cref="ConfigDocument"/> 等）仍然零反射。
/// </para>
/// </remarks>
[RequiresUnreferencedCode(AotMessage)]
[RequiresDynamicCode(AotMessage)]
public static class ConfigFile
{
    private const string AotMessage =
        "自动映射通过反射访问模型成员，裁剪或 NativeAOT 下需要保留模型类型元数据；AOT 项目请使用 SaveTo / LoadFrom 手写映射。";

    private const DynamicallyAccessedMemberTypes ModelMembers =
        DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    private const int MaxDepth = 256;

    private static readonly ConcurrentDictionary<Type, MemberModel[]> ModelCache = new();
    private static readonly ConcurrentDictionary<Type, byte> ValidatedTypes = new();

    // ===== 写：类 → 配置对象 / 文本 / 文件 =====

    /// <summary>把对象转换为配置根对象（可与 <see cref="ConfigDocument"/> 等底层 API 混用）。</summary>
    /// <param name="value">对象实例。</param>
    /// <param name="includeModelComments">是否把成员的 [Description] / XML 文档 summary 写成条目注释。</param>
    /// <exception cref="NotSupportedException">模型包含不支持映射的成员类型。</exception>
    /// <exception cref="InvalidOperationException">模型存在循环引用或嵌套超过 256 层。</exception>
    public static ConfigObject ToObject<[DynamicallyAccessedMembers(ModelMembers)] T>(T value,
        bool includeModelComments = true)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        // 使用运行时类型：以基类/接口变量调用时不会静默丢失派生成员
        Type type = value.GetType();
        EnsureValidated(type);

        var root = new ConfigObject();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        WriteMembers(type, value, root, 0, visited, includeModelComments);

        if (includeModelComments && string.IsNullOrWhiteSpace(root.Comment))
            root.Comment = ModelCommentReader.GetTypeComment(type);

        return root;
    }

    /// <summary>把对象转换为 .cif 文本。</summary>
    public static string ToText<[DynamicallyAccessedMembers(ModelMembers)] T>(T value,
        bool includeHeader = true, bool includeModelComments = true)
        => new ConfigDocument(ToObject(value, includeModelComments)).ToText(includeHeader);

    /// <summary>把对象保存为 .cif 文件（默认原子替换、UTF-8 无 BOM、含头注释）。</summary>
    public static void Save<[DynamicallyAccessedMembers(ModelMembers)] T>(string path, T value,
        bool atomic = true, bool includeHeader = true, bool includeModelComments = true)
        => new ConfigDocument(ToObject(value, includeModelComments)).Save(path, atomic, includeHeader);

    /// <summary>尝试把对象保存为文件；失败时不抛异常，通过 <paramref name="error"/> 返回原因。</summary>
    public static bool TrySave<[DynamicallyAccessedMembers(ModelMembers)] T>(string path, T value,
        [NotNullWhen(false)] out string? error, bool atomic = true, bool includeHeader = true,
        bool includeModelComments = true)
    {
        try
        {
            Save(path, value, atomic, includeHeader, includeModelComments);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 映射会调用用户 getter/setter，异常类型不可预知；Try 系列统一不抛异常
            error = ex.Message;
            return false;
        }
    }

    // ===== 读：文件 / 文本 / 配置对象 → 类 =====

    /// <summary>读取 .cif 文件并直接生成类实例。</summary>
    /// <exception cref="ConfigFormatException">文本存在语法错误。</exception>
    /// <exception cref="IOException">文件读取失败。</exception>
    /// <exception cref="NotSupportedException">模型包含不支持映射的成员类型。</exception>
    public static T Load<[DynamicallyAccessedMembers(ModelMembers)] T>(string path) where T : new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromObject<T>(ConfigDocument.Load(path).Root);
    }

    /// <summary>尝试读取文件并生成类实例；失败时不抛异常。</summary>
    public static bool TryLoad<[DynamicallyAccessedMembers(ModelMembers)] T>(string path,
        [NotNullWhen(true)] out T? value, [NotNullWhen(false)] out string? error) where T : new()
    {
        try
        {
            value = Load<T>(path)!;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 映射会调用用户 getter/setter，异常类型不可预知；Try 系列统一不抛异常
            value = default;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 读取文件并生成类实例；文件缺失或内容有误时不抛异常，返回带默认值的 <c>new T()</c>，
    /// 失败原因通过 <paramref name="error"/> 返回（成功时为 <c>null</c>）。
    /// </summary>
    public static T LoadOrDefault<[DynamicallyAccessedMembers(ModelMembers)] T>(string path,
        out string? error) where T : new()
        => TryLoad<T>(path, out T? value, out error) ? value : new T();

    /// <summary>解析 .cif 文本并直接生成类实例。</summary>
    public static T Parse<[DynamicallyAccessedMembers(ModelMembers)] T>(string text) where T : new()
    {
        ArgumentNullException.ThrowIfNull(text);
        return FromObject<T>(ConfigDocument.Parse(text).Root);
    }

    /// <summary>把配置根对象填充为类实例（缺失的键保留 <c>new T()</c> 的默认值）。</summary>
    public static T FromObject<[DynamicallyAccessedMembers(ModelMembers)] T>(ConfigObject root) where T : new()
    {
        ArgumentNullException.ThrowIfNull(root);
        // 可空结构体根类型：按底层结构体映射，再解箱回 T
        Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        EnsureValidated(type);
        return (T)ReadModel(type, root, 0);
    }

    /// <summary>把配置根对象读取到已有实例（适合重新加载设置）。</summary>
    public static void LoadInto<[DynamicallyAccessedMembers(ModelMembers)] T>(ConfigObject root, T target)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(target);
        // 使用运行时类型：已有派生实例时也能读取派生成员
        Type type = target.GetType();
        EnsureValidated(type);
        ReadMembersInto(type, target, root, 0);
    }

    // ===== 写出实现 =====

    private static void WriteMembers([DynamicallyAccessedMembers(ModelMembers)] Type type, object instance,
        ConfigObject owner, int depth, HashSet<object> visited, bool includeModelComments)
    {
        CheckDepth(depth);
        if (!type.IsValueType && !visited.Add(instance))
            throw new InvalidOperationException($"检测到循环引用：{type.FullName} 的实例被自身包含。");

        try
        {
            foreach (MemberModel member in GetMembers(type))
            {
                object? value = GetMemberValue(instance, member);
                WriteMember(owner, member.Key, member.Type, value, depth + 1, visited, includeModelComments);

                if (includeModelComments)
                    ApplyMemberComment(owner, member);
            }
        }
        finally
        {
            if (!type.IsValueType)
                visited.Remove(instance);
        }
    }

    private static void WriteMember(ConfigObject owner, string key, Type type, object? value,
        int depth, HashSet<object> visited, bool includeModelComments)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (value is null)
        {
            SetConfigValue(owner, key, ConfigValue.Null);
            return;
        }

        if (IsSupportedScalar(underlying))
        {
            SetConfigValue(owner, key, ScalarToValue(underlying, value));
            return;
        }

        if (TryGetCollectionElement(underlying, out Type? elementType))
        {
            var array = new ConfigArray();
            foreach (object? item in (IEnumerable)value)
                array.Add(WriteElement(elementType!, item, depth + 1, visited, includeModelComments));
            SetConfigValue(owner, key, new ConfigValue(array));
            return;
        }

        WriteMembers(underlying, value, owner.GetObject(key), depth + 1, visited, includeModelComments);
    }

    private static ConfigValue WriteElement(Type type, object? value, int depth, HashSet<object> visited,
        bool includeModelComments)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (value is null)
            return ConfigValue.Null;

        if (IsSupportedScalar(underlying))
            return ScalarToValue(underlying, value);

        if (TryGetCollectionElement(underlying, out Type? elementType))
        {
            var nested = new ConfigArray();
            foreach (object? item in (IEnumerable)value)
                nested.Add(WriteElement(elementType!, item, depth + 1, visited, includeModelComments));
            return new ConfigValue(nested);
        }

        var child = new ConfigObject();
        WriteMembers(underlying, value, child, depth + 1, visited, includeModelComments);
        return new ConfigValue(child);
    }

    private static ConfigValue ScalarToValue(Type type, object value) => type switch
    {
        _ when type == typeof(string) => new ConfigValue((string)value),
        _ when type == typeof(bool) => new ConfigValue((bool)value),
        _ when type == typeof(byte) => new ConfigValue((byte)value),
        _ when type == typeof(sbyte) => new ConfigValue((sbyte)value),
        _ when type == typeof(short) => new ConfigValue((short)value),
        _ when type == typeof(ushort) => new ConfigValue((ushort)value),
        _ when type == typeof(int) => new ConfigValue((int)value),
        _ when type == typeof(uint) => new ConfigValue((uint)value),
        _ when type == typeof(long) => new ConfigValue((long)value),
        _ when type == typeof(ulong) => new ConfigValue((ulong)value),
        _ when type == typeof(float) => new ConfigValue((float)value),
        _ when type == typeof(double) => new ConfigValue((double)value),
        _ when type == typeof(decimal) => new ConfigValue((decimal)value),
        _ when type.IsEnum => new ConfigValue(value.ToString()),
        _ => throw new NotSupportedException($"类型 {type.FullName} 不支持作为配置标量。"),
    };

    // ===== 读取实现 =====

    private static object ReadModel([DynamicallyAccessedMembers(ModelMembers)] Type type, ConfigObject source, int depth)
    {
        object instance = CreateInstance(type);
        ReadMembersInto(type, instance, source, depth);
        return instance;
    }

    private static void ReadMembersInto([DynamicallyAccessedMembers(ModelMembers)] Type type, object instance,
        ConfigObject source, int depth)
    {
        CheckDepth(depth);

        foreach (MemberModel member in GetMembers(type))
        {
            if (!TryGetMemberValue(source, member.Key, out ConfigValue configured))
                continue; // 缺失：保留 new T() 的默认值

            Type memberType = member.Type;
            Type underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;

            if (configured.IsNull)
            {
                if (!memberType.IsValueType || Nullable.GetUnderlyingType(memberType) is not null)
                    SetMemberValue(instance, member, null);
                continue;
            }

            if (IsSupportedScalar(underlying))
            {
                if (TryScalarFromValue(underlying, configured, out object? scalar))
                    SetMemberValue(instance, member, scalar);
                continue;
            }

            if (TryGetCollectionElement(underlying, out Type? elementType))
            {
                if (!configured.IsArray)
                    continue;
                object collection = ReadCollection(underlying, elementType!, configured.AsArray(), depth + 1);
                SetMemberValue(instance, member, collection);
                continue;
            }

            if (!configured.IsObject)
                continue;

            object? current = GetMemberValue(instance, member);
            object child = current ?? CreateInstance(underlying);
            ReadMembersInto(underlying, child, configured.AsObject(), depth + 1);
            SetMemberValue(instance, member, child);
        }
    }

    private static object ReadCollection(Type type, Type elementType, ConfigArray source, int depth)
    {
        if (type.IsArray)
        {
            Array array = Array.CreateInstance(elementType, source.Count);
            for (int i = 0; i < source.Count; i++)
                array.SetValue(ReadElement(elementType, source[i], depth + 1), i);
            return array;
        }

        object list = CreateInstance(type);
        var items = (IList)list;
        for (int i = 0; i < source.Count; i++)
            items.Add(ReadElement(elementType, source[i], depth + 1));
        return list;
    }

    private static object? ReadElement(Type type, ConfigValue value, int depth)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        // null 元素：引用类型与可空值类型保留 null，非可空值类型取 default
        if (value.IsNull)
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? DefaultOf(type) : null;

        if (IsSupportedScalar(underlying))
            return TryScalarFromValue(underlying, value, out object? scalar) ? scalar : DefaultOf(underlying);

        if (TryGetCollectionElement(underlying, out Type? elementType))
            return value.IsArray
                ? ReadCollection(underlying, elementType!, value.AsArray(), depth + 1)
                : DefaultOf(underlying);

        if (!value.IsObject)
            return DefaultOf(underlying);

        object instance = CreateInstance(underlying);
        ReadMembersInto(underlying, instance, value.AsObject(), depth + 1);
        return instance;
    }

    private static bool TryScalarFromValue(Type type, ConfigValue value, out object? result)
    {
        if (type == typeof(string))
        {
            if (value.TryConvertToString(out string? text))
            {
                result = text;
                return true;
            }

            result = null;
            return false;
        }

        if (type == typeof(bool))
        {
            if (value.TryConvertToBoolean(out bool boolean))
            {
                result = boolean;
                return true;
            }

            result = null;
            return false;
        }

        if (type.IsEnum)
        {
            if (value.TryConvertToString(out string? text) && Enum.TryParse(type, text, ignoreCase: true, out object? parsed))
            {
                result = parsed;
                return true;
            }

            if (value.TryConvertToInt64(out long number))
            {
                result = Enum.ToObject(type, number);
                return true;
            }

            result = null;
            return false;
        }

        if (type == typeof(float))
        {
            if (value.TryConvertToDouble(out double single))
            {
                result = (float)single;
                return true;
            }

            result = null;
            return false;
        }

        if (type == typeof(double))
        {
            if (value.TryConvertToDouble(out double number))
            {
                result = number;
                return true;
            }

            result = null;
            return false;
        }

        if (type == typeof(decimal))
        {
            if (value.TryConvertToDecimal(out decimal number))
            {
                result = number;
                return true;
            }

            result = null;
            return false;
        }

        if (type == typeof(ulong))
        {
            if (value.TryConvertToInt64(out long number))
            {
                result = unchecked((ulong)number);
                return true;
            }

            result = null;
            return false;
        }

        if (IsIntegerType(type))
        {
            if (value.TryConvertToInt64(out long number))
            {
                result = ClampToInteger(type, number);
                return true;
            }

            result = null;
            return false;
        }

        result = null;
        return false;
    }

    private static object ClampToInteger(Type type, long value)
    {
        if (type == typeof(byte)) return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
        if (type == typeof(sbyte)) return (sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
        if (type == typeof(short)) return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        if (type == typeof(ushort)) return (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue);
        if (type == typeof(int)) return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
        if (type == typeof(uint)) return (uint)Math.Clamp(value, uint.MinValue, uint.MaxValue);
        return value;
    }

    private static object? DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    // ===== 成员查找 =====

    private static bool TryGetMemberValue(ConfigObject owner, string key, out ConfigValue value)
        => key.AsSpan().IndexOf('.') >= 0
            ? TryGetPath(owner, key, out value)
            : TryGetValue(owner, key, out value);

    /// <summary>点路径读取：每级先精确匹配、再忽略大小写匹配。</summary>
    private static bool TryGetPath(ConfigObject owner, string path, out ConfigValue value)
    {
        value = default;
        ConfigObject node = owner;
        ReadOnlySpan<char> remaining = path.AsSpan();
        while (true)
        {
            int dot = remaining.IndexOf('.');
            ReadOnlySpan<char> segment = dot < 0 ? remaining : remaining[..dot];
            if (segment.IsEmpty)
                return false;

            if (!TryGetValue(node, segment.ToString(), out value))
                return false;
            if (dot < 0)
                return true;
            if (!value.IsObject)
            {
                value = default;
                return false;
            }

            node = value.AsObject();
            remaining = remaining[(dot + 1)..];
        }
    }

    /// <summary>单级键读取：先精确匹配，再忽略大小写（容忍手工改错大小写）。</summary>
    private static bool TryGetValue(ConfigObject owner, string key, out ConfigValue value)
    {
        if (owner.TryGetValue(key, out value))
            return true;

        foreach (KeyValuePair<string, ConfigValue> pair in owner)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void SetConfigValue(ConfigObject owner, string key, ConfigValue value)
    {
        if (key.AsSpan().IndexOf('.') >= 0)
            owner.SetPath(key, value);
        else
            owner.Set(key, value);
    }

    /// <summary>为刚写出的成员补充自动注释：成员说明优先，其次嵌套类型/枚举/集合元素类型说明。</summary>
    private static void ApplyMemberComment(ConfigObject owner, MemberModel member)
    {
        string? comment = ModelCommentReader.GetMemberComment(member.Member)
                          ?? ModelCommentReader.GetFallbackComment(member.Type);
        if (string.IsNullOrWhiteSpace(comment))
            return;

        SetMemberComment(owner, member.Key, comment);
    }

    private static void SetMemberComment(ConfigObject owner, string key, string comment)
    {
        if (key.AsSpan().IndexOf('.') < 0)
        {
            if (owner.GetComment(key) is null)
                owner.SetComment(key, comment);
            return;
        }

        int dot = key.LastIndexOf('.');
        if (dot <= 0 || dot == key.Length - 1)
            return; // 非法路径已在映射校验阶段拒绝，此处仅防御

        ConfigObject parent = owner.GetObject(key[..dot]);
        string leaf = key[(dot + 1)..];
        if (parent.GetComment(leaf) is null)
            parent.SetComment(leaf, comment);
    }

    private static object? GetMemberValue(object instance, MemberModel member)
    {
        try
        {
            return member.Field is not null ? member.Field.GetValue(instance) : member.Property!.GetValue(instance);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // 解包用户 getter 的原始异常，避免 TryLoad/TrySave 只看到包装异常
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void SetMemberValue(object instance, MemberModel member, object? value)
    {
        try
        {
            if (member.Field is not null)
                member.Field.SetValue(instance, value);
            else
                member.Property!.SetValue(instance, value);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // ===== 模型元数据 =====

    private static MemberModel[] GetMembers([DynamicallyAccessedMembers(ModelMembers)] Type type)
        => ModelCache.GetOrAdd(type, BuildMembers);

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "自动映射 API 已标注 RequiresUnreferencedCode；嵌套类型成员保留由调用方负责。")]
    private static MemberModel[] BuildMembers([DynamicallyAccessedMembers(ModelMembers)] Type type)
    {
        if (type == typeof(object) || type.IsInterface || type.IsAbstract || type.IsPointer || type.IsByRef)
            throw new NotSupportedException($"类型 {type.FullName} 不能作为配置模型，请使用普通类或结构体。");

        var members = new List<MemberModel>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly || field.IsLiteral)
                continue;
            if (field.IsDefined(typeof(ConfigIgnoreAttribute), inherit: true))
                continue;
            string key = GetKey(field);
            AddMemberKey(keys, type, key);
            members.Add(new MemberModel(key, field.FieldType, field, null));
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;
            if (property.GetMethod is not { IsPublic: true } || property.SetMethod is not { IsPublic: true })
                continue;
            if (property.IsDefined(typeof(ConfigIgnoreAttribute), inherit: true))
                continue;
            string key = GetKey(property);
            AddMemberKey(keys, type, key);
            members.Add(new MemberModel(key, property.PropertyType, null, property));
        }

        if (members.Count == 0)
        {
            throw new NotSupportedException(
                $"类型 {type.FullName} 没有任何可映射的公共字段或可读写属性；不需要映射的成员请标注 [ConfigIgnore]。");
        }

        ValidateKeyPaths(keys, type);
        return members.ToArray();
    }

    private static string GetKey(MemberInfo member)
    {
        var attribute = member.GetCustomAttribute<ConfigNameAttribute>(inherit: true);
        return attribute is not null ? attribute.Name : ToCamelCase(member.Name);
    }

    private static void AddMemberKey(HashSet<string> keys, Type type, string key)
    {
        if (key.Length == 0 || key[0] == '.' || key[^1] == '.'
            || key.Contains("..", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"类型 {type.FullName} 的键 \"{key}\" 不是合法的键或点路径（存在空段，或以 '.' 开头/结尾）。");
        }

        if (!keys.Add(key))
        {
            throw new NotSupportedException(
                $"类型 {type.FullName} 中有多个成员映射到键 \"{key}\"（键名忽略大小写比较），请用 [ConfigName] 改名或 [ConfigIgnore] 排除。");
        }
    }

    /// <summary>
    /// 检查点路径键之间是否互为前缀：<c>"serial"</c> 与 <c>"serial.port"</c> 会写同一命名空间，
    /// 同时存在时后写者会覆盖先写者，必须在映射阶段提前拒绝。
    /// </summary>
    private static void ValidateKeyPaths(HashSet<string> keys, Type type)
    {
        foreach (string key in keys)
        {
            int dot = key.IndexOf('.');
            while (dot >= 0)
            {
                string prefix = key[..dot];
                if (keys.Contains(prefix))
                {
                    throw new NotSupportedException(
                        $"类型 {type.FullName} 的键 \"{key}\" 与键 \"{prefix}\" 路径重叠，写出时会互相覆盖；" +
                        "请用 [ConfigName] 改名、把成员放进嵌套类，或用 [ConfigIgnore] 排除。");
                }

                dot = key.IndexOf('.', dot + 1);
            }
        }
    }

    private static string ToCamelCase(string name)
    {
        if (name.Length == 0 || char.IsLower(name[0]))
            return name;
        // 连续大写开头（IPAddress / IO）保持原样，避免变成 iPA…
        if (name.Length > 1 && char.IsUpper(name[1]))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    // ===== 类型判断与校验 =====

    private static bool IsSupportedScalar(Type type)
        => type == typeof(string) || type == typeof(bool) || IsIntegerType(type)
           || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
           || type.IsEnum;

    private static bool IsIntegerType(Type type)
        => type == typeof(byte) || type == typeof(sbyte)
           || type == typeof(short) || type == typeof(ushort)
           || type == typeof(int) || type == typeof(uint)
           || type == typeof(long) || type == typeof(ulong);

    private static bool TryGetCollectionElement(Type type, out Type? elementType)
    {
        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
                throw new NotSupportedException("自动映射仅支持一维数组；多维数组请改用嵌套 List 或 [ConfigIgnore]。");
            elementType = type.GetElementType();
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            throw new NotSupportedException("自动映射不支持 Dictionary；请改用 List<T> 或标注 [ConfigIgnore] 后手写映射。");

        elementType = null;
        return false;
    }

    private static void EnsureValidated([DynamicallyAccessedMembers(ModelMembers)] Type type)
    {
        if (ValidatedTypes.ContainsKey(type))
            return;

        ValidateRootType(type);
        ValidateType(type, new HashSet<Type>());
        ValidatedTypes.TryAdd(type, 0);
    }

    private static void ValidateRootType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (IsSupportedScalar(underlying) || underlying.IsArray || underlying == typeof(object)
            || (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>)))
        {
            throw new NotSupportedException(
                $"类型 {type.Name} 不是可映射的配置模型，请使用包含公共字段或可读写属性的类/结构体作为根类型。");
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "自动映射 API 已标注 RequiresUnreferencedCode；嵌套类型成员保留由调用方负责。")]
    private static void ValidateType(Type type, HashSet<Type> visited)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (IsSupportedScalar(underlying))
            return;

        if (TryGetCollectionElement(underlying, out Type? elementType))
        {
            ValidateType(elementType!, visited);
            return;
        }

        if (!visited.Add(underlying))
            return; // 自引用类型：成员已在栈上校验

        try
        {
            foreach (MemberModel member in GetMembers(underlying))
                ValidateType(member.Type, visited);
        }
        finally
        {
            visited.Remove(underlying);
        }
    }

    private static object CreateInstance(Type type)
    {
        if (type.IsValueType)
            return Activator.CreateInstance(type)!;

        if (type.IsAbstract || type.IsInterface)
            throw new NotSupportedException($"类型 {type.FullName} 是抽象类型或接口，不能创建配置模型实例。");

        try
        {
            return Activator.CreateInstance(type)
                   ?? throw new NotSupportedException($"无法创建类型 {type.FullName} 的实例。");
        }
        catch (MissingMethodException)
        {
            throw new NotSupportedException($"类型 {type.FullName} 需要公共无参构造函数才能作为配置模型。");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // 解包用户构造函数的原始异常
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void CheckDepth(int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException($"配置模型嵌套超过 {MaxDepth} 层，请检查是否存在自引用结构。");
    }

    private sealed class MemberModel
    {
        public MemberModel(string key, Type type, FieldInfo? field, PropertyInfo? property)
        {
            Key = key;
            Type = type;
            Field = field;
            Property = property;
        }

        public string Key { get; }
        public Type Type { get; }
        public FieldInfo? Field { get; }
        public PropertyInfo? Property { get; }
        public MemberInfo Member => Field is not null ? Field : Property!;
    }
}
