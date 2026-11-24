using System.ComponentModel;

namespace ConfigInfo.Demo;

/// <summary>
/// 演示：普通类直接交给 <see cref="ConfigFile"/> 自动存取，无需手写映射。
/// <para>
/// 字段名默认转成首字母小写的键（<c>ComPort</c> → <c>comPort</c>）；
/// 这里用 <see cref="ConfigNameAttribute"/> 的“点路径”把扁平类写成分组，
/// 保持 app / serial / network.tcp / send 的结构。嵌套类写法可完全不加特性。
/// 标注 <c>[Description]</c> 的字段会自动写成配置注释。
/// </para>
/// </summary>
internal sealed class AppSettings
{
    // 应用信息
    [Description("应用名称")]
    [ConfigName("app.name")] public string AppName = "信息调试助手";
    [Description("版本号")]
    [ConfigName("app.version")] public string AppVersion = "V1.0";
    [Description("界面主题")]
    [ConfigName("app.theme")] public string ThemeVariant = "Light";
    [ConfigName("app.tabIndex")] public int TabItemIndex = 0;

    // 串口参数
    [Description("串口名称，如 COM3")]
    [ConfigName("serial.port")] public string ComPort = "COM1";
    [Description("波特率")]
    [ConfigName("serial.baudRate")] public int BaudRate = 9600;
    [Description("数据位")]
    [ConfigName("serial.dataBits")] public int DataBits = 8;
    [Description("校验方式")]
    [ConfigName("serial.parity")] public string Parity = "None";
    [ConfigName("serial.stopBits")] public string StopBits = "One";
    [Description("帧头字节")]
    [ConfigName("serial.frameHead")] public byte[] FrameHead = [0xAA, 0x11];

    // TCP 参数
    [Description("连接模式")]
    [ConfigName("network.tcp.mode")] public string TcpMode = "Client";
    [ConfigName("network.tcp.localIp")] public string TcpLocalIp = "0.0.0.0";
    [ConfigName("network.tcp.localPort")] public int TcpLocalPort = 0;
    [Description("远端 IP")]
    [ConfigName("network.tcp.remoteIp")] public string TcpRemoteIp = "127.0.0.1";
    [Description("远端端口")]
    [ConfigName("network.tcp.remotePort")] public int TcpRemotePort = 8080;

    // 显示 / 发送参数
    [Description("接收区按文本显示")]
    [ConfigName("send.asText")] public bool DisplayAsText = true;
    [ConfigName("send.showTime")] public bool DisplayTime = true;
    [Description("发送间隔（毫秒）")]
    [ConfigName("send.interval")] public int SendInterval = 1000;

    // 运行时状态：不属于配置，自动映射会跳过
    [ConfigIgnore] public bool IsConnected = false;
}
