using System.Diagnostics;
using System.Text;
using ConfigInfo;

namespace ConfigInfo.Demo;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("--selftest"))
            return SelfTest.Run();

        RunShowcase();
        return 0;
    }

    private static void RunShowcase()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Config", "app.cif");

        // ===== 1. 创建配置并保存：类 → 配置对象（[Description] 自动转注释） =====
        Console.WriteLine("==== 1. 创建 AppSettings 并保存为 .cif ====");
        var settings = new AppSettings();
        ConfigObject root = ConfigFile.ToObject(settings);
        root.Comment = "本文件由 ConfigInfo 生成，可直接编辑。";
        root.SetComment("app", "应用信息");
        root.SetComment("serial", "串口参数");
        root.GetObject("serial").SetInlineComment("baudRate", "波特率：9600 / 115200");
        root.GetObject("network").SetComment("tcp", "TCP 连接参数");
        new ConfigDocument(root).Save(path);
        Console.WriteLine($"已保存：{path}");
        Console.WriteLine();
        Console.WriteLine("---- 文件内容 ----");
        Console.WriteLine(File.ReadAllText(path));

        // ===== 2. 读取配置：文件 → 类，同时可读出注释 =====
        Console.WriteLine("==== 2. 读取配置 ====");
        ConfigDocument loadedDocument = ConfigDocument.Load(path);
        AppSettings loaded = ConfigFile.FromObject<AppSettings>(loadedDocument.Root);
        Console.WriteLine($"文件注释：{loadedDocument.Root.Comment}");
        Console.WriteLine($"串口注释：{loadedDocument.Root.GetComment("serial")}；波特率行尾：{loadedDocument.Root.GetObject("serial").GetInlineComment("baudRate")}");
        Console.WriteLine($"应用：{loaded.AppName} {loaded.AppVersion}，主题 {loaded.ThemeVariant}");
        Console.WriteLine($"串口：{loaded.ComPort} @ {loaded.BaudRate}，{loaded.DataBits} 数据位，校验 {loaded.Parity}，停止位 {loaded.StopBits}");
        Console.WriteLine($"TCP：{loaded.TcpMode} → {loaded.TcpRemoteIp}:{loaded.TcpRemotePort}");
        Console.WriteLine($"帧头：{string.Join(" ", loaded.FrameHead.Select(b => $"0x{b:X2}"))}");
        Console.WriteLine($"发送间隔：{loaded.SendInterval} ms，文本显示：{loaded.DisplayAsText}");
        Console.WriteLine();

        // ===== 3. 修改后再次保存（保留注释，原子写入） =====
        Console.WriteLine("==== 3. 修改并再次保存 ====");
        ConfigDocument document = ConfigDocument.Load(path);
        document.Root.SetPath("serial.port", "COM7");
        document.Root.SetPath("serial.baudRate", 115200);
        document.Root.SetPath("network.tcp.remotePort", 9000);
        document.Save(path);
        AppSettings verify = ConfigFile.Load<AppSettings>(path);
        Console.WriteLine($"重新读取：{verify.ComPort} @ {verify.BaudRate}，远端端口 {verify.TcpRemotePort}");
        Console.WriteLine($"注释仍在：{document.Root.GetComment("serial")} / {document.Root.GetObject("serial").GetInlineComment("baudRate")}");
        Console.WriteLine();

        // ===== 4. 手工编辑容错演示 =====
        Console.WriteLine("==== 4. 手工编辑容错（字符串数字 / 十六进制 / 行尾注释 / 裸值） ====");
        const string handwritten = """
            # 用户手工修改后的配置
            [serial]
            port = COM7
            baudRate = "115200"    // 字符串也能直接读成 int
            dataBits = 8           # 行尾注释
            parity = None

            [network.tcp]
            remoteIp = 192.168.1.10
            remotePort = 0x1F90    # 十六进制：8080
            """;
        ConfigDocument userDoc = ConfigDocument.Parse(handwritten);
        Console.WriteLine($"serial.baudRate（字符串）→ int：{userDoc.Root.GetInt32("serial.baudRate")}");
        Console.WriteLine($"network.tcp.remotePort（0x1F90）→ int：{userDoc.Root.GetInt32("network.tcp.remotePort")}");
        Console.WriteLine($"raw 值：{userDoc.Root.GetString("network.tcp.remoteIp")}");
        Console.WriteLine($"缺失键取默认值：localIp = {userDoc.Root.GetString("network.tcp.localIp", "0.0.0.0")}");

        // 同一份手写文本也可以直接生成类：未写的键保留字段默认值
        AppSettings tolerant = ConfigFile.Parse<AppSettings>(handwritten);
        Console.WriteLine($"自动映射：{tolerant.ComPort} @ {tolerant.BaudRate}，远端端口 {tolerant.TcpRemotePort}，主题默认值 {tolerant.ThemeVariant}");
        Console.WriteLine();

        // ===== 5. 性能 =====
        RunBenchmark();

        Console.WriteLine();
        Console.WriteLine("运行 --selftest 可执行内置断言自测。");
    }

    private static void RunBenchmark()
    {
        Console.WriteLine("==== 5. 性能（200 节 × 10 键 = 2000 个键值） ====");
        var document = new ConfigDocument();
        for (int s = 0; s < 200; s++)
        {
            ConfigObject section = document.Root.GetObject($"section{s}");
            for (int k = 0; k < 10; k++)
                section.Set($"key{k}", s * 10 + k);
        }

        string text = document.ToText();
        int textBytes = Encoding.UTF8.GetByteCount(text);

        // 预热
        ConfigDocument.Parse(text);

        const int iterations = 100;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            ConfigDocument.Parse(text);
        sw.Stop();

        double parseMs = sw.Elapsed.TotalMilliseconds / iterations;
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            document.ToText();
        sw2.Stop();
        double writeMs = sw2.Elapsed.TotalMilliseconds / iterations;

        Console.WriteLine($"文本大小：{textBytes / 1024.0:F1} KB");
        Console.WriteLine($"解析：{parseMs * 1000:F0} μs/次（约 {textBytes / 1024.0 / 1024.0 / (parseMs / 1000.0):F1} MB/s）");
        Console.WriteLine($"写出：{writeMs * 1000:F0} μs/次");
    }
}
