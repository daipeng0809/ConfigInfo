namespace ConfigInfo;

/// <summary>
/// 解析 .cif 文本时的语法错误，包含出错的行号与列号（均从 1 开始）。
/// </summary>
public sealed class ConfigFormatException : Exception
{
    /// <summary>出错行号（从 1 开始）。</summary>
    public int Line { get; }

    /// <summary>出错列号（从 1 开始）。</summary>
    public int Column { get; }

    public ConfigFormatException(string message, int line, int column)
        : base($"{message}（第 {line} 行，第 {column} 列）")
    {
        Line = line;
        Column = column;
    }
}
