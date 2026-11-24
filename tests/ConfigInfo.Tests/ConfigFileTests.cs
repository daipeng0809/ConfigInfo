using System.ComponentModel;
using ConfigInfo;

namespace ConfigInfo.Tests;

public enum ParityKind
{
    None,
    Odd,
    Even,
}

public sealed class SerialGroup
{
    public string Port = "COM1";
    public int BaudRate = 9600;
    public FlowGroup Flow = new();
}

public sealed class FlowGroup
{
    public bool Rts = true;
    public bool Dtr;
}

public sealed class MappingSettings
{
    public string Name = "默认名称";
    public int Port = 8080;
    public bool Enabled = true;
    public double Ratio = 0.5;
    public ParityKind Parity = ParityKind.None;
    public int? RetryCount;
    public byte[] Head = [0xAA, 0x55];
    public List<string> Tags = ["a", "b"];
    public SerialGroup Serial = new();
    public List<SerialGroup> Groups = [];
    [ConfigName("network.remoteIp")] public string RemoteIp = "127.0.0.1";
    [ConfigName("network.remotePort")] public int RemotePort = 9000;
    [ConfigIgnore] public string RuntimeOnly = "不写入文件";
}

public sealed class PropertySettings
{
    public string Name { get; set; } = "属性默认";
    public int Count { get; set; } = 7;
    public SerialGroup Serial { get; set; } = new();
    public string Version { get; init; } = "1.0";
    public string ReadOnly => "只读属性被跳过";
}

public sealed class LoopNode
{
    public LoopNode? Next;
}

public struct PointSettings
{
    public int X;
    public int Y;
}

[Flags]
public enum AccessFlags
{
    None = 0,
    Read = 1,
    Write = 2,
}

public sealed class EdgeCaseSettings
{
    public int?[] NullableArray = [1, null, 3];
    public List<int?> NullableList = [null, 2];
    public List<PointSettings> Points = [];
    public List<PointSettings?> NullablePoints = [new PointSettings { X = 11 }, null];
    public PointSettings?[] NullablePointArray = [null, new PointSettings { X = 12 }];
    public PointSettings Point = new() { X = 1, Y = 2 };
    public PointSettings? MaybePoint = new() { X = 3, Y = 4 };
    public List<int>? NullableItems = [7];
    public int[][] Matrix = [[1, 2], [3]];
    public AccessFlags Flags = AccessFlags.Read | AccessFlags.Write;
}

public class BaseSettings
{
    public string BaseName = "base";
}

public sealed class DerivedSettings : BaseSettings
{
    public int Extra = 5;
}

public sealed class NodeListSettings
{
    public List<NodeListSettings> Children = [];
}

public sealed class DuplicateKeySettings
{
    public int Value = 1;
    [ConfigName("value")] public int Other = 2;
}

public sealed class PathConflictSettings
{
    public SerialGroup Serial = new();
    [ConfigName("serial.port")] public string LegacyPort = "LEGACY";
}

public sealed class InvalidPathSettings
{
    [ConfigName("a.")] public int Value = 1;
}

public sealed class ValidatedSettings
{
    private int _port;

    public int Port
    {
        get => _port;
        set => _port = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "端口不能为负。");
    }
}

public sealed class ThrowingGetterSettings
{
    private int _value;

    public int Value
    {
        get => throw new InvalidOperationException("getter 失败");
        set => _value = value;
    }
}

/// <summary>带 XML 文档的模型。</summary>
public sealed class DocumentedSettings
{
    /// <summary>串口名称。</summary>
    [ConfigName("serial.port")] public string Port = "COM1";

    /// <summary>嵌套参数。</summary>
    public DocumentedNested Nested = new();

    public DocumentedNested AutoNested = new();

    public List<DocumentedNested> Items = [];

    /// <summary>XML 文档值。</summary>
    public int Value = 1;

    /// <summary>属性说明。</summary>
    public int FromProperty { get; set; } = 5;

    /// <summary>
    /// 多行
    /// 说明。
    /// </summary>
    public int MultiLine = 3;

    /// <summary>XML 不应覆盖 Description。</summary>
    [Description("描述优先")]
    public int Priority = 2;

    public DocumentedParity Parity = DocumentedParity.None;
}

/// <summary>嵌套类型说明。</summary>
public sealed class DocumentedNested
{
    /// <summary>波特率。</summary>
    public int BaudRate = 9600;
}

/// <summary>校验方式。</summary>
public enum DocumentedParity
{
    None,
    Odd,
}

public sealed class UnsupportedMemberSettings
{
    public object Anything = new();
}

public class AutoMappingWriteTests
{
    [Fact]
    public void 自动映射生成分组与数组()
    {
        var settings = new MappingSettings
        {
            Groups = [new SerialGroup { Port = "COM2" }, new SerialGroup { Port = "COM3" }],
        };

        ConfigObject root = ConfigFile.ToObject(settings);

        Assert.Equal("默认名称", root.GetString("name"));
        Assert.Equal(8080, root.GetInt32("port"));
        Assert.True(root.GetBoolean("enabled"));
        Assert.Equal(0.5, root.GetDouble("ratio"), 12);
        Assert.Equal("None", root.GetString("parity"));
        Assert.True(root.Get("retryCount").IsNull);
        Assert.Equal(new[] { 0xAA, 0x55 }, root.GetPath("head").AsArray().ToInt32Array());
        Assert.Equal(new[] { "a", "b" }, root.GetPath("tags").AsArray().ToStringArray());
        Assert.Equal("COM1", root.GetString("serial.port"));
        Assert.Equal(9600, root.GetInt32("serial.baudRate"));
        Assert.True(root.GetBoolean("serial.flow.rts"));
        Assert.False(root.GetBoolean("serial.flow.dtr"));
        Assert.Equal("127.0.0.1", root.GetString("network.remoteIp"));
        Assert.Equal(9000, root.GetInt32("network.remotePort"));
        Assert.Equal(2, root.GetPath("groups").AsArray().Count);
        Assert.Equal("COM3", root.GetPath("groups").AsArray()[1].AsObject().GetString("port"));
        Assert.False(root.ContainsKey("runtimeOnly"));
    }

    [Fact]
    public void 属性模型与只读属性()
    {
        var settings = new PropertySettings { Count = 9 };

        ConfigObject root = ConfigFile.ToObject(settings);

        Assert.Equal("属性默认", root.GetString("name"));
        Assert.Equal(9, root.GetInt32("count"));
        Assert.Equal("COM1", root.GetString("serial.port"));
        Assert.False(root.ContainsKey("readOnly"));
    }

    [Fact]
    public void 文本包含头注释()
    {
        string withHeader = ConfigFile.ToText(new MappingSettings());
        string withoutHeader = ConfigFile.ToText(new MappingSettings(), includeHeader: false);

        Assert.StartsWith("# =====", withHeader, StringComparison.Ordinal);
        Assert.False(withoutHeader.StartsWith("# =====", StringComparison.Ordinal));
    }

    [Fact]
    public void 空值与循环引用()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigFile.ToObject<MappingSettings>(null!));

        var node = new LoopNode();
        node.Next = node;
        Assert.Throws<InvalidOperationException>(() => ConfigFile.ToObject(node));
    }

    [Fact]
    public void 不支持成员类型抛出明确异常()
    {
        var error = Assert.Throws<NotSupportedException>(() => ConfigFile.ToObject(new UnsupportedMemberSettings()));
        Assert.Contains("Object", error.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => ConfigFile.FromObject<UnsupportedMemberSettings>(new ConfigObject()));
    }
}

public class AutoMappingReadTests
{
    [Fact]
    public void 缺失键保留默认值()
    {
        MappingSettings settings = ConfigFile.Parse<MappingSettings>("port = 1234");

        Assert.Equal(1234, settings.Port);
        Assert.Equal("默认名称", settings.Name);
        Assert.Equal(9600, settings.Serial.BaudRate);
        Assert.True(settings.Serial.Flow.Rts);
        Assert.Equal(new byte[] { 0xAA, 0x55 }, settings.Head);
    }

    [Fact]
    public void 宽松转换与大小写容错()
    {
        MappingSettings settings = ConfigFile.Parse<MappingSettings>("""
            PORT = "0x1F90"
            NAME = 手写名称
            enabled = yes
            ratio = "0.75"
            SERIAL.PORT = COM4
            NETWORK.REMOTEPORT = 1234
            """);

        Assert.Equal(8080, settings.Port);
        Assert.Equal("手写名称", settings.Name);
        Assert.True(settings.Enabled);
        Assert.Equal(0.75, settings.Ratio, 12);
        Assert.Equal("COM4", settings.Serial.Port);
        Assert.Equal(1234, settings.RemotePort);
    }

    [Fact]
    public void 枚举可空与null()
    {
        MappingSettings settings = ConfigFile.Parse<MappingSettings>("""
            parity = odd
            retryCount = 3
            name = null
            """);

        Assert.Equal(ParityKind.Odd, settings.Parity);
        Assert.Equal(3, settings.RetryCount);
        Assert.Null(settings.Name);
    }

    [Fact]
    public void 嵌套对象与对象数组()
    {
        MappingSettings settings = ConfigFile.Parse<MappingSettings>("""
            serial {
                port = COM9
                flow { rts = false }
            }
            groups = [
                { port = A, baudRate = 1200 },
                { port = B },
            ]
            network.remotePort = 1234
            """);

        Assert.Equal("COM9", settings.Serial.Port);
        Assert.False(settings.Serial.Flow.Rts);
        Assert.Equal(2, settings.Groups.Count);
        Assert.Equal("A", settings.Groups[0].Port);
        Assert.Equal(1200, settings.Groups[0].BaudRate);
        Assert.Equal("B", settings.Groups[1].Port);
        Assert.Equal(1234, settings.RemotePort);
    }

    [Fact]
    public void 完整往返()
    {
        var settings = new MappingSettings
        {
            Name = "往返",
            Port = 1234,
            Enabled = false,
            Ratio = 1.25,
            Parity = ParityKind.Even,
            RetryCount = 5,
            Head = [1, 2, 3],
            Tags = ["x", "y"],
            Serial = new SerialGroup { Port = "COM5", BaudRate = 57600, Flow = new FlowGroup { Rts = false, Dtr = true } },
            Groups = [new SerialGroup { Port = "COM6" }],
        };

        string text = ConfigFile.ToText(settings, includeHeader: false);
        MappingSettings again = ConfigFile.Parse<MappingSettings>(text);

        Assert.Equal(settings.Name, again.Name);
        Assert.Equal(settings.Port, again.Port);
        Assert.Equal(settings.Enabled, again.Enabled);
        Assert.Equal(settings.Ratio, again.Ratio, 12);
        Assert.Equal(settings.Parity, again.Parity);
        Assert.Equal(settings.RetryCount, again.RetryCount);
        Assert.Equal(settings.Head, again.Head);
        Assert.Equal(settings.Tags, again.Tags);
        Assert.Equal(settings.Serial.Port, again.Serial.Port);
        Assert.Equal(settings.Serial.BaudRate, again.Serial.BaudRate);
        Assert.Equal(settings.Serial.Flow.Rts, again.Serial.Flow.Rts);
        Assert.Equal(settings.Serial.Flow.Dtr, again.Serial.Flow.Dtr);
        Assert.Equal(settings.Groups.Count, again.Groups.Count);
        Assert.Equal("COM6", again.Groups[0].Port);
        Assert.Equal(5, again.RetryCount);
    }

    [Fact]
    public void 从配置对象生成与加载到已有实例()
    {
        ConfigObject root = ConfigDocument.Parse("name = 覆盖\nport = 4321").Root;

        MappingSettings created = ConfigFile.FromObject<MappingSettings>(root);
        Assert.Equal("覆盖", created.Name);
        Assert.Equal(4321, created.Port);
        Assert.Equal(9600, created.Serial.BaudRate);

        var existing = new MappingSettings { Name = "原值", Port = 1, RuntimeOnly = "保留" };
        ConfigFile.LoadInto(root, existing);
        Assert.Equal("覆盖", existing.Name);
        Assert.Equal(4321, existing.Port);
        Assert.Equal("保留", existing.RuntimeOnly);
    }
}

public class AutoMappingEdgeTests
{
    [Fact]
    public void 可空集合元素保留null()
    {
        var settings = new EdgeCaseSettings();

        string text = ConfigFile.ToText(settings, includeHeader: false);
        EdgeCaseSettings again = ConfigFile.Parse<EdgeCaseSettings>(text);

        Assert.Equal(new int?[] { 1, null, 3 }, again.NullableArray);
        Assert.Equal(new int?[] { null, 2 }, again.NullableList);
        Assert.Equal(new[] { 7 }, again.NullableItems);
        Assert.Equal(2, again.NullablePoints.Count);
        Assert.Equal(11, again.NullablePoints[0]!.Value.X);
        Assert.Null(again.NullablePoints[1]);
        Assert.Equal(2, again.NullablePointArray.Length);
        Assert.Null(again.NullablePointArray[0]);
        Assert.Equal(12, again.NullablePointArray[1]!.Value.X);
        Assert.Equal(AccessFlags.Read | AccessFlags.Write, again.Flags);
    }

    [Fact]
    public void 手写null元素与null集合()
    {
        EdgeCaseSettings settings = ConfigFile.Parse<EdgeCaseSettings>("""
            nullableArray = [null, 5, null]
            nullableItems = null
            maybePoint = null
            """);

        Assert.Equal(new int?[] { null, 5, null }, settings.NullableArray);
        Assert.Null(settings.NullableItems);
        Assert.Null(settings.MaybePoint);
    }

    [Fact]
    public void 嵌套结构体与可空结构体往返()
    {
        var settings = new EdgeCaseSettings
        {
            Point = new PointSettings { X = 5, Y = 6 },
            MaybePoint = new PointSettings { X = 7, Y = 8 },
            Points = [new PointSettings { X = 9, Y = 10 }],
        };

        string text = ConfigFile.ToText(settings, includeHeader: false);
        EdgeCaseSettings again = ConfigFile.Parse<EdgeCaseSettings>(text);

        Assert.Equal(5, again.Point.X);
        Assert.Equal(6, again.Point.Y);
        Assert.NotNull(again.MaybePoint);
        Assert.Equal(7, again.MaybePoint!.Value.X);
        Assert.Equal(8, again.MaybePoint.Value.Y);
        Assert.Single(again.Points);
        Assert.Equal(9, again.Points[0].X);
    }

    [Fact]
    public void 部分配置修改结构体时保留其余默认值()
    {
        EdgeCaseSettings settings = ConfigFile.Parse<EdgeCaseSettings>("point { x = 9 }");

        Assert.Equal(9, settings.Point.X);
        Assert.Equal(2, settings.Point.Y);
    }

    [Fact]
    public void 交错数组往返()
    {
        var settings = new EdgeCaseSettings { Matrix = [[1, 2, 3], [4], []] };

        string text = ConfigFile.ToText(settings, includeHeader: false);
        EdgeCaseSettings again = ConfigFile.Parse<EdgeCaseSettings>(text);

        Assert.Equal(3, again.Matrix.Length);
        Assert.Equal(new[] { 1, 2, 3 }, again.Matrix[0]);
        Assert.Equal(new[] { 4 }, again.Matrix[1]);
        Assert.Empty(again.Matrix[2]);
    }

    [Fact]
    public void 继承成员参与映射()
    {
        var settings = new DerivedSettings { BaseName = "继承", Extra = 7 };

        string text = ConfigFile.ToText(settings, includeHeader: false);
        DerivedSettings again = ConfigFile.Parse<DerivedSettings>(text);

        Assert.Equal("继承", again.BaseName);
        Assert.Equal(7, again.Extra);
    }

    [Fact]
    public void 集合中的循环引用被检测()
    {
        var node = new NodeListSettings();
        node.Children.Add(node);

        Assert.Throws<InvalidOperationException>(() => ConfigFile.ToObject(node));
    }

    [Fact]
    public void 重复映射键抛出明确异常()
    {
        var error = Assert.Throws<NotSupportedException>(() => ConfigFile.ToObject(new DuplicateKeySettings()));
        Assert.Contains("value", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void init属性可读写()
    {
        var settings = new PropertySettings { Version = "2.0" };
        ConfigObject root = ConfigFile.ToObject(settings);

        Assert.Equal("2.0", root.GetString("version"));

        PropertySettings again = ConfigFile.Parse<PropertySettings>("version = 3.0");
        Assert.Equal("3.0", again.Version);
    }

    [Fact]
    public void 结构体作为根类型()
    {
        PointSettings point = ConfigFile.FromObject<PointSettings>(
            ConfigDocument.Parse("x = 3\ny = 4").Root);

        Assert.Equal(3, point.X);
        Assert.Equal(4, point.Y);

        ConfigObject root = ConfigFile.ToObject(point);
        Assert.Equal(3, root.GetInt32("x"));
        Assert.Equal(4, root.GetInt32("y"));
    }

    [Fact]
    public void 可空结构体作为根类型()
    {
        ConfigObject root = ConfigFile.ToObject<PointSettings?>(new PointSettings { X = 1, Y = 2 });
        Assert.Equal(1, root.GetInt32("x"));
        Assert.Equal(2, root.GetInt32("y"));

        PointSettings? point = ConfigFile.FromObject<PointSettings?>(
            ConfigDocument.Parse("x = 7\ny = 8").Root);
        Assert.NotNull(point);
        Assert.Equal(7, point!.Value.X);
        Assert.Equal(8, point.Value.Y);
    }

    [Fact]
    public void 路径重叠键抛出明确异常()
    {
        var error = Assert.Throws<NotSupportedException>(() => ConfigFile.ToObject(new PathConflictSettings()));
        Assert.Contains("路径重叠", error.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => ConfigFile.FromObject<PathConflictSettings>(new ConfigObject()));
    }

    [Fact]
    public void 非法点路径抛出明确异常()
    {
        var error = Assert.Throws<NotSupportedException>(() => ConfigFile.ToObject(new InvalidPathSettings()));
        Assert.Contains("不是合法的键或点路径", error.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => ConfigFile.FromObject<InvalidPathSettings>(new ConfigObject()));
    }

    [Fact]
    public void 基类变量写出派生成员()
    {
        BaseSettings settings = new DerivedSettings { BaseName = "base", Extra = 99 };

        ConfigObject root = ConfigFile.ToObject(settings);

        Assert.Equal("base", root.GetString("baseName"));
        Assert.Equal(99, root.GetInt32("extra"));
    }

    [Fact]
    public void LoadInto派生实例读取派生成员()
    {
        var target = new DerivedSettings { Extra = 5 };

        ConfigFile.LoadInto<BaseSettings>(
            ConfigDocument.Parse("baseName = x\nextra = 42").Root, target);

        Assert.Equal("x", target.BaseName);
        Assert.Equal(42, target.Extra);
    }

    [Fact]
    public void 用户setter异常被解包且TryLoad不抛()
    {
        string path = Path.Combine(Path.GetTempPath(), "ConfigInfoThrow_" + Guid.NewGuid().ToString("N") + ".cif");
        try
        {
            File.WriteAllText(path, "port = -1");

            Assert.False(ConfigFile.TryLoad<ValidatedSettings>(path, out ValidatedSettings? value, out string? error));
            Assert.Null(value);
            Assert.Contains("端口不能为负", error, StringComparison.Ordinal);

            Assert.Throws<ArgumentOutOfRangeException>(() => ConfigFile.Load<ValidatedSettings>(path));

            ValidatedSettings fallback = ConfigFile.LoadOrDefault<ValidatedSettings>(path, out string? reason);
            Assert.Equal(0, fallback.Port);
            Assert.Contains("端口不能为负", reason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 用户getter异常TrySave不抛()
    {
        string path = Path.Combine(Path.GetTempPath(), "ConfigInfoThrow_" + Guid.NewGuid().ToString("N") + ".cif");
        try
        {
            Assert.False(ConfigFile.TrySave(path, new ThrowingGetterSettings(), out string? error));
            Assert.Contains("getter 失败", error, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => ConfigFile.ToObject(new ThrowingGetterSettings()));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class AutoMappingCommentTests
{
    [Fact]
    public void Description与XML文档自动生成注释()
    {
        ConfigObject root = ConfigFile.ToObject(new DocumentedSettings());

        Assert.Equal("带 XML 文档的模型。", root.Comment);
        Assert.Equal("串口名称。", root.GetObject("serial").GetComment("port"));
        Assert.Equal("嵌套参数。", root.GetComment("nested"));
        Assert.Equal("嵌套类型说明。", root.GetComment("autoNested"));
        Assert.Equal("嵌套类型说明。", root.GetComment("items"));
        Assert.Equal("XML 文档值。", root.GetComment("value"));
        Assert.Equal("属性说明。", root.GetComment("fromProperty"));
        Assert.Equal("多行 说明。", root.GetComment("multiLine"));
        Assert.Equal("描述优先", root.GetComment("priority"));
        Assert.Equal("校验方式。", root.GetComment("parity"));
        Assert.Equal("波特率。", root.GetObject("nested").GetComment("baudRate"));
    }

    [Fact]
    public void 可关闭自动注释()
    {
        ConfigObject root = ConfigFile.ToObject(new DocumentedSettings(), includeModelComments: false);

        Assert.Null(root.Comment);
        Assert.Null(root.GetComment("value"));
        Assert.Null(root.GetObject("serial").GetComment("port"));

        string text = ConfigFile.ToText(new DocumentedSettings(), includeHeader: false, includeModelComments: false);
        Assert.DoesNotContain("# 串口名称。", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToText默认带自动注释()
    {
        string text = ConfigFile.ToText(new DocumentedSettings(), includeHeader: false);

        Assert.Contains("# 带 XML 文档的模型。", text, StringComparison.Ordinal);
        Assert.Contains("# 串口名称。", text, StringComparison.Ordinal);
    }
}

public class AutoMappingFileTests
{
    [Fact]
    public void 保存与读取文件()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ConfigInfoMap_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "sub", "app.cif");
            var settings = new MappingSettings { Port = 7777, Serial = new SerialGroup { Port = "COM8" } };

            ConfigFile.Save(path, settings);
            Assert.True(File.Exists(path));

            MappingSettings loaded = ConfigFile.Load<MappingSettings>(path);
            Assert.Equal(7777, loaded.Port);
            Assert.Equal("COM8", loaded.Serial.Port);
            Assert.Equal("默认名称", loaded.Name);
            Assert.Equal("不写入文件", loaded.RuntimeOnly);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 缺失文件时TryLoad与LoadOrDefault()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.cif");

        Assert.False(ConfigFile.TryLoad<MappingSettings>(path, out MappingSettings? value, out string? error));
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));

        MappingSettings fallback = ConfigFile.LoadOrDefault<MappingSettings>(path, out string? reason);
        Assert.Equal(8080, fallback.Port);
        Assert.Equal("默认名称", fallback.Name);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void 语法错误时TryLoad返回错误()
    {
        string path = Path.Combine(Path.GetTempPath(), "ConfigInfoBad_" + Guid.NewGuid().ToString("N") + ".cif");
        try
        {
            File.WriteAllText(path, "a = [1, 2");
            Assert.False(ConfigFile.TryLoad<MappingSettings>(path, out _, out string? error));
            Assert.Contains("行", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
