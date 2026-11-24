using System.Globalization;
using System.Text;

namespace ConfigInfo;

/// <summary>
/// .cif 文本写出器：保持键的插入顺序，输出稳定且可手工继续编辑的文本。
/// </summary>
internal static class ConfigWriter
{
    private const string IndentUnit = "    ";
    private const int MaxInlineArrayLength = 100;

    /// <summary>写出时的嵌套深度上限，与解析器保持一致，避免超深结构导致栈溢出。</summary>
    private const int MaxNestingDepth = 256;

    public static string Write(ConfigObject root, bool includeHeader)
    {
        var builder = new StringBuilder(root.Count * 32 + 128);
        if (includeHeader)
            AppendHeader(builder);
        if (!string.IsNullOrWhiteSpace(root.Comment))
        {
            AppendCommentBlock(builder, root.Comment, 0);
            builder.Append('\n'); // 空行分隔：解析时保持为文件级注释，而不是第一条的注释
        }

        AppendObjectEntries(builder, root, 0);
        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder) => ConfigHeader.Append(builder);

    private static void AppendObjectEntries(StringBuilder builder, ConfigObject obj, int depth)
    {
        CheckNestingDepth(depth);
        foreach (var pair in obj)
        {
            ConfigValue value = pair.Value;

            string? comment = obj.GetComment(pair.Key);
            if (comment is null && value.Kind == ConfigValueKind.Object)
                comment = value.AsObject().Comment;
            AppendCommentBlock(builder, comment, depth);

            AppendIndent(builder, depth);
            if (value.Kind == ConfigValueKind.Object)
            {
                ConfigObject child = value.AsObject();
                AppendKey(builder, pair.Key);
                if (child.Count == 0 && string.IsNullOrWhiteSpace(child.TrailingComment))
                {
                    builder.Append(" = {}");
                    AppendInlineComment(builder, obj.GetInlineComment(pair.Key));
                    builder.Append('\n');
                    continue;
                }

                builder.Append(" {");
                AppendInlineComment(builder, obj.GetInlineComment(pair.Key));
                builder.Append('\n');
                AppendObjectEntries(builder, child, depth + 1);
                AppendIndent(builder, depth);
                builder.Append("}\n");
                continue;
            }

            AppendKey(builder, pair.Key);
            builder.Append(" = ");
            AppendValue(builder, value, depth);
            AppendInlineComment(builder, obj.GetInlineComment(pair.Key));
            builder.Append('\n');
        }

        AppendCommentBlock(builder, obj.TrailingComment, depth);
    }

    /// <summary>写条目上方的注释块：多行注释拆成多条 '#' 行，空行写成单独的 '#'。</summary>
    private static void AppendCommentBlock(StringBuilder builder, string? comment, int depth)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return;

        string normalized = comment.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string rawLine in normalized.Split('\n'))
        {
            AppendIndent(builder, depth);
            string line = rawLine.TrimEnd();
            builder.Append('#');
            if (line.Length > 0)
                builder.Append(' ').Append(line);
            builder.Append('\n');
        }
    }

    /// <summary>写行尾注释；多行内容合并为一行。</summary>
    private static void AppendInlineComment(StringBuilder builder, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return;

        string singleLine = comment
            .Replace("\r\n", " ")
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();
        if (singleLine.Length > 0)
            builder.Append("  # ").Append(singleLine);
    }

    private static void AppendValue(StringBuilder builder, ConfigValue value, int depth)
    {
        switch (value.Kind)
        {
            case ConfigValueKind.Array:
                AppendArray(builder, value.AsArray(), depth);
                break;
            case ConfigValueKind.Object:
                AppendInlineObject(builder, value.AsObject(), depth + 1);
                break;
            default:
                AppendScalar(builder, value);
                break;
        }
    }

    private static void CheckNestingDepth(int depth)
    {
        if (depth > MaxNestingDepth)
            throw new InvalidOperationException($"配置嵌套层级超过上限 {MaxNestingDepth} 层，无法写出。");
    }

    /// <summary>用于数组与内联对象内部的写法：全部单行，不再展开多行。</summary>
    private static void AppendInlineValue(StringBuilder builder, ConfigValue value, int depth)
    {
        CheckNestingDepth(depth);
        switch (value.Kind)
        {
            case ConfigValueKind.Array:
                ConfigArray array = value.AsArray();
                builder.Append('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    AppendInlineValue(builder, array[i], depth + 1);
                }

                builder.Append(']');
                break;
            case ConfigValueKind.Object:
                AppendInlineObject(builder, value.AsObject(), depth + 1);
                break;
            default:
                AppendScalar(builder, value);
                break;
        }
    }

    private static void AppendScalar(StringBuilder builder, ConfigValue value)
    {
        switch (value.Kind)
        {
            case ConfigValueKind.Null:
                builder.Append("null");
                break;
            case ConfigValueKind.Boolean:
                builder.Append(value.AsBoolean() ? "true" : "false");
                break;
            case ConfigValueKind.Integer:
                builder.Append(value.AsInt64().ToString(CultureInfo.InvariantCulture));
                break;
            case ConfigValueKind.Float:
                builder.Append(ConfigValue.FormatDouble(value.AsDouble()));
                break;
            case ConfigValueKind.String:
                AppendQuotedString(builder, value.AsString());
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void AppendArray(StringBuilder builder, ConfigArray array, int depth)
    {
        CheckNestingDepth(depth);
        if (array.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        int mark = builder.Length;
        builder.Append('[');
        for (int i = 0; i < array.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            AppendInlineValue(builder, array[i], depth + 1);
        }

        builder.Append(']');
        if (builder.Length - mark <= MaxInlineArrayLength)
            return;

        // 单行过长 → 回滚并改为多行
        builder.Length = mark;
        builder.Append("[\n");
        for (int i = 0; i < array.Count; i++)
        {
            AppendIndent(builder, depth + 1);
            AppendInlineValue(builder, array[i], depth + 1);
            builder.Append(",\n");
        }

        AppendIndent(builder, depth);
        builder.Append(']');
    }

    private static void AppendInlineObject(StringBuilder builder, ConfigObject obj, int depth)
    {
        CheckNestingDepth(depth);
        if (obj.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.Append("{ ");
        bool first = true;
        foreach (var pair in obj)
        {
            if (!first)
                builder.Append(", ");
            first = false;
            AppendKey(builder, pair.Key);
            builder.Append(" = ");
            AppendInlineValue(builder, pair.Value, depth + 1);
        }

        builder.Append(" }");
    }

    private static void AppendKey(StringBuilder builder, string key)
    {
        if (NeedsQuote(key))
        {
            AppendQuotedString(builder, key);
            return;
        }

        builder.Append(key);
    }

    private static bool NeedsQuote(string key)
    {
        if (key.Length == 0)
            return true;

        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                return true;
            switch (c)
            {
                case '=':
                case '{':
                case '}':
                case '[':
                case ']':
                case ',':
                case '#':
                case '"':
                case '\'':
                case '\\':
                // '.' 必须加引号：裸键中的 '.' 会在解析时展开成嵌套对象，破坏原有结构
                case '.':
                // ':' 是解析器的键终止符（用于识别 "key: value" 误写），因此必须加引号
                case ':':
                    return true;
                case '/' when i + 1 < key.Length && key[i + 1] == '/':
                    return true;
            }
        }

        return false;
    }

    private static void AppendQuotedString(StringBuilder builder, string text)
    {
        builder.Append('"');
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\0': builder.Append("\\0"); break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        // 合法代理对：原样输出，保证 emoji 等增补平面字符可往返
                        builder.Append(c).Append(text[i + 1]);
                        i++;
                    }
                    else if (char.IsControl(c) || char.IsSurrogate(c))
                    {
                        // 控制字符与孤立代理项转成 \uXXXX，避免写出无法解析的文本
                        builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static void AppendIndent(StringBuilder builder, int depth)
    {
        for (int i = 0; i < depth; i++)
            builder.Append(IndentUnit);
    }
}
