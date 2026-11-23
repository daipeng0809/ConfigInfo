namespace ConfigInfo.Demo;

/// <summary>
/// 演示：为已有配置模型手写映射（无反射、无源生成器，NativeAOT 下安全）。
/// 结构与 XinTiao 串口助手的 AppConfig 类似，方便直接套用。
/// </summary>
internal sealed class AppSettings
{
    // 应用信息
    public string AppName = "信息调试助手";
    public string AppVersion = "V1.0";
    public string ThemeVariant = "Light";
    public int TabItemIndex;

    // 串口参数
    public string ComPort = "COM1";
    public int BaudRate = 9600;
    public int DataBits = 8;
    public string Parity = "None";
    public string StopBits = "One";
    public byte[] FrameHead = [0xAA, 0x55];

    // TCP 参数
    public string TcpMode = "Client";
    public string TcpLocalIp = "0.0.0.0";
    public int TcpLocalPort;
    public string TcpRemoteIp = "127.0.0.1";
    public int TcpRemotePort = 8080;

    // 显示 / 发送参数
    public bool DisplayAsText = true;
    public bool DisplayTime = true;
    public int SendInterval = 1000;

    /// <summary>把当前设置写入配置对象（分组：app / serial / network.tcp / send）。</summary>
    public void SaveTo(ConfigObject root)
    {
        ConfigObject app = root.GetObject("app");
        app.Set("name", AppName);
        app.Set("version", AppVersion);
        app.Set("theme", ThemeVariant);
        app.Set("tabIndex", TabItemIndex);

        ConfigObject serial = root.GetObject("serial");
        serial.Set("port", ComPort);
        serial.Set("baudRate", BaudRate);
        serial.Set("dataBits", DataBits);
        serial.Set("parity", Parity);
        serial.Set("stopBits", StopBits);
        ConfigArray frameHead = root.GetArray("serial.frameHead");
        frameHead.Clear();
        foreach (byte value in FrameHead)
            frameHead.Add(value);

        ConfigObject tcp = root.GetObject("network.tcp");
        tcp.Set("mode", TcpMode);
        tcp.Set("localIp", TcpLocalIp);
        tcp.Set("localPort", TcpLocalPort);
        tcp.Set("remoteIp", TcpRemoteIp);
        tcp.Set("remotePort", TcpRemotePort);

        ConfigObject send = root.GetObject("send");
        send.Set("asText", DisplayAsText);
        send.Set("showTime", DisplayTime);
        send.Set("interval", SendInterval);
    }

    /// <summary>从配置对象读取设置；缺失的键自动使用字段默认值（宽松类型转换）。</summary>
    public static AppSettings LoadFrom(ConfigObject root)
    {
        var settings = new AppSettings
        {
            AppName = root.GetString("app.name", "信息调试助手"),
            AppVersion = root.GetString("app.version", "V1.0"),
            ThemeVariant = root.GetString("app.theme", "Light"),
            TabItemIndex = root.GetInt32("app.tabIndex"),

            ComPort = root.GetString("serial.port", "COM1"),
            BaudRate = root.GetInt32("serial.baudRate", 9600),
            DataBits = root.GetInt32("serial.dataBits", 8),
            Parity = root.GetString("serial.parity", "None"),
            StopBits = root.GetString("serial.stopBits", "One"),

            TcpMode = root.GetString("network.tcp.mode", "Client"),
            TcpLocalIp = root.GetString("network.tcp.localIp", "0.0.0.0"),
            TcpLocalPort = root.GetInt32("network.tcp.localPort", 0),
            TcpRemoteIp = root.GetString("network.tcp.remoteIp", "127.0.0.1"),
            TcpRemotePort = root.GetInt32("network.tcp.remotePort", 8080),

            DisplayAsText = root.GetBoolean("send.asText", true),
            DisplayTime = root.GetBoolean("send.showTime", true),
            SendInterval = root.GetInt32("send.interval", 1000),
        };

        if (root.TryGetPath("serial.frameHead", out ConfigValue head) && head.IsArray)
        {
            int[] values = head.AsArray().ToInt32Array();
            settings.FrameHead = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
                settings.FrameHead[i] = (byte)Math.Clamp(values[i], byte.MinValue, byte.MaxValue);
        }

        return settings;
    }
}
