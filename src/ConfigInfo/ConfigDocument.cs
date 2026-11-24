using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ConfigInfo;

/// <summary>
/// 一个 .cif 配置文档：包含根对象 <see cref="Root"/>，负责解析、生成文本与文件读写。
/// 注释（条目上方注释、行尾注释、根对象头尾注释）会随解析读取，并在保存时写出。
/// 全部实现不依赖反射与动态代码，NativeAOT / 裁剪下可安全使用。
/// </summary>
public sealed class ConfigDocument
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>配置根对象。</summary>
    public ConfigObject Root { get; }

    /// <summary>创建空文档。</summary>
    public ConfigDocument() : this(new ConfigObject()) { }

    /// <summary>用已有根对象创建文档（不复制，直接引用）。</summary>
    public ConfigDocument(ConfigObject root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>解析 .cif 文本，语法错误时抛出 <see cref="ConfigFormatException"/>（含行号/列号）。</summary>
    public static ConfigDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text.AsSpan());
    }

    /// <summary>解析 .cif 文本（Span 版本，避免额外字符串分配）。</summary>
    public static ConfigDocument Parse(ReadOnlySpan<char> text) => new(ConfigParser.Parse(text));

    /// <summary>
    /// 尝试解析 .cif 文本；语法错误时不抛异常，通过 <paramref name="error"/> 返回
    /// <see cref="ConfigFormatException"/>（保留行号/列号）。
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ConfigDocument? document,
        [NotNullWhen(false)] out ConfigFormatException? error)
    {
        if (text is null)
        {
            document = null;
            error = new ConfigFormatException("配置文本不能为 null。", 1, 1);
            return false;
        }

        try
        {
            document = Parse(text);
            error = null;
            return true;
        }
        catch (ConfigFormatException ex)
        {
            document = null;
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// 从文件加载配置。自动识别 UTF-8 / UTF-16（含 BOM）；无 BOM 时按 UTF-8 解码。
    /// </summary>
    /// <exception cref="ConfigFormatException">文本存在语法错误。</exception>
    /// <exception cref="IOException">文件读取失败。</exception>
    /// <exception cref="UnauthorizedAccessException">没有读取权限。</exception>
    /// <exception cref="ArgumentException">路径为空或非法。</exception>
    public static ConfigDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // File.ReadAllText 自带 BOM 探测：UTF-8 / UTF-16 LE / UTF-16 BE
        return Parse(File.ReadAllText(path));
    }

    /// <summary>尝试从文件加载配置；失败时不抛异常，通过 <paramref name="error"/> 返回原因。</summary>
    public static bool TryLoad(string path, [NotNullWhen(true)] out ConfigDocument? document,
        [NotNullWhen(false)] out string? error)
    {
        try
        {
            document = Load(path);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ConfigFormatException or ArgumentException or NotSupportedException)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>生成 .cif 文本。<paramref name="includeHeader"/> 为 true 时在文件头写入格式说明注释；
    /// <see cref="ConfigObject.Comment"/> 写在标准头之后。</summary>
    public string ToText(bool includeHeader = true) => ConfigWriter.Write(Root, includeHeader);

    /// <summary>生成 .cif 文本（含头注释）。</summary>
    public override string ToString() => ToText();

    /// <summary>
    /// 保存到文件（UTF-8 无 BOM）。
    /// <paramref name="atomic"/> 为 true 时先写唯一临时文件再整体替换，写入中断不会损坏原配置文件；
    /// 临时文件在替换失败时会被清理。
    /// </summary>
    /// <exception cref="IOException">写入或替换失败。</exception>
    /// <exception cref="UnauthorizedAccessException">没有写入权限，或目标是只读文件。</exception>
    /// <exception cref="ArgumentException">路径非法。</exception>
    /// <exception cref="InvalidOperationException">配置嵌套层级超过写出上限。</exception>
    public void Save(string path, bool atomic = true, bool includeHeader = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string text = ToText(includeHeader);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (!atomic)
        {
            File.WriteAllText(fullPath, text, Utf8NoBom);
            return;
        }

        // 唯一临时名：并发保存同一个文件时不会互相覆盖临时文件
        string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, text, Utf8NoBom);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>尝试保存到文件；失败时不抛异常，通过 <paramref name="error"/> 返回原因。</summary>
    public bool TrySave(string path, [NotNullWhen(false)] out string? error,
        bool atomic = true, bool includeHeader = true)
    {
        try
        {
            Save(path, atomic, includeHeader);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }
}
