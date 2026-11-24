using System.Text;

namespace ConfigInfo;

/// <summary>
/// 写出时使用的标准文件头。解析器会原样识别并跳过这段固定注释，
/// 避免 <c>Load → Save</c> 把库生成的头注释重复叠加。
/// </summary>
internal static class ConfigHeader
{
    /// <summary>标准头的每一行（不含换行）。</summary>
    public static readonly string[] Lines =
    [
        "# ============================================================",
        "# ConfigInfo 配置文件（.cif 格式，UTF-8 文本，可直接编辑）",
        "# 键值：key = value         分组：[section.path] 或 key { ... }",
        "# 数组：[1, 2, 3]           对象：{ a = 1, b = 2 }",
        "# 数字：-1 / 3.14 / 1e9 / 0x1F / 0b1010 / 0o17 / NaN / Infinity",
        "# 注释：# 或 //             字符串：\"...\" / '...' / 裸值",
        "# ============================================================",
    ];

    public static void Append(StringBuilder builder)
    {
        foreach (string line in Lines)
            builder.Append(line).Append('\n');
    }
}
