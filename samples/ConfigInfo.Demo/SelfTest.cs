using System.Text;
using ConfigInfo;

namespace ConfigInfo.Demo;

/// <summary>
/// 内置断言自测：dotnet run --project samples/ConfigInfo.Demo -- --selftest
/// 覆盖解析、类型、路径、错误定位、文本往返与文件读写。
/// </summary>
internal static class SelfTest
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        Console.WriteLine("ConfigInfo 自测开始");
        TestParsingAndTypes();
        TestLooseConversion();
        TestPathAccess();
        TestErrors();
        TestRoundtrip();
        TestFileSaveLoad();
        TestModelMapping();
        TestComments();
        Console.WriteLine($"自测结束：通过 {_passed}，失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [通过] {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  [失败] {name}");
        }
    }

    private static void TestParsingAndTypes()
    {
        Console.WriteLine("-- 解析与类型 --");
        const string text = """
            # 注释行应被忽略
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
        ConfigDocument document = ConfigDocument.Parse(text);
        ConfigObject root = document.Root;

        Check("双引号字符串", root.GetString("app.name") == "信息调试助手");
        Check("单引号保留反斜杠", root.GetString("app.version") == @"C:\Program Files\App\1.0");
        Check("整数", root.GetInt32("app.port") == 8080);
        Check("浮点", Math.Abs(root.GetDouble("app.ratio") - 0.75) < 1e-12);
        Check("布尔", root.GetBoolean("app.enabled"));
        Check("十六进制 0x1F", root.GetInt32("app.hex") == 31);
        Check("二进制 0b1010", root.GetInt32("app.bin") == 10);
        Check("null", root.TryGetPath("app.nothing", out ConfigValue nothing) && nothing.IsNull);
        Check("URL 中的 // 与 # 不当作注释",
            root.GetString("app.url") == "http://127.0.0.1:8080/api?x=1#frag");

        ConfigArray tags = root.GetPath("app.tags").AsArray();
        Check("数组元素个数", tags.Count == 3);
        Check("数组裸值", tags.GetString(0) == "com");
        Check("数组字符串", tags.GetString(1) == "tcp");
        Check("数组数字", tags.GetInt32(2) == 3);
        Check("嵌套数组", root.GetPath("app.matrix").AsArray()[1].AsArray().GetInt32(0) == 3);
        Check("内联对象", root.GetString("app.meta.author") == "daipeng");

        Check("表头 + 裸值", root.GetString("serial.com") == "COM3");
        Check("字符串数字可直接读成 int", root.GetInt32("serial.baud") == 115200);
        Check("值本身仍是字符串", root.GetPath("serial.baud").IsString);
        Check("数组十六进制", root.GetPath("serial.frame").AsArray().ToInt32Array() is [0xAA, 0x55]);
        Check("内联对象浮点", Math.Abs(root.GetDouble("serial.stop.bits") - 1.5) < 1e-12);
        Check("内联对象字符串", root.GetString("serial.stop.name") == "One");
        Check("表头 + 块对象", root.GetString("network.tcp.mode") == "Client");
        Check("块对象嵌套", root.GetInt32("network.tcp.remote.port") == 8080);
        Check("顶层分组数量", root.Count == 3);
    }

    private static void TestLooseConversion()
    {
        Console.WriteLine("-- 宽松类型转换 --");
        var node = new ConfigObject
        {
            ["s"] = "115200",
            ["b"] = "yes",
            ["n"] = 5,
            ["flag"] = true,
            ["f"] = 2.5,
        };

        Check("字符串 → int", node.GetInt32("s") == 115200);
        Check("yes → bool", node.GetBoolean("b"));
        Check("int → string", node.GetString("n") == "5");
        Check("bool → int", node.GetInt64("flag") == 1);
        Check("double → int", node.GetInt32("f") == 2);
        Check("缺失键取默认值", node.GetInt32("missing", -1) == -1);
        Check("类型不可转换取默认值", node.GetInt32("b", -7) == -7);
    }

    private static void TestPathAccess()
    {
        Console.WriteLine("-- 路径访问 --");
        var document = new ConfigDocument();
        document.Root.SetPath("a.b.c", 42);
        Check("SetPath 自动创建中间层", document.Root.GetInt32("a.b.c") == 42);

        ConfigArray list = document.Root.GetArray("a.b.list");
        list.Add(1);
        list.Add("two");
        Check("GetArray 自动创建", document.Root.GetPath("a.b.list").AsArray().GetString(1) == "two");
        Check("TryGetPath 不存在返回 false", !document.Root.TryGetPath("a.x.y", out _));

        bool threw = false;
        try
        {
            document.Root.GetObject("a.b.c.d");
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Check("结构冲突不静默覆盖", threw);
    }

    private static void TestErrors()
    {
        Console.WriteLine("-- 错误定位 --");
        CheckParseError("数组缺少 ']'", "a = [1, 2", 1);
        CheckParseError("缺少值", "a = 1\nb = ", 2);
        CheckParseError("缺少值时不吞下一行", "a =\n# 注释\nb = 2", 1);
        CheckParseError("键后缺少 '='", "a = 1\n  b", 2);
        CheckParseError("字符串缺少结束引号", "a = \"abc", 1);
        CheckParseError("字符串内直接换行", "a = \"ab\ncd\"", 1);
        CheckParseError("嵌套过深", "a = " + new string('[', 300) + new string(']', 300), 1);

        var duplicate = ConfigDocument.Parse("a = 1\na = 2");
        Check("重复键后者覆盖前者", duplicate.Root.GetInt32("a") == 2 && duplicate.Root.Count == 1);

        var comments = ConfigDocument.Parse("a = 1 // 行尾注释\n# 整行注释\nb = 2 # 注释");
        Check("三种注释均被忽略", comments.Root.Count == 2 && comments.Root.GetInt32("b") == 2);
    }

    private static void CheckParseError(string name, string text, int expectedLine)
    {
        try
        {
            ConfigDocument.Parse(text);
            Check(name, false);
        }
        catch (ConfigFormatException ex)
        {
            Check($"{name}（第 {ex.Line} 行）", ex.Line == expectedLine);
        }
    }

    private static void TestRoundtrip()
    {
        Console.WriteLine("-- 文本往返 --");
        var document = new ConfigDocument();
        document.Root["str"] = "含\"引号\\反斜杠\n换行\t制表符 中文";
        document.Root["big"] = 9007199254740993L;
        document.Root["float"] = 3.0;
        document.Root["flag"] = true;
        document.Root["nothing"] = ConfigValue.Null;
        document.Root["nan"] = double.NaN;
        document.Root["literal.dot"] = 7;

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
        Check("文本往返幂等", text1 == text2);

        ConfigObject again = ConfigDocument.Parse(text1).Root;
        Check("字符串转义往返", again.GetString("str") == "含\"引号\\反斜杠\n换行\t制表符 中文");
        Check("大整数往返", again.GetInt64("big") == 9007199254740993L);
        Check("浮点类型保持", again.Get("float").IsFloat && again.GetDouble("float") == 3.0);
        Check("null 往返", again.Get("nothing").IsNull);
        Check("NaN 往返", again.Get("nan").IsFloat && double.IsNaN(again.GetDouble("nan")));
        Check("点号键保持字面", again.GetInt32("literal.dot") == 7 && !again.ContainsKey("literal"));
        Check("数组往返", again.GetPath("serial.frame").AsArray().ToInt32Array() is [0xAA, 0xFF]);
        Check("嵌套对象往返", again.GetString("serial.option.name") == "值 中");
        Check("混合数组往返", again.GetPath("serial.option.items").AsArray().GetInt32(1) == 2);
    }

    private static void TestFileSaveLoad()
    {
        Console.WriteLine("-- 文件读写 --");
        string dir = Path.Combine(Path.GetTempPath(), "ConfigInfoSelfTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "sub", "test.cif");
            var document = new ConfigDocument();
            document.Root.SetPath("app.name", "测试 中文");
            document.Save(path);
            Check("自动创建目录并写入", File.Exists(path));

            ConfigDocument loaded = ConfigDocument.Load(path);
            Check("加载值一致", loaded.Root.GetString("app.name") == "测试 中文");

            string content = File.ReadAllText(path);
            Check("默认包含头注释", content.StartsWith("# =====", StringComparison.Ordinal));
            Check("UTF-8 无 BOM", !content.StartsWith('\uFEFF'));

            string utf16Path = Path.Combine(dir, "utf16.cif");
            File.WriteAllText(utf16Path, "name = 兼容UTF16", Encoding.Unicode);
            Check("UTF-16 BOM 自动识别", ConfigDocument.Load(utf16Path).Root.GetString("name") == "兼容UTF16");

            Check("CRLF 文本", ConfigDocument.Parse("a = 1\r\n[b]\r\nc = 2").Root.GetInt32("b.c") == 2);
            Check("TryLoad 失败返回错误",
                !ConfigDocument.TryLoad(Path.Combine(dir, "missing.cif"), out _, out string? error)
                && !string.IsNullOrEmpty(error));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static void TestModelMapping()
    {
        Console.WriteLine("-- 类自动映射 --");
        var settings = new AppSettings { ComPort = "COM7", BaudRate = 115200, FrameHead = [0xAA, 0x55] };

        ConfigObject root = ConfigFile.ToObject(settings);
        Check("类直接生成分组", root.GetString("serial.port") == "COM7");
        Check("自动映射数字", root.GetInt32("serial.baudRate") == 115200);
        Check("自动映射数组", root.GetPath("serial.frameHead").AsArray().ToInt32Array() is [0xAA, 0x55]);
        Check("忽略成员不写出", !root.ContainsKey("isConnected"));

        string text = ConfigFile.ToText(settings, includeHeader: false);
        AppSettings again = ConfigFile.Parse<AppSettings>(text);
        Check("自动映射文本往返", again.ComPort == "COM7" && again.BaudRate == 115200 && again.FrameHead is [0xAA, 0x55]);
        Check("未写键保留默认值", again.SendInterval == 1000 && again.ThemeVariant == "Light");

        AppSettings partial = ConfigFile.Parse<AppSettings>("[serial]\nport = COM9\nbaudRate = \"57600\"");
        Check("手写字符串数字直接读成 int", partial.BaudRate == 57600);
        Check("部分配置其余字段保持默认", partial.ComPort == "COM9" && partial.SendInterval == 1000);

        string dir = Path.Combine(Path.GetTempPath(), "ConfigInfoModel_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "app.cif");
            ConfigFile.Save(path, settings);
            AppSettings loaded = ConfigFile.Load<AppSettings>(path);
            Check("读取文件直接生成类", loaded.ComPort == "COM7" && loaded.TcpRemotePort == 8080);

            AppSettings fallback = ConfigFile.LoadOrDefault<AppSettings>(
                Path.Combine(dir, "missing.cif"), out string? error);
            Check("LoadOrDefault 缺失文件返回默认值", fallback.BaudRate == 9600 && error is not null);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static void TestComments()
    {
        Console.WriteLine("-- 注释读写 --");
        var document = new ConfigDocument();
        document.Root.Comment = "文件说明";
        document.Root["port"] = 8080;
        document.Root.SetComment("port", "监听端口");
        document.Root.SetInlineComment("port", "1-65535");

        string text = document.ToText(includeHeader: false);
        Check("写出文件级注释", text.Contains("# 文件说明", StringComparison.Ordinal));
        Check("写出条目与行尾注释",
            text.Contains("# 监听端口", StringComparison.Ordinal)
            && text.Contains("# 1-65535", StringComparison.Ordinal));

        ConfigObject again = ConfigDocument.Parse(text).Root;
        Check("读取文件级条目与行尾注释",
            again.Comment == "文件说明"
            && again.GetComment("port") == "监听端口"
            && again.GetInlineComment("port") == "1-65535");

        string dir = Path.Combine(Path.GetTempPath(), "ConfigInfoComment_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "app.cif");
            document.Save(path);

            ConfigDocument loaded = ConfigDocument.Load(path);
            Check("标准头不会被当作普通注释", loaded.Root.Comment == "文件说明");

            loaded.Save(path);
            string content = File.ReadAllText(path);
            Check("标准头只保留一份",
                content.Split("# ConfigInfo 配置文件").Length - 1 == 1);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
