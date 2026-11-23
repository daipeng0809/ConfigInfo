using System.Text;
using ConfigInfo;

namespace ConfigInfo.Tests;

/// <summary>针对审计发现问题的回归测试：往返完整性、数值边界、深层嵌套与文件安全。</summary>
public class WriterRoundtripTests
{
    [Fact]
    public void 点号键必须加引号并保持字面键名()
    {
        var document = new ConfigDocument();
        document.Root["a.b"] = 1;
        string text = document.ToText(includeHeader: false);

        Assert.Contains("\"a.b\" = 1", text);
        ConfigObject root = ConfigDocument.Parse(text).Root;
        Assert.Equal(1, root.GetInt32("a.b"));
        Assert.False(root.ContainsKey("a"));
        Assert.Equal(1, root.Count);
    }

    [Fact]
    public void 特殊键名往返()
    {
        string[] keys =
        [
            "", "with space", "a#b", "a//b", "quote\"key", "back\\slash",
            "tab\tkey", "换行\n键", "中文.键", "[bracket]", "comma,key", "a=b", "colon:key",
        ];

        var document = new ConfigDocument();
        for (int i = 0; i < keys.Length; i++)
            document.Root[keys[i]] = i;

        ConfigObject again = ConfigDocument.Parse(document.ToText()).Root;
        Assert.Equal(keys.Length, again.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, again.Get(keys[i]).AsInt32());
    }

    [Fact]
    public void 空对象与空数组往返()
    {
        var document = new ConfigDocument();
        document.Root.GetObject("empty");
        document.Root.GetArray("list");

        string text = document.ToText();
        Assert.Contains("empty = {}", text);
        Assert.Contains("list = []", text);

        ConfigObject root = ConfigDocument.Parse(text).Root;
        Assert.Empty(root.GetPath("empty").AsObject());
        Assert.Empty(root.GetPath("list").AsArray());
    }

    [Fact]
    public void 多行数组往返与幂等()
    {
        var document = new ConfigDocument();
        var array = document.Root.GetArray("items");
        for (int i = 0; i < 60; i++)
            array.Add(100000 + i);

        string text1 = document.ToText();
        Assert.Contains("[\n", text1);
        string text2 = ConfigDocument.Parse(text1).ToText();
        Assert.Equal(text1, text2);
        Assert.Equal(60, ConfigDocument.Parse(text1).Root.GetPath("items").AsArray().Count);
    }

    [Fact]
    public void 增补平面字符与孤立代理项往返()
    {
        const string emoji = "帧头😀结束";
        string lone = "孤立" + '\uD800' + "代理";

        var document = new ConfigDocument();
        document.Root["emoji"] = emoji;
        document.Root["lone"] = lone;

        ConfigObject again = ConfigDocument.Parse(document.ToText()).Root;
        Assert.Equal(emoji, again.GetString("emoji"));
        Assert.Equal(lone, again.GetString("lone"));
    }

    [Fact]
    public void NaN与无穷大往返()
    {
        var document = new ConfigDocument();
        document.Root["nan"] = double.NaN;
        document.Root["pos"] = double.PositiveInfinity;
        document.Root["neg"] = double.NegativeInfinity;
        document.Root["nf"] = float.NaN;
        document.Root["big"] = 1e20;
        document.Root["tiny"] = -0.0;

        string text = document.ToText();
        Assert.Contains("NaN", text);
        Assert.Contains("Infinity", text);

        ConfigObject again = ConfigDocument.Parse(text).Root;
        Assert.True(again.Get("nan").IsFloat);
        Assert.True(double.IsNaN(again.GetDouble("nan")));
        Assert.Equal(double.PositiveInfinity, again.GetDouble("pos"));
        Assert.Equal(double.NegativeInfinity, again.GetDouble("neg"));
        Assert.True(again.Get("nf").IsFloat);
        Assert.Equal(1e20, again.GetDouble("big"));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(again.GetDouble("tiny")));
    }

    [Fact]
    public void 浮点格式保持Float类型()
    {
        var document = new ConfigDocument();
        document.Root["a"] = 3.0;
        document.Root["b"] = 1e9;
        document.Root["c"] = 0.1;

        ConfigObject again = ConfigDocument.Parse(document.ToText()).Root;
        Assert.True(again.Get("a").IsFloat);
        Assert.True(again.Get("b").IsFloat);
        Assert.True(again.Get("c").IsFloat);
        Assert.Equal(0.1, again.GetDouble("c"));
    }

    [Fact]
    public void 表头写回为块对象且结构稳定()
    {
        ConfigObject root = ConfigDocument.Parse("[a.b]\nc = 1\n[a.d]\ne = 2").Root;
        string text = new ConfigDocument(root).ToText(includeHeader: false);

        Assert.Contains("a {", text);
        Assert.Contains("b {", text);
        ConfigObject again = ConfigDocument.Parse(text).Root;
        Assert.Equal(1, again.GetInt32("a.b.c"));
        Assert.Equal(2, again.GetInt32("a.d.e"));
    }
}

public class NumericBoundaryTests
{
    [Fact]
    public void 双精度2的63次方不被静默截断()
    {
        var node = new ConfigObject();
        node["x"] = 9223372036854775808d;   // 2^63，(double)long.MaxValue
        node["y"] = -9223372036854775808d;  // -2^63 == long.MinValue，合法

        Assert.Equal(-1, node.GetInt64("x", -1));
        Assert.Equal(long.MinValue, node.GetInt64("y"));
        Assert.Throws<InvalidCastException>(() => node.Get("x").AsInt64());
    }

    [Fact]
    public void decimal边界值返回默认值而不是抛异常()
    {
        var node = new ConfigObject();
        node["max"] = (double)decimal.MaxValue;
        node["min"] = (double)decimal.MinValue;
        node["normal"] = 0.1;

        Assert.Equal(5m, node.GetDecimal("max", 5m));
        Assert.Equal(6m, node.GetDecimal("min", 6m));
        Assert.Equal(0.1m, node.GetDecimal("normal"));
        Assert.Throws<InvalidCastException>(() => node.Get("max").AsDecimal());
    }

    [Fact]
    public void 整数下划线规则()
    {
        ConfigObject root = ConfigDocument.Parse("""
            ok = 1_000
            trailing = 1_
            duplicate = 1__0
            hexLeading = 0x_1F
            hexMiddle = 0x1_F
            bin = 0b1010_1010
            """).Root;

        Assert.Equal(1000, root.GetInt32("ok"));
        Assert.Equal(31, root.GetInt32("hexMiddle"));
        Assert.Equal(0b1010_1010, root.GetInt32("bin"));

        Assert.True(root.Get("trailing").IsString);
        Assert.True(root.Get("duplicate").IsString);
        Assert.True(root.Get("hexLeading").IsString);
    }

    [Fact]
    public void 十六进制位模式与超长字面量()
    {
        ConfigObject root = ConfigDocument.Parse("""
            mask = 0xFFFFFFFFFFFFFFFF
            overflow = 0x1_0000_0000_0000_0000
            min = -0x8000_0000_0000_0000
            """).Root;

        Assert.Equal(-1L, root.GetInt64("mask"));
        Assert.Equal(ulong.MaxValue, root.Get("mask").AsUInt64());
        Assert.True(root.Get("overflow").IsString);
        Assert.Equal(long.MinValue, root.GetInt64("min"));
    }

    [Fact]
    public void ulong补码往返()
    {
        var node = new ConfigObject();
        node["max"] = ulong.MaxValue;

        Assert.Equal(-1L, node.GetInt64("max"));
        Assert.Equal(ulong.MaxValue, node.Get("max").AsUInt64());
        Assert.Equal("-1", node.GetString("max"));
    }

    [Fact]
    public void 指数溢出按IEEE规则处理()
    {
        ConfigValue value = ConfigDocument.Parse("a = 1e400").Root.Get("a");
        Assert.True(value.IsFloat);
        Assert.Equal(double.PositiveInfinity, value.AsDouble());
    }

    [Fact]
    public void 布尔与浮点转换()
    {
        var node = new ConfigObject { ["f"] = 2.9, ["b"] = true, ["s"] = " 42 " };

        Assert.Equal(2, node.GetInt32("f"));
        Assert.Equal(1, node.GetInt32("b"));
        Assert.Equal(42, node.GetInt32("s"));
        Assert.Equal(2.9f, node.GetSingle("f"));
    }
}

public class NullAndEqualityTests
{
    [Fact]
    public void null读取字符串回退默认值()
    {
        var node = new ConfigObject { ["x"] = ConfigValue.Null };
        Assert.Equal("默认", node.GetString("x", "默认"));
        Assert.Throws<InvalidCastException>(() => node.Get("x").AsString());

        var array = new ConfigArray { ConfigValue.Null };
        Assert.Equal("默认", array.GetString(0, "默认"));
    }

    [Fact]
    public void 值相等性不跨类型()
    {
        Assert.Equal(new ConfigValue(1), new ConfigValue(1));
        Assert.NotEqual(new ConfigValue(1), new ConfigValue(1.0));
        Assert.Equal(new ConfigValue("a"), new ConfigValue("a"));
        Assert.NotEqual(new ConfigValue("A"), new ConfigValue("a"));
        // 浮点按位比较：相同位模式的 NaN 视为相等，与 GetHashCode 保持一致
        Assert.Equal(new ConfigValue(double.NaN), new ConfigValue(double.NaN));
        Assert.Equal(ConfigValue.Null, default(ConfigValue));
    }

    [Fact]
    public void 数组引用相等性()
    {
        var array = new ConfigArray();
        var value = new ConfigValue(array);
        Assert.Equal(value, new ConfigValue(array));
        Assert.NotEqual(value, new ConfigValue(new ConfigArray()));
    }
}

public class ParserRobustnessTests
{
    [Fact]
    public void BOM开头的文本可正常解析()
    {
        ConfigObject root = ConfigDocument.Parse("\uFEFFa = 1\n[b]\nc = 2").Root;
        Assert.Equal(1, root.GetInt32("a"));
        Assert.Equal(2, root.GetInt32("b.c"));
    }

    [Fact]
    public void 纯CR换行也能正确定位行号()
    {
        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse("a = 1\rb = "));
        Assert.Equal(2, exception.Line);
    }

    [Fact]
    public void 嵌套数组超过深度上限抛出格式异常()
    {
        string text = "a = " + new string('[', 400) + new string(']', 400);
        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse(text));
        Assert.Contains("嵌套层级", exception.Message);
        Assert.True(exception.Line > 0);
    }

    [Fact]
    public void 嵌套对象超过深度上限抛出格式异常()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < 400; i++)
            builder.Append("k { ");
        builder.Append("x = 1");
        for (int i = 0; i < 400; i++)
            builder.Append(" }");

        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse(builder.ToString()));
        Assert.Contains("嵌套层级", exception.Message);
    }

    [Fact]
    public void 超长点路径超过深度上限抛出格式异常()
    {
        string key = "a" + string.Concat(Enumerable.Repeat(".a", 400));
        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse(key + " = 1"));
        Assert.Contains("嵌套层级", exception.Message);
    }

    [Fact]
    public void 合理深度仍可正常往返()
    {
        string key = "a" + string.Concat(Enumerable.Repeat(".a", 200));
        ConfigObject root = ConfigDocument.Parse(key + " = 1").Root;
        Assert.Equal(1, root.GetPath(key).AsInt32());
        Assert.Equal(1, ConfigDocument.Parse(new ConfigDocument(root).ToText()).Root.GetPath(key).AsInt32());
    }

    [Fact]
    public void 写出超深结构抛出明确异常()
    {
        var document = new ConfigDocument();
        ConfigObject node = document.Root;
        for (int i = 0; i < 400; i++)
        {
            var child = new ConfigObject();
            node.Set("k", child);
            node = child;
        }

        Assert.Throws<InvalidOperationException>(() => document.ToText());
    }

    [Fact]
    public void 缺少值时不吞掉下一行()
    {
        var exception = Assert.Throws<ConfigFormatException>(() =>
            ConfigDocument.Parse("a =\n# 注释\nb = 2"));
        Assert.Equal(1, exception.Line);
        Assert.Contains("缺少值", exception.Message);
    }

    [Fact]
    public void 数组内部允许换行与注释()
    {
        ConfigObject root = ConfigDocument.Parse("""
            a = [
                1,   # 第一项
                2,   // 第二项
                3
            ]
            """).Root;

        Assert.Equal(new[] { 1, 2, 3 }, root.GetPath("a").AsArray().ToInt32Array());
    }

    [Fact]
    public void 以井号开头的裸值必须加引号()
    {
        Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse("color = #FF0000"));
        Assert.Equal("#FF0000", ConfigDocument.Parse("color = \"#FF0000\"").Root.GetString("color"));
    }

    [Fact]
    public void 冒号分隔符给出明确提示()
    {
        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse("a: 1"));
        Assert.Contains("'='", exception.Message);
    }

    [Fact]
    public void 对象内表头给出明确提示()
    {
        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse("a {\n[b]\n}"));
        Assert.Contains("表头", exception.Message);
    }

    [Fact]
    public void 表头中的引号给出明确提示而不是生成怪异键名()
    {
        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse("[\"a.b\"]\nx = 1"));
        Assert.Contains("表头不支持引号", exception.Message);

        // 含 '.' 的字面键应在键值行中使用引号写法
        ConfigObject root = ConfigDocument.Parse("\"a.b\" = 1").Root;
        Assert.Equal(1, root.GetInt32("a.b"));
        Assert.Equal(1, root.Count);
    }

    [Fact]
    public void TryParse返回行号且不抛异常()
    {
        Assert.True(ConfigDocument.TryParse("a = 1", out ConfigDocument? document, out ConfigFormatException? error));
        Assert.NotNull(document);
        Assert.Null(error);

        Assert.False(ConfigDocument.TryParse("a = ", out _, out ConfigFormatException? formatError));
        Assert.NotNull(formatError);
        Assert.Equal(1, formatError.Line);

        Assert.False(ConfigDocument.TryParse(null, out _, out ConfigFormatException? nullError));
        Assert.NotNull(nullError);
    }
}

public class CollectionSafetyTests
{
    [Fact]
    public void 枚举期间新增会抛出异常()
    {
        var node = new ConfigObject { ["a"] = 1 };
        using ConfigObject.Enumerator enumerator = node.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        node.Set("b", 2);
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void 枚举期间删除或清空会抛出异常()
    {
        var node = new ConfigObject { ["a"] = 1, ["b"] = 2 };

        var first = node.GetEnumerator();
        Assert.True(first.MoveNext());
        node.Remove("b");
        Assert.Throws<InvalidOperationException>(() => first.MoveNext());

        var second = node.GetEnumerator();
        Assert.True(second.MoveNext());
        node.Clear();
        Assert.Throws<InvalidOperationException>(() => second.MoveNext());
    }

    [Fact]
    public void 枚举值修改会抛出异常()
    {
        var node = new ConfigObject { ["a"] = 1, ["b"] = 2 };
        var enumerator = node.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        node["a"] = 3;
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }
}

public class SaveSafetyTests
{
    [Fact]
    public void 原子保存不残留临时文件()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ConfigInfoAudit_" + Guid.NewGuid().ToString("N"));
        try
        {
            var document = new ConfigDocument();
            document.Root["a"] = 1;
            string path = Path.Combine(dir, "app.cif");

            document.Save(path);
            document.Root["a"] = 2;
            document.Save(path);

            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.Empty(Directory.GetFiles(dir, ".*.tmp"));
            Assert.Equal(2, ConfigDocument.Load(path).Root.GetInt32("a"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TrySave非法路径返回错误()
    {
        var document = new ConfigDocument();
        bool ok = document.TrySave("bad\0path.cif", out string? error);
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void 非原子保存同样可用()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ConfigInfoAudit_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "app.cif");
            var document = new ConfigDocument();
            document.Root.SetPath("a.b", "值");
            document.Save(path, atomic: false, includeHeader: false);

            Assert.Equal("值", ConfigDocument.Load(path).Root.GetString("a.b"));
            Assert.False(File.ReadAllText(path).StartsWith('#'));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
