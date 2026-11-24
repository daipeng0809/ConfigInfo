using System.Globalization;
using System.Text;

namespace ConfigInfo;

/// <summary>
/// .cif 文本解析器：单遍扫描（ReadOnlySpan&lt;char&gt;），不产生中间 Token 列表，
/// 不使用任何反射，NativeAOT 完全可用。
/// </summary>
internal ref struct ConfigParser
{
    /// <summary>嵌套容器深度上限：超过后抛出可捕获的格式错误，而不是 StackOverflow 崩溃。</summary>
    private const int MaxNestingDepth = 256;

    private readonly ReadOnlySpan<char> _text;
    private int _pos;

    private ConfigParser(ReadOnlySpan<char> text)
    {
        _text = text;
        // 容忍以 BOM 开头的字符串（File.ReadAllText 会剥掉 BOM，但 Parse(string) 不一定）
        _pos = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
    }

    public static ConfigObject Parse(ReadOnlySpan<char> text) => new ConfigParser(text).ParseRoot();

    private bool AtEnd => _pos >= _text.Length;

    private char Peek => _text[_pos];

    private char PeekAt(int offset) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private static bool IsLineBreak(char c) => c == '\n' || c == '\r';

    // ===== 顶层 =====

    private ConfigObject ParseRoot()
    {
        var root = new ConfigObject();
        ConfigObject current = root;
        int currentDepth = 0;
        bool atStart = true;

        SkipGeneratedHeader();

        while (true)
        {
            ReadLeadingComments(out string? detached, out string? attached);
            string? leading = JoinComments(detached, attached);
            if (AtEnd)
            {
                if (atStart)
                {
                    // 没有条目时：空行分隔的前一块仍属于文件头，最后一块属于文件尾
                    root.Comment = detached;
                    root.TrailingComment = attached;
                }
                else
                {
                    root.TrailingComment = leading;
                }
                break;
            }

            char c = Peek;
            if (c == '[')
            {
                (current, currentDepth) = ParseSectionHeader(root, leading);
                atStart = false;
                continue;
            }

            if (c == '{' || c == '}' || c == ']' || c == '=' || c == ',')
                throw Error($"行首出现了意外的字符 '{c}'。");

            int keyStart = _pos;
            string key = ParseKey(out bool quoted);
            SkipInlineSpace();
            if (AtEnd || IsLineBreak(Peek))
                throw Error($"键 \"{key}\" 后缺少 '=' 或 '{{'。");

            ConfigValue value;
            string? inlineComment = null;
            if (Peek == '{')
            {
                _pos++;
                inlineComment = ReadInlineComment();
                value = new ConfigValue(ParseObjectBody(currentDepth + 1));
            }
            else if (Peek == '=')
            {
                _pos++;
                value = ParseValue(currentDepth, out inlineComment);
            }
            else if (Peek == ':')
            {
                throw Error($"键 \"{key}\" 后应使用 '=' 而不是 ':'。");
            }
            else
            {
                throw Error($"键 \"{key}\" 后应为 '=' 或 '{{'，实际为 '{Peek}'。");
            }

            (ConfigObject target, string actualKey) =
                Assign(current, currentDepth, key, value, quoted, keyStart);

            if (atStart)
                root.Comment = detached;
            else if (leading is not null)
                target.SetComment(actualKey, leading);

            if (atStart && attached is not null)
                target.SetComment(actualKey, attached);

            if (inlineComment is not null)
                target.SetInlineComment(actualKey, inlineComment);

            atStart = false;
            string? trailing = FinishLine();
            if (trailing is not null)
            {
                string? existing = target.GetInlineComment(actualKey);
                target.SetInlineComment(actualKey, existing is null ? trailing : existing + " " + trailing);
            }
        }

        return root;
    }

    /// <summary>跳过库生成的标准头注释；不匹配时不移动位置。</summary>
    private void SkipGeneratedHeader()
    {
        int position = _pos;
        foreach (string expected in ConfigHeader.Lines)
        {
            int end = position;
            while (end < _text.Length && !IsLineBreak(_text[end]))
                end++;

            if (!_text[position..end].SequenceEqual(expected))
                return;

            if (end < _text.Length)
            {
                if (_text[end] == '\r' && end + 1 < _text.Length && _text[end + 1] == '\n')
                    end += 2;
                else
                    end++;
            }

            position = end;
        }

        _pos = position;
    }

    // ===== 表头 [section.path] =====

    private (ConfigObject Node, int Depth) ParseSectionHeader(ConfigObject root, string? comment)
    {
        _pos++; // '['
        int start = _pos;
        while (!AtEnd && Peek != ']' && !IsLineBreak(Peek))
            _pos++;
        if (AtEnd || Peek != ']')
            throw Error("表头缺少 ']'。");

        ReadOnlySpan<char> raw = _text[start.._pos].Trim();
        if (raw.IsEmpty)
            throw Error("表头 [ ] 不能为空。");
        if (raw.IndexOfAny('"', '\'') >= 0)
            throw ErrorAt("表头不支持引号；含 '.' 的键请在键值行中使用引号键名（如 \"a.b\" = 1）。", start - 1);
        _pos++; // ']'

        ConfigObject node = root;
        ConfigObject parent = root;
        string lastKey = string.Empty;
        int depth = 0;
        while (true)
        {
            int dot = raw.IndexOf('.');
            ReadOnlySpan<char> segment = (dot < 0 ? raw : raw[..dot]).Trim();
            if (segment.IsEmpty)
                throw Error($"表头 [{raw.ToString()}] 中存在空段。");

            depth++;
            CheckNestingDepth(depth);
            lastKey = segment.ToString();
            parent = node;
            node = EnterObject(node, lastKey, $"表头 [{raw.ToString()}]", _pos);
            if (dot < 0)
                break;
            raw = raw[(dot + 1)..];
        }

        SkipInlineSpace();
        string? inline = ReadInlineComment();
        if (!AtEnd && !IsLineBreak(Peek))
            throw Error($"表头 ']' 后存在多余内容 '{Peek}'。");

        if (comment is not null)
            parent.SetComment(lastKey, comment);
        if (inline is not null)
            parent.SetInlineComment(lastKey, inline);

        return (node, depth);
    }

    private ConfigObject EnterObject(ConfigObject parent, string key, string context, int position)
    {
        if (parent.TryGetValue(key, out ConfigValue existing))
        {
            if (existing.Kind == ConfigValueKind.Object)
                return existing.AsObject();
            if (existing.IsNull)
            {
                var created = new ConfigObject();
                parent.Set(key, new ConfigValue(created));
                return created;
            }

            throw ErrorAt($"{context} 中的键 \"{key}\" 已存在且为 {existing.Kind}，无法作为分组对象。", position);
        }

        var node = new ConfigObject();
        parent.Set(key, new ConfigValue(node));
        return node;
    }

    // ===== 对象 { ... } =====

    private ConfigObject ParseObjectBody(int depth)
    {
        CheckNestingDepth(depth);
        var obj = new ConfigObject();
        while (true)
        {
            ReadLeadingComments(out string? detached, out string? attached);
            string? leading = JoinComments(detached, attached);
            if (AtEnd)
                throw Error("对象缺少 '}'。");
            if (Peek == '}')
            {
                _pos++;
                obj.TrailingComment = leading;
                return obj;
            }

            if (Peek == '[')
                throw Error("对象内部不支持 [section] 表头，请把表头写在顶层，或改用 key { ... }。");
            if (Peek == ']')
                throw Error("对象内出现多余的 ']'，缺少 '}'。");

            int keyStart = _pos;
            string key = ParseKey(out bool quoted);
            SkipInlineSpace();
            if (AtEnd || IsLineBreak(Peek))
                throw Error($"键 \"{key}\" 后缺少 '=' 或 '{{'。");

            ConfigValue value;
            string? inlineComment = null;
            if (Peek == '=')
            {
                _pos++;
                value = ParseValue(depth, out inlineComment);
            }
            else if (Peek == '{')
            {
                _pos++;
                inlineComment = ReadInlineComment();
                value = new ConfigValue(ParseObjectBody(depth + 1));
            }
            else if (Peek == ':')
            {
                throw Error($"键 \"{key}\" 后应使用 '=' 而不是 ':'。");
            }
            else
            {
                throw Error($"键 \"{key}\" 后应为 '=' 或 '{{'，实际为 '{Peek}'。");
            }

            (ConfigObject target, string actualKey) =
                Assign(obj, depth, key, value, quoted, keyStart);

            if (leading is not null)
                target.SetComment(actualKey, leading);
            if (inlineComment is not null)
                target.SetInlineComment(actualKey, inlineComment);

            string? trailing = ReadInlineComment();
            if (trailing is not null)
            {
                string? existing = target.GetInlineComment(actualKey);
                target.SetInlineComment(actualKey, existing is null ? trailing : existing + " " + trailing);
            }

            SkipWhitespace(includeNewLines: true);
            if (!AtEnd && Peek == ',')
                _pos++; // 容忍 JSON 风格逗号
        }
    }

    private void CheckNestingDepth(int depth) => CheckNestingDepth(depth, _pos);

    private void CheckNestingDepth(int depth, int position)
    {
        if (depth > MaxNestingDepth)
            throw ErrorAt($"嵌套层级超过上限 {MaxNestingDepth} 层。", position);
    }

    // ===== 值 =====

    private ConfigValue ParseValue(int depth, out string? inlineComment)
    {
        // 值必须与 '=' 同行开始：不跨行、也不跳过注释。
        // 否则 "a =\n# 注释\nb = 2" 会把下一行的 "b = 2" 当成 a 的值，造成静默错乱。
        // 数组/对象内部的多行与注释由 ParseArray / ParseObjectBody 负责跳过。
        SkipInlineSpace();
        if (AtEnd)
            throw Error("缺少值。");

        inlineComment = null;
        return Peek switch
        {
            '"' or '\'' => new ConfigValue(ParseQuotedString()),
            '[' => new ConfigValue(ParseArray(depth + 1)),
            '{' => ParseInlineObject(depth + 1, out inlineComment),
            _ => ParseBareValue(),
        };
    }

    private ConfigValue ParseInlineObject(int depth, out string? inlineComment)
    {
        _pos++; // '{'
        inlineComment = ReadInlineComment();
        return new ConfigValue(ParseObjectBody(depth));
    }

    private ConfigArray ParseArray(int depth)
    {
        CheckNestingDepth(depth);
        _pos++; // '['
        var array = new ConfigArray();
        while (true)
        {
            SkipTrivia(includeNewLines: true);
            if (AtEnd)
                throw Error("数组缺少 ']'。");
            if (Peek == ']')
            {
                _pos++;
                return array;
            }

            array.Add(ParseValue(depth, out _));

            SkipTrivia(includeNewLines: true);
            if (AtEnd)
                throw Error("数组缺少 ']'。");
            if (Peek == ',')
            {
                _pos++;
                continue;
            }

            if (Peek == ']')
            {
                _pos++;
                return array;
            }

            throw Error($"数组元素之间缺少 ',' 或 ']'，实际为 '{Peek}'。");
        }
    }

    private ConfigValue ParseBareValue()
    {
        int start = _pos;
        while (!AtEnd)
        {
            char c = _text[_pos];
            if (c == '\n' || c == '\r' || c == ',' || c == ']' || c == '}')
                break;

            // 注释只在行首或空白之后开始，保证 http://host/path 之类的值不被截断
            bool atTokenHead = _pos == start || char.IsWhiteSpace(_text[_pos - 1]);
            if (c == '#' && atTokenHead)
                break;
            if (c == '/' && PeekAt(1) == '/' && atTokenHead)
                break;

            _pos++;
        }

        ReadOnlySpan<char> raw = _text[start.._pos].Trim();
        if (raw.IsEmpty)
            throw Error("缺少值。");
        return InferValue(raw);
    }

    private static ConfigValue InferValue(ReadOnlySpan<char> raw)
    {
        if (raw.Equals("null", StringComparison.OrdinalIgnoreCase))
            return ConfigValue.Null;
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
            return new ConfigValue(true);
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            return new ConfigValue(false);

        // 特殊浮点：必须识别，否则写出器产生的 NaN / Infinity 无法读回
        if (raw.Equals("NaN", StringComparison.OrdinalIgnoreCase))
            return new ConfigValue(double.NaN);
        if (raw.Equals("Infinity", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("+Infinity", StringComparison.OrdinalIgnoreCase))
            return new ConfigValue(double.PositiveInfinity);
        if (raw.Equals("-Infinity", StringComparison.OrdinalIgnoreCase))
            return new ConfigValue(double.NegativeInfinity);

        char first = raw[0];
        bool looksNumeric = first == '-' || first == '+' || first == '.' || (first >= '0' && first <= '9');
        if (looksNumeric)
        {
            if (ConfigValue.TryParseInteger(raw, out long integer))
                return new ConfigValue(integer);
            if (raw.IndexOfAny('.', 'e', 'E') >= 0
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return new ConfigValue(number);
        }

        return new ConfigValue(raw.ToString());
    }

    // ===== 键与字符串 =====

    /// <summary>
    /// 解析键名。<paramref name="quoted"/> 为 true 表示键带引号，此时键名保持字面（不按点展开路径）。
    /// </summary>
    private string ParseKey(out bool quoted)
    {
        if (AtEnd)
            throw Error("缺少键名。");

        char c = Peek;
        if (c == '"' || c == '\'')
        {
            quoted = true;
            return ParseQuotedString();
        }

        quoted = false;
        int start = _pos;
        while (!AtEnd)
        {
            char ch = _text[_pos];
            // ':' 也是终止符：便于把 "a: 1" 这种 JSON/TOML 写法报成明确的错误，
            // 键名本身含 ':' 时请使用引号（写出器会自动加引号）。
            if (ch == '=' || ch == ':' || ch == '{' || ch == '}' || ch == ']' || ch == ',' || IsLineBreak(ch))
                break;
            if (ch == '#' || (ch == '/' && PeekAt(1) == '/'))
                break;
            _pos++;
        }

        ReadOnlySpan<char> span = _text[start.._pos].Trim();
        if (span.IsEmpty)
            throw Error("缺少键名。");
        return span.ToString();
    }

    /// <summary>
    /// 赋值：裸键中的 '.' 自动展开为嵌套对象（与 [a.b] 表头一致）；引号键保持字面键名。
    /// <paramref name="targetDepth"/> 为目标容器的嵌套层级，用于限制整体深度。
    /// </summary>
    private (ConfigObject Target, string Key) Assign(ConfigObject target, int targetDepth, string key,
        ConfigValue value, bool quoted, int keyPosition)
    {
        if (quoted || key.AsSpan().IndexOf('.') < 0)
        {
            CheckNestingDepth(targetDepth + 1, keyPosition);
            target.Set(key, value);
            return (target, key);
        }

        int segmentCount = 1;
        for (int i = 0; i < key.Length; i++)
        {
            if (key[i] == '.')
                segmentCount++;
        }

        CheckNestingDepth(targetDepth + segmentCount, keyPosition);

        ConfigObject node = target;
        ReadOnlySpan<char> remaining = key.AsSpan();
        while (true)
        {
            int dot = remaining.IndexOf('.');
            if (dot < 0)
                break;
            ReadOnlySpan<char> segment = remaining[..dot];
            if (segment.IsEmpty)
                throw ErrorAt($"键 \"{key}\" 中存在空段。", keyPosition);
            node = EnterObject(node, segment.ToString(), $"键 \"{key}\"", keyPosition);
            remaining = remaining[(dot + 1)..];
        }

        if (remaining.IsEmpty)
            throw ErrorAt($"键 \"{key}\" 以 '.' 结尾。", keyPosition);

        string finalKey = remaining.ToString();
        node.Set(finalKey, value);
        return (node, finalKey);
    }

    private string ParseQuotedString()
    {
        char quote = _text[_pos++];
        StringBuilder? builder = null;
        int chunkStart = _pos;
        while (true)
        {
            if (AtEnd)
                throw Error("字符串缺少结束引号。");

            char c = Peek;
            if (c == quote)
            {
                // 单引号字符串内 '' 表示一个单引号
                if (quote == '\'' && PeekAt(1) == '\'')
                {
                    builder ??= new StringBuilder();
                    builder.Append(_text[chunkStart.._pos]);
                    builder.Append('\'');
                    _pos += 2;
                    chunkStart = _pos;
                    continue;
                }

                break;
            }

            if (IsLineBreak(c))
                throw Error("字符串中不允许直接换行（双引号字符串可用 \\n 转义）。");

            // 双引号字符串支持 \n \t \" \\ \uXXXX 等转义；单引号字符串保持字面量（便于写 Windows 路径）
            if (c == '\\' && quote == '"')
            {
                builder ??= new StringBuilder();
                builder.Append(_text[chunkStart.._pos]);
                _pos++;
                AppendEscape(builder);
                chunkStart = _pos;
                continue;
            }

            _pos++;
        }

        string result = builder is null
            ? _text[chunkStart.._pos].ToString()
            : builder.Append(_text[chunkStart.._pos]).ToString();
        _pos++; // 结束引号
        return result;
    }

    private void AppendEscape(StringBuilder builder)
    {
        if (AtEnd)
            throw Error("转义字符不完整。");

        char c = _text[_pos++];
        switch (c)
        {
            case '"': builder.Append('"'); break;
            case '\\': builder.Append('\\'); break;
            case '/': builder.Append('/'); break;
            case 'b': builder.Append('\b'); break;
            case 'f': builder.Append('\f'); break;
            case 'n': builder.Append('\n'); break;
            case 'r': builder.Append('\r'); break;
            case 't': builder.Append('\t'); break;
            case '0': builder.Append('\0'); break;
            case 'u':
                if (_pos + 4 > _text.Length)
                    throw Error("\\u 转义需要 4 位十六进制数。");
                int code = 0;
                for (int i = 0; i < 4; i++)
                {
                    int digit = ConfigValue.DigitValue(_text[_pos + i]);
                    if (digit < 0)
                        throw Error("\\u 转义需要 4 位十六进制数。");
                    code = code * 16 + digit;
                }

                _pos += 4;
                builder.Append((char)code);
                break;
            default:
                throw Error($"未知的转义序列 \\{c}。");
        }
    }

    // ===== 空白与行尾 =====

    /// <summary>跳过空格/Tab，以及注释；<paramref name="includeNewLines"/> 为 false 时遇到换行即停。</summary>
    private void SkipTrivia(bool includeNewLines)
    {
        while (!AtEnd)
        {
            char c = _text[_pos];
            if (c == ' ' || c == '\t')
            {
                _pos++;
                continue;
            }

            if (IsLineBreak(c))
            {
                if (!includeNewLines)
                    return;
                _pos++;
                continue;
            }

            if (c == '#' || (c == '/' && PeekAt(1) == '/'))
            {
                while (!AtEnd && !IsLineBreak(_text[_pos]))
                    _pos++;
                continue;
            }

            break;
        }
    }

    private void SkipInlineSpace()
    {
        while (!AtEnd && (Peek == ' ' || Peek == '\t'))
            _pos++;
    }

    /// <summary>只跳过空白（可选换行），不消耗注释；用于条目之间保留注释。</summary>
    private void SkipWhitespace(bool includeNewLines)
    {
        while (!AtEnd)
        {
            char c = Peek;
            if (c == ' ' || c == '\t')
            {
                _pos++;
                continue;
            }

            if (IsLineBreak(c))
            {
                if (!includeNewLines)
                    return;
                _pos++;
                continue;
            }

            return;
        }
    }

    /// <summary>
    /// 跳过空白与注释，并把注释拆成两块：
    /// <paramref name="detached"/> 为与后续条目之间隔了空行的块，<paramref name="attached"/> 为紧贴后续条目的块。
    /// 两块的每行都已去掉注释符号与首尾空白，多行用 '\n' 连接；没有注释时为 <c>null</c>。
    /// </summary>
    private void ReadLeadingComments(out string? detached, out string? attached)
    {
        StringBuilder? current = null;
        StringBuilder? earlier = null;
        int lineBreaks = 0;

        while (!AtEnd)
        {
            char c = Peek;
            if (c == ' ' || c == '\t')
            {
                _pos++;
                continue;
            }

            if (IsLineBreak(c))
            {
                // CRLF 只算一次换行，否则连续注释会被误判为空行分隔
                if (c == '\r' && _pos + 1 < _text.Length && _text[_pos + 1] == '\n')
                    _pos += 2;
                else
                    _pos++;
                lineBreaks++;
                continue;
            }

            if (c == '#' || (c == '/' && PeekAt(1) == '/'))
            {
                // 与上一块之间隔了空行：上一块不再属于紧随的条目
                if (lineBreaks >= 2 && current is not null)
                {
                    earlier ??= new StringBuilder();
                    if (earlier.Length > 0)
                        earlier.Append('\n');
                    earlier.Append(current);
                    current.Clear();
                }

                string text = ReadCommentLine();
                if (text.Length > 0)
                {
                    current ??= new StringBuilder();
                    if (current.Length > 0)
                        current.Append('\n');
                    current.Append(text);
                }

                lineBreaks = 0;
                continue;
            }

            break;
        }

        // 注释与后续条目之间隔了空行：整块移到 detached
        if (lineBreaks >= 2 && current is not null)
        {
            earlier ??= new StringBuilder();
            if (earlier.Length > 0)
                earlier.Append('\n');
            earlier.Append(current);
            current.Clear();
        }

        detached = earlier?.ToString();
        attached = current is { Length: > 0 } ? current.ToString() : null;
    }

    private static string? JoinComments(string? first, string? second)
        => first is null ? second : second is null ? first : first + '\n' + second;

    /// <summary>读取当前位置的注释内容（不含注释符号），停在行尾。</summary>
    private string ReadCommentLine()
    {
        _pos += Peek == '#' ? 1 : 2;
        if (!AtEnd && Peek == ' ')
            _pos++;

        int start = _pos;
        while (!AtEnd && !IsLineBreak(Peek))
            _pos++;
        return _text[start.._pos].ToString().Trim();
    }

    /// <summary>读取同一行尾部的注释；没有注释时返回 <c>null</c>（不消费换行）。</summary>
    private string? ReadInlineComment()
    {
        SkipInlineSpace();
        if (AtEnd)
            return null;
        return Peek == '#' || (Peek == '/' && PeekAt(1) == '/')
            ? ReadCommentLine()
            : null;
    }

    private string? FinishLine()
    {
        SkipInlineSpace();
        string? comment = null;

        if (!AtEnd && (Peek == '#' || (Peek == '/' && PeekAt(1) == '/')))
        {
            comment = ReadCommentLine();
        }
        else if (!AtEnd && Peek == ',')
        {
            _pos++; // 容忍 JSON 风格逗号
            SkipInlineSpace();
            if (!AtEnd && (Peek == '#' || (Peek == '/' && PeekAt(1) == '/')))
                comment = ReadCommentLine();
        }

        SkipInlineSpace();
        if (AtEnd)
            return comment;

        if (Peek == '\r')
        {
            _pos++;
            if (!AtEnd && Peek == '\n')
                _pos++;
            return comment;
        }

        if (Peek == '\n')
        {
            _pos++;
            return comment;
        }

        throw Error($"值后存在多余内容 '{Peek}'。");
    }

    // ===== 错误定位 =====

    private ConfigFormatException Error(string message) => ErrorAt(message, _pos);

    private ConfigFormatException ErrorAt(string message, int position)
    {
        (int line, int column) = GetLineColumn(position);
        return new ConfigFormatException(message, line, column);
    }

    private (int Line, int Column) GetLineColumn(int position)
    {
        int line = 1;
        int lineStart = 0;
        int limit = Math.Min(position, _text.Length);
        for (int i = 0; i < limit; i++)
        {
            char c = _text[i];
            if (c == '\n')
            {
                line++;
                lineStart = i + 1;
            }
            else if (c == '\r')
            {
                // CRLF 只算一次换行；老式 CR 换行也要计入行号
                if (i + 1 < limit && _text[i + 1] == '\n')
                    continue;
                line++;
                lineStart = i + 1;
            }
        }

        return (line, limit - lineStart + 1);
    }
}
