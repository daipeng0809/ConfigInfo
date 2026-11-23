using System.Text;
using ConfigInfo;

namespace ConfigInfo.Tests;

public class ParsingTests
{
    private const string Sample = """
        # 注释
        app.name = "信息调试助手"
        app.version = 'C:\Program Files\App\1.0'
        app.port = 8080
        app.ratio = 0.75
        app.enabled = true
        app.hex = 0x1F
        app.bin = 0b1010
        app.nothing = null
        app.url = http://127.0.0.1:8080/api?x=1#frag
        app.tags = [com, "tcp", 3]
        app.matrix = [[1, 2], [3, 4]]
        app.meta = { author = daipeng, level = 2 }

        [serial]
        com = COM3
        baud = "115200"
        frame = [0xAA, 0x55]
        stop = { bits = 1.5, name = One }

        [network.tcp]
        mode = Client
        remote {
            ip = 127.0.0.1
            port = 8080
        }
        """;

    [Fact]
    public void 双引号与单引号字符串()
    {
        ConfigObject root = ConfigDocument.Parse(Sample).Root;
        Assert.Equal("信息调试助手", root.GetString("app.name"));
        Assert.Equal(@"C:\Program Files\App\1.0", root.GetString("app.version"));
    }

    [Fact]
    public void 数字与布尔与空值()
    {
        ConfigObject root = ConfigDocument.Parse(Sample).Root;
        Assert.Equal(8080, root.GetInt32("app.port"));
        Assert.Equal(0.75, root.GetDouble("app.ratio"), 12);
        Assert.True(root.GetBoolean("app.enabled"));
        Assert.Equal(31, root.GetInt32("app.hex"));
        Assert.Equal(10, root.GetInt32("app.bin"));
        Assert.True(root.TryGetPath("app.nothing", out ConfigValue nothing));
        Assert.True(nothing.IsNull);
    }

    [Fact]
    public void 裸值中的双斜杠与井号不当注释()
    {
        ConfigObject root = ConfigDocument.Parse(Sample).Root;
        Assert.Equal("http://127.0.0.1:8080/api?x=1#frag", root.GetString("app.url"));
    }

    [Fact]
    public void 数组与嵌套数组与内联对象()
    {
        ConfigObject root = ConfigDocument.Parse(Sample).Root;
        ConfigArray tags = root.GetPath("app.tags").AsArray();
        Assert.Equal(3, tags.Count);
        Assert.Equal("com", tags.GetString(0));
        Assert.Equal("tcp", tags.GetString(1));
        Assert.Equal(3, tags.GetInt32(2));

        ConfigArray matrix = root.GetPath("app.matrix").AsArray();
        Assert.Equal(3, matrix[1].AsArray().GetInt32(0));

        ConfigObject meta = root.GetPath("app.meta").AsObject();
        Assert.Equal("daipeng", meta.GetString("author"));
        Assert.Equal(2, meta.GetInt32("level"));
    }

    [Fact]
    public void 表头与块对象()
    {
        ConfigObject root = ConfigDocument.Parse(Sample).Root;
        Assert.Equal("COM3", root.GetString("serial.com"));
        Assert.Equal(115200, root.GetInt32("serial.baud"));
        Assert.True(root.GetPath("serial.baud").IsString);
        Assert.Equal(new[] { 0xAA, 0x55 }, root.GetPath("serial.frame").AsArray().ToInt32Array());
        Assert.Equal(1.5, root.GetDouble("serial.stop.bits"), 12);
        Assert.Equal("One", root.GetString("serial.stop.name"));
        Assert.Equal("Client", root.GetString("network.tcp.mode"));
        Assert.Equal(8080, root.GetInt32("network.tcp.remote.port"));
        Assert.Equal(3, root.Count);
    }

    [Fact]
    public void 重复键后者覆盖()
    {
        ConfigObject root = ConfigDocument.Parse("a = 1\na = 2").Root;
        Assert.Equal(2, root.GetInt32("a"));
        Assert.Equal(1, root.Count);
    }

    [Fact]
    public void 三种注释均被忽略()
    {
        ConfigObject root = ConfigDocument.Parse("a = 1 // 注释\n# 整行\nb = 2 # 注释").Root;
        Assert.Equal(2, root.Count);
        Assert.Equal(2, root.GetInt32("b"));
    }

    [Fact]
    public void CRLF换行()
    {
        ConfigObject root = ConfigDocument.Parse("a = 1\r\n[b]\r\nc = 2").Root;
        Assert.Equal(2, root.GetInt32("b.c"));
    }
}

public class ErrorTests
{
    [Theory]
    [InlineData("a = [1, 2", 1)]
    [InlineData("a = 1\nb = ", 2)]
    [InlineData("a = 1\n  b", 2)]
    [InlineData("a = \"abc", 1)]
    [InlineData("a = \"ab\ncd\"", 1)]
    public void 语法错误包含行号(string text, int expectedLine)
    {
        var exception = Assert.Throws<ConfigFormatException>(() => ConfigDocument.Parse(text));
        Assert.Equal(expectedLine, exception.Line);
        Assert.True(exception.Column > 0);
    }
}

public class LooseConversionTests
{
    private readonly ConfigObject _node = new()
    {
        ["s"] = "115200",
        ["b"] = "yes",
        ["n"] = 5,
        ["flag"] = true,
        ["f"] = 2.5,
    };

    [Fact]
    public void 字符串转数字()
    {
        Assert.Equal(115200, _node.GetInt32("s"));
        Assert.Equal(115200d, _node.GetDouble("s"), 12);
    }

    [Fact]
    public void 字符串转布尔()
    {
        Assert.True(_node.GetBoolean("b"));
        Assert.False(_node.GetBoolean("nope"));
    }

    [Fact]
    public void 数字与布尔转字符串()
    {
        Assert.Equal("5", _node.GetString("n"));
        Assert.Equal("2.5", _node.GetString("f"));
        Assert.Equal("true", _node.GetString("flag"));
    }

    [Fact]
    public void 布尔转整数()
    {
        Assert.Equal(1, _node.GetInt64("flag"));
    }

    [Fact]
    public void 缺失或不可转换取默认值()
    {
        Assert.Equal(-1, _node.GetInt32("missing", -1));
        Assert.Equal(-7, _node.GetInt32("b", -7));
        Assert.Equal("默认", _node.GetString("missing", "默认"));
    }

    [Fact]
    public void 整数溢出回退默认值()
    {
        _node["huge"] = long.MaxValue;
        Assert.Equal(3, _node.GetInt32("huge", 3));
        Assert.Equal(long.MaxValue, _node.GetInt64("huge"));
    }
}

public class PathTests
{
    [Fact]
    public void SetPath自动创建中间层()
    {
        var document = new ConfigDocument();
        document.Root.SetPath("a.b.c", 42);
        Assert.Equal(42, document.Root.GetInt32("a.b.c"));
    }

    [Fact]
    public void GetArray自动创建()
    {
        var document = new ConfigDocument();
        ConfigArray list = document.Root.GetArray("a.b.list");
        list.Add(1);
        list.Add("two");
        Assert.Equal("two", document.Root.GetPath("a.b.list").AsArray().GetString(1));
    }

    [Fact]
    public void 结构冲突不静默覆盖()
    {
        var document = new ConfigDocument();
        document.Root.SetPath("a.b.c", 42);
        Assert.Throws<InvalidOperationException>(() => document.Root.GetObject("a.b.c.d"));
    }

    [Fact]
    public void 字面键优先于路径()
    {
        var node = new ConfigObject { ["a.b"] = 1 };
        Assert.Equal(1, node.GetInt32("a.b"));
    }
}

public class RoundtripTests
{
    [Fact]
    public void 特殊字符与类型往返()
    {
        var document = new ConfigDocument();
        document.Root["str"] = "含\"引号\\反斜杠\n换行\t制表符 中文";
        document.Root["big"] = 9007199254740993L;
        document.Root["float"] = 3.0;
        document.Root["flag"] = true;
        document.Root["nothing"] = ConfigValue.Null;

        ConfigArray frame = document.Root.GetArray("serial.frame");
        frame.Add(0xAA);
        frame.Add(0xFF);
        ConfigObject option = document.Root.GetObject("serial.option");
        option.Set("name", "值 中");
        option.Set("ratio", 0.1);
        ConfigArray items = option.GetArray("items");
        items.Add("x");
        items.Add(2);

        string text1 = document.ToText();
        string text2 = ConfigDocument.Parse(text1).ToText();
        Assert.Equal(text1, text2);

        ConfigObject again = ConfigDocument.Parse(text1).Root;
        Assert.Equal("含\"引号\\反斜杠\n换行\t制表符 中文", again.GetString("str"));
        Assert.Equal(9007199254740993L, again.GetInt64("big"));
        Assert.True(again.Get("float").IsFloat);
        Assert.Equal(3.0, again.GetDouble("float"));
        Assert.True(again.Get("nothing").IsNull);
        Assert.Equal(new[] { 0xAA, 0xFF }, again.GetPath("serial.frame").AsArray().ToInt32Array());
        Assert.Equal("值 中", again.GetString("serial.option.name"));
        Assert.Equal(2, again.GetPath("serial.option.items").AsArray().GetInt32(1));
    }
}

public class FileTests
{
    [Fact]
    public void 保存与加载()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ConfigInfoTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "sub", "test.cif");
            var document = new ConfigDocument();
            document.Root.SetPath("app.name", "测试 中文");
            document.Save(path);

            Assert.True(File.Exists(path));
            Assert.Equal("测试 中文", ConfigDocument.Load(path).Root.GetString("app.name"));

            string content = File.ReadAllText(path);
            Assert.StartsWith("# =====", content, StringComparison.Ordinal);
            Assert.False(content.StartsWith('\uFEFF'));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UTF16自动识别()
    {
        string path = Path.Combine(Path.GetTempPath(), "ConfigInfoUtf16_" + Guid.NewGuid().ToString("N") + ".cif");
        try
        {
            File.WriteAllText(path, "name = 兼容UTF16", Encoding.Unicode);
            Assert.Equal("兼容UTF16", ConfigDocument.Load(path).Root.GetString("name"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryLoad不存在文件返回错误()
    {
        bool ok = ConfigDocument.TryLoad(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.cif"),
            out _, out string? error);
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
