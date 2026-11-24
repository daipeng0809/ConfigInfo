using ConfigInfo;

namespace ConfigInfo.Tests;

public class CommentWriteTests
{
    [Fact]
    public void 写出前置与行尾注释()
    {
        var document = new ConfigDocument();
        document.Root.Comment = "文件说明";
        document.Root["name"] = "app";
        document.Root.SetComment("name", "应用名称");
        document.Root.SetInlineComment("name", "必填");

        ConfigObject serial = document.Root.GetObject("serial");
        serial.Comment = "串口分组";
        serial.Set("port", "COM1");
        serial.SetComment("port", "串口名");
        serial.SetInlineComment("port", "如 COM3");
        serial.TrailingComment = "串口结束";

        string text = document.ToText(includeHeader: false);

        Assert.Contains("# 文件说明", text, StringComparison.Ordinal);
        Assert.Contains("# 应用名称", text, StringComparison.Ordinal);
        Assert.Contains("name = \"app\"  # 必填", text, StringComparison.Ordinal);
        Assert.Contains("# 串口分组", text, StringComparison.Ordinal);
        Assert.Contains("# 串口名", text, StringComparison.Ordinal);
        Assert.Contains("port = \"COM1\"  # 如 COM3", text, StringComparison.Ordinal);
        Assert.Contains("# 串口结束", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 覆盖值保留注释移除后重新添加为空()
    {
        var obj = new ConfigObject();
        obj["a"] = 1;
        obj.SetComment("a", "说明");
        obj.SetInlineComment("a", "行尾");

        obj["a"] = 2; // 覆盖值
        Assert.Equal("说明", obj.GetComment("a"));
        Assert.Equal("行尾", obj.GetInlineComment("a"));

        obj.Remove("a");
        obj["a"] = 3;
        Assert.Null(obj.GetComment("a"));
        Assert.Null(obj.GetInlineComment("a"));
    }

    [Fact]
    public void 给不存在的键写注释抛出异常()
    {
        var obj = new ConfigObject();
        Assert.Throws<KeyNotFoundException>(() => obj.SetComment("missing", "x"));
        Assert.Throws<KeyNotFoundException>(() => obj.SetInlineComment("missing", "x"));
        Assert.Null(obj.GetComment("missing"));
        Assert.Null(obj.GetInlineComment("missing"));
    }
}

public class CommentParseTests
{
    private const string Sample = """
        # 文件头说明

        // 应用名
        app.name = demo   # 行尾说明

        serial {
            # 串口名
            com = COM3
            # 块尾说明
        }
        # 文件末尾
        """;

    [Fact]
    public void 读取文件级条目级与行尾注释()
    {
        ConfigObject root = ConfigDocument.Parse(Sample).Root;

        Assert.Equal("文件头说明", root.Comment);
        Assert.Equal("应用名", root.GetObject("app").GetComment("name"));
        Assert.Equal("行尾说明", root.GetObject("app").GetInlineComment("name"));
        Assert.Equal("串口名", root.GetObject("serial").GetComment("com"));
        Assert.Equal("块尾说明", root.GetObject("serial").TrailingComment);
        Assert.Equal("文件末尾", root.TrailingComment);
    }

    [Fact]
    public void 注释随文本往返且幂等()
    {
        string text1 = ConfigDocument.Parse(Sample).ToText(includeHeader: false);
        ConfigDocument again = ConfigDocument.Parse(text1);

        Assert.Equal("文件头说明", again.Root.Comment);
        Assert.Equal("应用名", again.Root.GetObject("app").GetComment("name"));
        Assert.Equal("块尾说明", again.Root.GetObject("serial").TrailingComment);
        Assert.Equal("文件末尾", again.Root.TrailingComment);

        string text2 = again.ToText(includeHeader: false);
        Assert.Equal(text1, text2);
    }

    [Fact]
    public void 块对象与表头的行尾注释()
    {
        const string text = """
            serial {   // 串口组
                # 名称
                port = COM1
            }

            # 网络分组
            [network.tcp]  # TCP
            remotePort = 8080
            """;

        ConfigObject root = ConfigDocument.Parse(text).Root;
        Assert.Equal("串口组", root.GetInlineComment("serial"));
        Assert.Equal("名称", root.GetObject("serial").GetComment("port"));
        Assert.Equal("网络分组", root.GetObject("network").GetComment("tcp"));
        Assert.Equal("TCP", root.GetObject("network").GetInlineComment("tcp"));
    }

    [Fact]
    public void 数组内部注释不影响取值()
    {
        const string text = """
            # 帧头
            frame = [
                # 第一个字节
                0xAA,  // 第二个字节
                0x55,
            ]
            """;

        ConfigObject root = ConfigDocument.Parse(text).Root;
        Assert.Equal("帧头", root.GetComment("frame"));
        Assert.Equal(new[] { 0xAA, 0x55 }, root.GetPath("frame").AsArray().ToInt32Array());
    }

    [Fact]
    public void 注释可以清除且空白注释视为清除()
    {
        var obj = new ConfigObject();
        obj["a"] = 1;
        obj.SetComment("a", "说明");
        obj.SetComment("a", "   ");
        Assert.Null(obj.GetComment("a"));

        obj.SetInlineComment("a", "行尾");
        obj.SetInlineComment("a", null);
        Assert.Null(obj.GetInlineComment("a"));
    }

    [Fact]
    public void CRLF连续注释不被误拆()
    {
        ConfigObject root = ConfigDocument.Parse("# 第一行\r\n# 第二行\r\nport = 8080\r\n").Root;

        Assert.Equal("第一行\n第二行", root.GetComment("port"));
        Assert.Null(root.Comment);
    }

    [Fact]
    public void 空文档的头尾注释可往返()
    {
        var document = new ConfigDocument();
        document.Root.Comment = "文件头";
        document.Root.TrailingComment = "文件尾";

        ConfigObject root = ConfigDocument.Parse(document.ToText(includeHeader: false)).Root;

        Assert.Equal("文件头", root.Comment);
        Assert.Equal("文件尾", root.TrailingComment);
    }

    [Fact]
    public void 空对象内部尾注释可往返()
    {
        ConfigObject root = ConfigDocument.Parse("""
            a {
                # 空分组说明
            }
            """).Root;

        Assert.Equal("空分组说明", root.GetObject("a").TrailingComment);

        string text = new ConfigDocument(root).ToText(includeHeader: false);
        Assert.Contains("# 空分组说明", text, StringComparison.Ordinal);

        ConfigObject again = ConfigDocument.Parse(text).Root;
        Assert.Equal("空分组说明", again.GetObject("a").TrailingComment);
    }
}

public class CommentFileTests
{
    [Fact]
    public void 标准头不会重复叠加()
    {
        string path = Path.Combine(Path.GetTempPath(), "ConfigInfoComment_" + Guid.NewGuid().ToString("N") + ".cif");
        try
        {
            var document = new ConfigDocument();
            document.Root["a"] = 1;
            document.Save(path);

            ConfigDocument loaded = ConfigDocument.Load(path);
            Assert.Null(loaded.Root.Comment);
            Assert.DoesNotContain("ConfigInfo 配置文件", loaded.Root.Comment ?? string.Empty, StringComparison.Ordinal);

            loaded.Root.Comment = "用户说明";
            loaded.Save(path);

            ConfigDocument reloaded = ConfigDocument.Load(path);
            Assert.Equal("用户说明", reloaded.Root.Comment);

            reloaded.Save(path);
            string text = File.ReadAllText(path);
            Assert.Equal(1, CountOccurrences(text, "# ConfigInfo 配置文件"));
            Assert.Equal(1, CountOccurrences(text, "# 用户说明"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 自动映射生成的对象也可以附加注释()
    {
        var settings = new MappingSettings { Port = 9000, Name = "带注释" };
        ConfigObject root = ConfigFile.ToObject(settings);
        root.SetComment("port", "监听端口");
        root.SetInlineComment("port", "1-65535");
        root.GetObject("serial").SetComment("port", "串口名");

        string text = new ConfigDocument(root).ToText(includeHeader: false);
        MappingSettings loaded = ConfigFile.Parse<MappingSettings>(text);

        Assert.Equal(9000, loaded.Port);
        Assert.Equal("带注释", loaded.Name);
        Assert.Contains("# 监听端口", text, StringComparison.Ordinal);
        Assert.Contains("port = 9000  # 1-65535", text, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
