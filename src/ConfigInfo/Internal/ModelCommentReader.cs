using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ConfigInfo;

/// <summary>
/// 读取模型成员的说明文本：优先 [Description]，其次程序集旁的 XML 文档文件中的 &lt;summary&gt;。
/// XML 文件按程序集缓存；文件缺失或损坏时静默返回 null，不影响配置读写。
/// </summary>
internal static class ModelCommentReader
{
    private static readonly ConcurrentDictionary<Assembly, Lazy<Dictionary<string, string>>> XmlDocs = new();

    /// <summary>读取成员自身的 [Description] 或 XML summary。</summary>
    public static string? GetMemberComment(MemberInfo member)
    {
        var description = member.GetCustomAttribute<DescriptionAttribute>(inherit: true);
        if (!string.IsNullOrWhiteSpace(description?.Description))
            return Normalize(description.Description);

        return TryGetXmlComment(member.Module.Assembly, GetMemberDocId(member));
    }

    /// <summary>读取类型自身的 [Description] 或 XML summary。</summary>
    public static string? GetTypeComment(Type type)
    {
        if (type.IsGenericType)
            type = type.GetGenericTypeDefinition();

        var description = type.GetCustomAttribute<DescriptionAttribute>(inherit: false);
        if (!string.IsNullOrWhiteSpace(description?.Description))
            return Normalize(description.Description);

        return TryGetXmlComment(type.Assembly, "T:" + GetXmlTypeId(type));
    }

    /// <summary>
    /// 成员自身没有说明时的回退：枚举、嵌套模型、数组/List 元素类型有说明时使用类型说明。
    /// 标量类型（int/string 等）不回退，避免写入系统类型文档。
    /// </summary>
    public static string? GetFallbackComment(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsEnum)
            return GetTypeComment(underlying);

        if (underlying.IsArray && underlying.GetArrayRank() == 1)
            return GetElementComment(underlying.GetElementType());

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
            return GetElementComment(underlying.GetGenericArguments()[0]);

        return IsScalar(underlying) ? null : GetTypeComment(underlying);
    }

    private static string? GetElementComment(Type? elementType)
    {
        if (elementType is null)
            return null;

        Type underlying = Nullable.GetUnderlyingType(elementType) ?? elementType;
        if (underlying.IsEnum)
            return GetTypeComment(underlying);
        if (underlying.IsArray || IsList(underlying) || IsScalar(underlying))
            return null;

        return GetTypeComment(underlying);
    }

    private static bool IsScalar(Type type)
        => type == typeof(string) || type == typeof(bool) || type == typeof(decimal)
           || type == typeof(byte) || type == typeof(sbyte)
           || type == typeof(short) || type == typeof(ushort)
           || type == typeof(int) || type == typeof(uint)
           || type == typeof(long) || type == typeof(ulong)
           || type == typeof(float) || type == typeof(double)
           || type.IsEnum;

    private static bool IsList(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

    private static string GetMemberDocId(MemberInfo member)
    {
        string prefix = member is FieldInfo ? "F:" : "P:";
        string typeId = member.DeclaringType is null ? string.Empty : GetXmlTypeId(member.DeclaringType);
        return prefix + typeId + "." + member.Name;
    }

    private static string GetXmlTypeId(Type type)
    {
        if (type.IsGenericType)
            type = type.GetGenericTypeDefinition();

        // XML 文档 ID 使用 '.' 分隔嵌套类型，而 Type.FullName 使用 '+'
        string fullName = type.FullName ?? type.Name;
        return fullName.Replace('+', '.');
    }

    private static string? TryGetXmlComment(Assembly assembly, string id)
    {
        Lazy<Dictionary<string, string>> docs = XmlDocs.GetOrAdd(
            assembly,
            static a => new Lazy<Dictionary<string, string>>(() => LoadXmlDocs(a)));

        return docs.Value.TryGetValue(id, out string? comment) ? comment : null;
    }

    private static Dictionary<string, string> LoadXmlDocs(Assembly assembly)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            string? path = GetXmlDocPath(assembly);
            if (path is null || !File.Exists(path))
                return result;

            var settings = new XmlReaderSettings
            {
                // XML 文档是外部文件：禁止 DTD，避免外部实体解析
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            using XmlReader reader = XmlReader.Create(path, settings);
            XDocument document = XDocument.Load(reader);
            XElement? members = document.Root?.Element("members");
            if (members is null)
                return result;

            foreach (XElement member in members.Elements("member"))
            {
                string? name = member.Attribute("name")?.Value;
                string? summary = Normalize(member.Element("summary")?.Value);
                if (!string.IsNullOrEmpty(name) && summary is not null)
                    result[name] = summary;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or XmlException or ArgumentException or NotSupportedException)
        {
            // XML 文档缺失、无权限或格式错误：静默降级为无注释
        }

        return result;
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "单文件发布时 Assembly.Location 为空，代码已回退到 AppContext.BaseDirectory。")]
    private static string? GetXmlDocPath(Assembly assembly)
    {
        try
        {
            string location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
                return Path.ChangeExtension(location, ".xml");

            // 单文件发布 / 动态加载时 Assembly.Location 为空，尝试应用目录
            string? name = assembly.GetName().Name;
            return string.IsNullOrEmpty(name) ? null : Path.Combine(AppContext.BaseDirectory, name + ".xml");
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>把 summary / Description 的多行缩进折叠成一行。</summary>
    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
