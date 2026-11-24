# ConfigInfo

**面向 NativeAOT 的 .cif 文本配置库（核心零反射，另带一行式自动映射）**  
A `.cif` text configuration library with a zero-reflection core and an optional one-line object mapper.

[中文说明](#中文说明) · [English Manual](#english-manual)

---

## 目录

- [中文说明](#中文说明)
  - [1. 项目简介](#1-项目简介)
  - [2. 特性总览](#2-特性总览)
  - [3. 快速开始](#3-快速开始)
  - [4. .cif 格式规范](#4-cif-格式规范)
  - [5. API 参考](#5-api-参考)
  - [6. 类型转换规则](#6-类型转换规则)
  - [7. 文件读写与原子保存](#7-文件读写与原子保存)
  - [8. 错误处理](#8-错误处理)
  - [9. 性能与内存](#9-性能与内存)
  - [10. AOT 与裁剪](#10-aot-与裁剪)
  - [11. 线程安全](#11-线程安全)
  - [12. 已知限制](#12-已知限制)
  - [13. 构建、测试与示例](#13-构建测试与示例)
  - [14. 审计与修复记录](#14-审计与修复记录)
  - [15. 版本与兼容性](#15-版本与兼容性)
- [English Manual](#english-manual)

---

# 中文说明

## 1. 项目简介

`ConfigInfo` 是一个纯 C# 的轻量文本配置库，使用自定义的 `.cif`（ConfigInfo Format）文本格式，
面向**手工编辑友好**、**核心零反射**、**NativeAOT / 裁剪安全**的场景，并提供可选的反射自动映射便捷层。

它适合桌面工具、串口/网络调试助手、嵌入式上位机等“配置量不大、但需要用户能直接改文件”的程序。

设计目标：

1. **可读**：配置就是普通文本，`key = value` 一眼看懂。
2. **容错**：`"115200"` 这样的字符串可以直接读成 `int`，手写失误不至于让程序读不到值。
3. **稳定**：写出顺序固定，读出→写出→再读出结果幂等；语法错误精确到行号/列号。
4. **安全**：不依赖反射、动态代码与序列化特性，NativeAOT 与 IL 裁剪下零警告。
5. **可控**：深层嵌套有明确上限，避免恶意/误写文本把进程打崩。

库的目标框架为 `net8.0`，整个库仅一个程序集，无任何第三方依赖。

## 2. 特性总览

| 能力 | 说明 |
| --- | --- |
| 数据类型 | `null`、布尔、64 位整数、双精度浮点、字符串、数组、有序对象 |
| 文本格式 | `.cif`：`key = value`、`[section.path]`、`key { ... }`、`[1, 2, 3]`、`{ a = 1 }` |
| 注释 | `#` 与 `//`（整行或行尾）；可读写，`Load → Save` 保留条目注释 |
| 字符串 | 双引号（含 `\n`、`\uXXXX` 等转义）与单引号（字面量，适合 Windows 路径） |
| 数字 | 十进制、`0x` / `0b` / `0o`、`_` 数字分隔符、`NaN` / `±Infinity` |
| 读取方式 | 字面键、点路径（`serial.tcp.port`）、强类型 `As*`、宽松 `Get*`（带默认值） |
| 对象映射 | `ConfigFile.Save(path, 类)` / `ConfigFile.Load<T>(path)` 自动映射；`[ConfigName]` / `[ConfigIgnore]` 微调 |
| 自动注释 | `[Description]` / XML `<summary>` 自动写成条目注释，`includeModelComments` 可关闭 |
| 写出 | 保持插入顺序；数组短则单行、长则自动多行；字符串自动加引号与转义 |
| 文件 | UTF-8 无 BOM 保存；自动识别 UTF-8 / UTF-16 BOM；原子替换保存 |
| 诊断 | `ConfigFormatException` 带 `Line` / `Column`（从 1 开始） |
| AOT | 核心解析/写出零反射；`ConfigFile` 自动映射为反射便捷层（标注 RUC/RDC，AOT 用手写映射） |

## 3. 快速开始

### 3.1 环境要求

- .NET SDK 8.0 或更高（库目标框架 `net8.0`）
- 运行需要 .NET 8+ 运行时。若机器只安装了更高版本运行时，可临时启用前滚：

```powershell
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
```

### 3.2 项目引用

```xml
<ItemGroup>
  <ProjectReference Include="..\src\ConfigInfo\ConfigInfo.csproj" />
</ItemGroup>
```

或引用编译产物 `ConfigInfo.dll`。

### 3.3 最小示例

```csharp
using ConfigInfo;

// 1) 从文本解析
ConfigDocument document = ConfigDocument.Parse("""
    app.name = "信息调试助手"
    app.version = 'C:\Program Files\App\1.0'
    app.port = 8080

    [serial]
    port = COM3
    baudRate = "115200"   # 字符串也能直接读成 int
    frame = [0xAA, 0x55]
    """);

ConfigObject root = document.Root;

// 2) 宽松读取（缺失/不可转换时返回默认值）
string name    = root.GetString("app.name", "未命名");
int    port    = root.GetInt32("app.port");
int    baud    = root.GetInt32("serial.baudRate");      // 115200
int[]  frame   = root.GetPath("serial.frame").AsArray().ToInt32Array();

// 3) 修改并保存（默认原子替换，写入中断不会留下半截文件）
root.SetPath("serial.port", "COM7");                     // 点路径写入已有嵌套键
root.SetPath("network.tcp.remotePort", 9000);            // 中间层自动创建
document.Save("config/app.cif");
```

### 3.4 强类型映射：类直接交给库（推荐）

不需要基类、接口、源生成器或任何配置：把普通类交给 `ConfigFile` 就能保存，读取文件时直接生成类。

```csharp
internal sealed class AppSettings
{
    public string ComPort = "COM1";                         // → comPort
    public int BaudRate = 9600;                             // → baudRate
    public byte[] FrameHead = [0xAA, 0x55];                 // → frameHead = [170, 85]
    public SerialSettings Serial = new();                   // → serial { ... }

    [ConfigName("network.remoteIp")] public string RemoteIp = "127.0.0.1";
    [ConfigIgnore] public bool IsConnected;                 // 不写出、不读取
}

internal sealed class SerialSettings
{
    public int DataBits = 8;                                // → serial.dataBits
    public string Parity = "None";                          // → serial.parity
}

var settings = new AppSettings { ComPort = "COM7" };
ConfigFile.Save("config/app.cif", settings);                // 类 → 文件（默认原子替换）

AppSettings loaded = ConfigFile.Load<AppSettings>("config/app.cif");   // 文件 → 类

// 文件缺失/损坏时不抛异常，返回带字段默认值的实例：
AppSettings safe = ConfigFile.LoadOrDefault("config/app.cif", out string? error);
```

映射规则：

- 映射公共实例字段与公共可读写属性；键名默认是成员名首字母小写（`ComPort` → `comPort`）。
- 嵌套类成员 → 子对象；`T[]` / `List<T>` → 数组；元素可以是标量或嵌套对象。
- 支持 `string`、`bool`、全部整数、`float`、`double`、`decimal`、枚举（写名称）、可空值类型、数组、`List<T>`、嵌套类。
- 读取时缺失的键保留 `new T()` 的默认值；键名先精确匹配、再忽略大小写匹配；未知键忽略。
- 沿用宽松转换：`"115200"`、`0x1F90` 都能读成整数；收窄溢出按目标类型范围截断。
- `[ConfigName("serial.port")]` 可改名或用点路径，把扁平类直接写成分组；`[ConfigIgnore]` 排除成员。
- 点路径必须合法（不能以 `.` 开头/结尾、不能有 `..`），且不能与同一类型的其他成员键互为前缀
  （如同时存在 `serial` 与 `serial.port` 会直接报错，避免写出时互相覆盖）。
- 根对象写出使用**运行时类型**：以基类/接口变量调用 `ToObject`/`Save` 不会丢失根级派生成员；`LoadInto` 也按目标实例的运行时类型读取。嵌套成员与集合元素按声明类型写出/读取，需要保留其派生字段时请把派生类型作为根对象或扁平化。读取使用 `Load<T>` / `FromObject<T>` 指定的 `T`，需要读派生成员时请指定派生类型。
- 模型存在循环引用、成员类型不支持（如 `object`）、嵌套超过 256 层时抛出明确异常。
- 自动注释：默认读取成员的 `[Description]` 或 XML 文档 `<summary>` 写成条目注释，类自身的说明写入 `Root.Comment`；XML summary 需要模型程序集生成并部署同名 `.xml` 文件。可用 `includeModelComments: false` 关闭。
- `ConfigFile.Save` 生成全新的根对象，不继承旧文件注释；需要保留旧注释时用 `ConfigDocument.Load` 修改保存，或对 `ToObject` 的结果再 `SetComment`。

> 自动映射基于反射：`ConfigFile` 已标注 `RequiresUnreferencedCode` / `RequiresDynamicCode`。
> NativeAOT / 裁剪项目请使用下面的零反射手写映射，调用 `ConfigFile` 会得到 IL2026 / IL3050 提示。

```csharp
// 零反射手写映射（AOT 安全）：底层 API 保持不变
internal sealed class AppSettings
{
    public string ComPort = "COM1";
    public int BaudRate = 9600;

    public void SaveTo(ConfigObject root)
    {
        ConfigObject serial = root.GetObject("serial");
        serial.Set("port", ComPort);
        serial.Set("baudRate", BaudRate);
    }

    public static AppSettings LoadFrom(ConfigObject root) => new()
    {
        ComPort = root.GetString("serial.port", "COM1"),
        BaudRate = root.GetInt32("serial.baudRate", 9600),
    };
}

// 与文件读写组合，同样只需两行：
// settings.SaveTo(document.Root); document.Save(path);
// AppSettings loaded = AppSettings.LoadFrom(ConfigDocument.Load(path).Root);
```

## 4. .cif 格式规范

### 4.1 文件与编码

- 推荐 UTF-8（无 BOM）；`ConfigDocument.Save` 固定输出 UTF-8 无 BOM。
- `ConfigDocument.Load` 使用 `File.ReadAllText` 的 BOM 探测：
  UTF-8（含 BOM）、UTF-16 LE / BE（含 BOM）均可读取；**无 BOM 时按 UTF-8 解码**，不自动识别无 BOM 的 UTF-16。
- `ConfigDocument.Parse(string)` 允许文本以 `\uFEFF`（BOM）开头，会自动跳过。
- 换行支持 `LF`、`CRLF`、`CR`；写出统一使用 `LF`。

### 4.2 注释

```cif
# 文件级说明（与后面的条目之间留一个空行）

# 串口参数
serial {
    # 波特率
    baudRate = 115200   # 单位 bps
}
```

规则：

- `#` 与 `//` 在**行首**或**前面是空白**时才被当作注释；`http://host/path#frag` 这类裸值不会被截断。
- 引号字符串中的 `#`、`//` 一律是普通字符。
- 注释参与文本往返：`Load → Save` 会保留条目上方注释与行尾注释（统一用 `#` 写出）。
  - 文件开头、与第一个条目之间隔有空行的注释块 → 根对象 `ConfigObject.Comment`；
  - 其余注释块归属紧随其后的条目；紧贴 `}` 的块归 `ConfigObject.TrailingComment`。
- 使用方式：

```csharp
var document = new ConfigDocument();
document.Root.Comment = "应用配置";
ConfigObject serial = document.Root.GetObject("serial");
serial.Set("port", "COM3");
document.Root.SetComment("serial", "串口参数");        // 条目上方
serial.SetComment("port", "串口名");
serial.SetInlineComment("port", "如 COM3");            // 行尾
serial.TrailingComment = "串口参数结束";
document.Save("app.cif");

// 读取注释
ConfigDocument loaded = ConfigDocument.Load("app.cif");
string? file = loaded.Root.Comment;
string? group = loaded.Root.GetComment("serial");
string? port = loaded.Root.GetObject("serial").GetComment("port");
string? inline = loaded.Root.GetObject("serial").GetInlineComment("port");
```

- `SetComment` / `SetInlineComment` 按字面键定位，键必须已存在；嵌套对象请先 `GetObject`。
- 库生成的**标准文件头**会被解析器识别并跳过，反复 `Load → Save` 不会叠加。
- 不保留的部分：注释符号（`//` → `#`）、注释缩进与空行、数组元素内部的注释。

#### 从类说明自动生成注释

`ConfigFile.ToObject` / `Save` / `ToText` 默认（`includeModelComments: true`）把模型说明写成注释：

```csharp
using System.ComponentModel;

/// <summary>应用配置类说明</summary>
public sealed class AppSettings
{
    [Description("串口名称，如 COM3")]
    public string Port = "COM1";

    /// <summary>波特率</summary>
    public int BaudRate = 9600;

    public SerialSettings Serial = new();
}

/// <summary>嵌套串口参数</summary>
public sealed class SerialSettings
{
    /// <summary>数据位</summary>
    public int DataBits = 8;
}

ConfigFile.Save("app.cif", settings);                               // 自动写注释
ConfigFile.Save("app.cif", settings, includeModelComments: false);  // 关闭
```

生成效果：

```cif
# 应用配置类说明（来自类型自身的 <summary> 或 [Description]）

# 串口名称，如 COM3
port = "COM1"
# 波特率
baudRate = 9600
# 嵌套串口参数（成员无说明时回退到 SerialSettings 类型说明）
serial {
    # 数据位
    dataBits = 8
}
```

规则：

- 优先级：成员 `[Description]` > 成员 XML `<summary>` > 成员类型（嵌套类 / 枚举 / 集合元素类型）的 `[Description]` / `<summary>`。
- 类型自身的说明写入 `Root.Comment`（文件头）。
- 嵌套类成员没有自己的说明时，使用该类的类型说明；枚举成员同理。
- XML `<summary>` 需要模型程序集设置 `<GenerateDocumentationFile>true</GenerateDocumentationFile>`，并把生成的 `.xml` 与程序集放在同一目录（单文件发布时回退到应用目录）；文件缺失时静默跳过。
- 自动注释只填空位；之后 `SetComment` / `SetInlineComment` 可覆盖。
- 数组/`List<T>` 内部的元素对象不支持逐条注释。

### 4.3 键值

```cif
name = "app"
port = 8080
debug = true
```

- `key = value` 必须写在同一行；值必须与 `=` 同行开始（不允许 `key =` 后换行再写值）。
- 键名区分大小写。
- 重复键：后出现的值覆盖先出现的值，**并保留第一次出现的位置**。
- 键名可以包含空格与中文；包含特殊字符（空格、`.`、`:`、`#`、`//`、`=`、括号、逗号、引号、反斜杠、控制字符）时写出器会自动加双引号。
- 键名含 `:` 时必须加引号；解析器把 `a: 1` 识别为“用错了分隔符”并给出明确错误。
- 顶层条目之间可以写逗号（兼容 JSON 风格），也可以不写。

### 4.4 字符串

```cif
path   = 'C:\Program Files\App\1.0'   # 单引号：反斜杠不转义
title  = "信息调试助手"                # 双引号：支持转义
quote  = "他说：\"你好\""
multi  = "第一行\n第二行"
```

| 写法 | 行为 |
| --- | --- |
| `'...'` | 字面量，反斜杠不转义；`''` 表示一个单引号；适合 Windows 路径 |
| `"..."` | 支持转义：`\"` `\\` `\/` `\b` `\f` `\n` `\r` `\t` `\0` `\uXXXX` |
| 裸值 | 不加引号的文本，见 [4.11](#411-语法速查表) 的终止规则 |

- 引号字符串中**不允许直接换行**（`ConfigFormatException`），需要换行请用 `\n`。
- 未识别的转义（如 `\q`）会报错，不会静默丢弃。
- 增补平面字符（emoji 等代理对）可正常往返；孤立代理项会写成 `\uXXXX`。

### 4.5 数字

```cif
count   = -42
ratio   = 0.75
big     = 1e9
mask    = 0x1F
flags   = 0b1010_1010
perm    = 0o755
special = NaN
limit   = Infinity
```

| 形式 | 说明 |
| --- | --- |
| 十进制整数 | 64 位有符号；超出范围的字面量**保留为字符串**（不静默截断） |
| `0x` / `0b` / `0o` | 按 64 位无符号位模式解释；最高位为 1 时读回负数，请用 `AsUInt64()` 取无符号值 |
| `_` 分隔符 | 只能位于两个数字之间：`1_000` 合法，`1_` / `1__0` / `0x_1F` 不合法（按字符串保留） |
| 浮点 | 含 `.` 或 `e`/`E` 的十进制数；按 IEEE 754 `double` 处理；`1e400` → `Infinity` |
| 特殊值 | `NaN`、`Infinity`、`+Infinity`、`-Infinity`（大小写不敏感） |

说明：

- 浮点写出使用最短往返格式，并保证整数值带 `.0`（如 `3.0`），从而读到后仍是浮点而不是整数。
- `-0.0` 会原样保留符号位。
- 十六进制超过 64 位（例如 `0x1_0000_0000_0000_0000`）会被当作字符串，而不是回绕。

### 4.6 布尔与 null

```cif
debug   = true
enabled = false
nothing = null
```

- `true` / `false` / `null` 大小写不敏感。
- 文本中只有这四个字面量会推断为布尔/空值；`yes`、`on`、`1` 在文本中仍是字符串，但通过
  `GetBoolean` 读取时会按宽松规则转换为 `true`。

### 4.7 数组

```cif
empty  = []
frame  = [0xAA, 0x55]
tags   = [com, "tcp", 3]
matrix = [[1, 2], [3, 4]]
items  = [
    { name = A, value = 1 },
    { name = B, value = 2 },   # 尾随逗号允许
]
```

- 元素可以是任意值：标量、数组、对象。
- 元素之间必须有 `,`；最后一个元素后可以有尾随逗号。
- 方括号内允许换行与注释。
- 写出规则：能在一行 100 个字符内写完就单行输出，否则自动改多行（每行一个元素 + 尾随逗号）。

### 4.8 分组：表头与块对象

```cif
[serial]
port = COM3

[network.tcp]
mode = Client

device {
    name = sensor-01
    option {
        rate = 10
    }
}
```

- `[a.b.c]` 是**顶层**表头，等价于逐级创建对象 `a → b → c`；表头内不能换行。
- 表头**不支持引号**：`["a.b"]` 会得到明确错误（否则会生成 `"a` / `b"` 这样的怪异键名）。
  含 `.` 的字面键请在键值行中使用引号写法：`"a.b" = 1`。
- `key { ... }` 是块对象，可以出现在顶层或任意块内部。
- `key = { a = 1, b = 2 }` 是内联对象（单行写法）。
- 空对象写成 `key = {}`。
- 同一个对象可以被多个表头/块重复打开，内容会合并。
- `[section]` **不能**写在 `{ ... }` 块内部（会得到明确错误），块内请用 `key { ... }`。
- 写出器统一把对象写成 `key { ... }` 块，不输出 `[section]` 表头；`ToText → Parse → ToText` 幂等。

### 4.9 点号键与路径

裸键中的 `.` 会被展开为嵌套对象（与表头一致）：

```cif
serial.port = COM3        # 等价于 [serial] / port = COM3
```

如果键名本身就想包含 `.`，必须加引号：

```cif
"a.b" = 1                 # 字面键名 "a.b"，不会创建嵌套对象
```

访问方式分两类：

| 类别 | API | 说明 |
| --- | --- | --- |
| 字面键 | `Get` / `TryGetValue` / `Set` / `Add` / `ContainsKey` / `Remove` / 索引器 | 键名完全按字面匹配，不拆解 `.` |
| 点路径 | `GetPath` / `TryGetPath` / `SetPath` / `GetObject` / `GetArray` | 按 `.` 逐级导航；`SetPath`/`GetObject`/`GetArray` 会自动创建中间对象 |
| 宽松读取 | `GetString` / `GetInt32` / … | **先按字面键查找**，找不到且含 `.` 时才回退到点路径 |

注意：

- 路径中间或末端的键已存在且不是对象/数组（也不是 `null`）时，`GetObject`/`GetArray`/`SetPath` 抛出
  `InvalidOperationException`，不会静默覆盖已有数据。值为 `null` 时会被替换为对象/数组。
- 解析与写出的嵌套深度上限为 **256 层**（含数组/对象/点路径/表头产生的层级）。超过会抛出
  `ConfigFormatException`（解析）或 `InvalidOperationException`（写出），而不是栈溢出崩溃。
- 键名含 `.` 的字段无法用路径 API 访问，请使用字面键 API。

### 4.10 完整示例

```cif
# ============================================================
# 信息调试助手配置
# ============================================================
app {
    name = "信息调试助手"
    version = "V1.0"
    theme = "Light"
}

serial {
    port = "COM7"
    baudRate = 115200
    dataBits = 8
    parity = "None"
    frameHead = [170, 85]
    options = { rts = true, dtr = false }   # 内联对象
}

network {
    tcp {
        mode = "Client"
        remoteIp = 127.0.0.1
        remotePort = 0x1F90    # 8080
    }
}

send {
    asText = true
    interval = 1000
}

"literal.with.dot" = 1     # 字面键名
```

写出结果（`includeHeader: false`）：

```cif
app {
    name = "信息调试助手"
    version = "V1.0"
    theme = "Light"
}
serial {
    port = "COM7"
    baudRate = 115200
    dataBits = 8
    parity = "None"
    frameHead = [170, 85]
    options {
        rts = true
        dtr = false
    }
}
network {
    tcp {
        mode = "Client"
        remoteIp = "127.0.0.1"
        remotePort = 8080
    }
}
send {
    asText = true
    interval = 1000
}
"literal.with.dot" = 1
```

> 内联对象（`key = { ... }` 输入写法）在写出时统一展开为块对象；只有空对象保留为 `key = {}`。
> 数组内部的元素对象仍写成内联形式。

### 4.11 语法速查表

| 语法 | 示例 | 备注 |
| --- | --- | --- |
| 键值 | `a = 1` | 值必须与 `=` 同行开始 |
| 块对象 | `a { b = 1 }` | 可换行、可嵌套 |
| 内联对象 | `a = { b = 1 }` | 作为值使用 |
| 表头 | `[a.b]` | 仅限顶层 |
| 数组 | `[1, "x", { a = 1 }]` | 逗号分隔，允许尾随逗号 |
| 注释 | `# ...`、`// ...` | 行首或空白后 |
| 双引号字符串 | `"a\nb"` | 支持转义 |
| 单引号字符串 | `'C:\dir'` | 字面量，`''` 表示 `'` |
| 空值 | `null` | 大小写不敏感 |
| 裸值终止符 | 换行、`,`、`]`、`}` | 不能以 `#` 或 `//` 开头 |
| 路径分隔 | `a.b.c` | 引号键不展开 |
| 键值分隔 | `=` | `:` 会报错并提示 |

## 5. API 参考

命名空间：`ConfigInfo`。全部类型均为 `public`，除特别说明外无反射依赖。

### 5.1 ConfigDocument

一个 `.cif` 文档，持有根对象并负责解析、生成文本与文件读写。

| 成员 | 说明 |
| --- | --- |
| `ConfigObject Root { get; }` | 根对象，始终非 `null` |
| `ConfigDocument()` | 创建空文档（`Root` 为空对象） |
| `ConfigDocument(ConfigObject root)` | 直接引用已有根对象（不复制） |
| `static ConfigDocument Parse(string text)` | 解析文本，失败抛 `ConfigFormatException` |
| `static ConfigDocument Parse(ReadOnlySpan<char> text)` | Span 重载，避免额外字符串分配 |
| `static bool TryParse(string? text, out ConfigDocument?, out ConfigFormatException?)` | 不抛异常的解析，保留行号/列号 |
| `static ConfigDocument Load(string path)` | 读文件并解析，自动识别 BOM |
| `static bool TryLoad(string path, out ConfigDocument?, out string? error)` | 不抛异常的加载 |
| `string ToText(bool includeHeader = true)` | 生成文本；默认带头部说明注释 |
| `string ToString()` | 等价于 `ToText()` |
| `void Save(string path, bool atomic = true, bool includeHeader = true)` | 保存为 UTF-8 无 BOM |
| `bool TrySave(string path, out string? error, bool atomic = true, bool includeHeader = true)` | 不抛异常的保存 |

`Load` / `Parse` 会读取注释：根对象用 `Root.Comment`，条目用 `GetComment` / `GetInlineComment`；保存时自动写出，详见 [4.2](#42-注释)。

`Save` 行为：

1. 自动创建目标目录。
2. `atomic: true`（默认）：先写同目录下的唯一临时文件 `文件名.<guid>.tmp`，再整体替换目标文件；
   写入/替换失败会清理临时文件，原文件保持完整。
3. `atomic: false`：直接覆盖写入，中途失败可能留下不完整文件。
4. 不做 `fsync`：能保证“不会出现半截文件”，但不提供掉电级的持久性保证。
5. 原子替换会生成新文件，原文件的 ACL/属性（例如只读标记）不保证保留；只读目标会失败。

### 5.2 ConfigObject

有序键值对象，保持插入顺序，键名区分大小写。

| 成员 | 说明 |
| --- | --- |
| `int Count` | 键值对数量 |
| `ConfigValue this[string key]` | 读写字面键；读不存在抛 `KeyNotFoundException` |
| `ConfigValue Get(string key)` | 读字面键，不存在抛 `KeyNotFoundException` |
| `bool TryGetValue(string key, out ConfigValue value)` | 读字面键，不抛异常 |
| `void Set(string key, ConfigValue value)` | 存在则覆盖（保留位置），不存在则追加 |
| `void Add(string key, ConfigValue value)` | 键已存在时抛 `ArgumentException` |
| `bool ContainsKey(string key)` | 字面键是否存在 |
| `string? Comment` / `string? TrailingComment` | 容器头部 / 末尾注释（多行用 `\n` 分隔） |
| `string? GetComment(string key)` | 读取条目上方注释；不存在或没有注释返回 `null` |
| `void SetComment(string key, string? comment)` | 设置条目上方注释；键必须已存在 |
| `string? GetInlineComment(string key)` | 读取条目行尾注释 |
| `void SetInlineComment(string key, string? comment)` | 设置条目行尾注释；多行会合并为一行 |
| `bool Remove(string key)` | 删除并返回是否成功 |
| `void Clear()` | 清空 |
| `string GetString(string key, string defaultValue = "")` | 宽松读取；缺失/null/不可转换返回默认值 |
| `bool GetBoolean(string key, bool defaultValue = false)` | 同上 |
| `int GetInt32(string key, int defaultValue = 0)` | 同上；超出 `int` 范围返回默认值 |
| `long GetInt64(string key, long defaultValue = 0)` | 同上 |
| `double GetDouble(string key, double defaultValue = 0d)` | 同上 |
| `float GetSingle(string key, float defaultValue = 0f)` | `(float)GetDouble(...)` |
| `decimal GetDecimal(string key, decimal defaultValue = 0m)` | 同上 |
| `ConfigValue GetPath(string path)` | 按点路径读，不存在抛 `KeyNotFoundException` |
| `bool TryGetPath(string path, out ConfigValue value)` | 按点路径读，不抛异常 |
| `void SetPath(string path, ConfigValue value)` | 按点路径写，自动创建中间对象 |
| `ConfigObject GetObject(string path)` | 按点路径取对象，不存在则创建 |
| `ConfigArray GetArray(string path)` | 按点路径取数组，不存在则创建 |
| `Enumerator GetEnumerator()` | 按插入顺序枚举；`foreach` 无额外分配 |

`Get*` 系列（宽松读取）的键参数同时支持字面键与点路径，规则见 [4.9](#49-点号键与路径)。

枚举期间修改对象（`Set`/`Add`/`Remove`/`Clear`，包括修改已有键的值）会抛
`InvalidOperationException`，避免得到不确定的枚举结果。

### 5.3 ConfigArray

有序数组，实现 `IList<ConfigValue>` 与 `IReadOnlyList<ConfigValue>`。

| 成员 | 说明 |
| --- | --- |
| `int Count` / `bool IsReadOnly` | `IsReadOnly` 恒为 `false` |
| `ConfigValue this[int index]` | 读写索引处元素 |
| `Add` / `AddRange` / `Insert` / `Remove` / `RemoveAt` / `Clear` | 常规集合操作 |
| `Contains` / `IndexOf` / `CopyTo` | 使用 `ConfigValue` 相等性（数组/对象按引用比较） |
| `int GetInt32(int index, int defaultValue = 0)` | 越界或不可转换返回默认值 |
| `long GetInt64(...)` / `double GetDouble(...)` | 同上 |
| `float GetSingle(...)` / `decimal GetDecimal(...)` | 同上 |
| `bool GetBoolean(...)` / `string GetString(...)` | 同上 |
| `int[] ToInt32Array()` | 逐元素转换，不可转换的元素取 `0` |
| `long[] ToInt64Array()` / `double[] ToDoubleArray()` | 同上 |
| `string[] ToStringArray()` | 同上（`null` 元素得到默认值 `""`） |
| `List<ConfigValue>.Enumerator GetEnumerator()` | `foreach` 无额外分配；枚举期间修改会抛异常 |

### 5.4 ConfigValue

`readonly struct`，不装箱地承载任意配置值。

构造与隐式转换：

```csharp
ConfigValue a = 123;                 // Integer
ConfigValue b = 1.5;                 // Float（double）
ConfigValue c = true;                // Boolean
ConfigValue d = "text";              // String
ConfigValue e = (string?)null;       // Null（显式 string? 转换）
ConfigValue f = ConfigValue.Null;    // Null 单例
ConfigValue g = new ConfigObject();  // Object
ConfigValue h = new ConfigArray();   // Array
```

> `null` 字面量不能直接赋给 `ConfigValue`（编译器报 CS0037：目标是非可空值类型）。
> 请使用 `ConfigValue.Null` 或 `(string?)null`。

类型判断与读取：

| 成员 | 说明 |
| --- | --- |
| `Kind` | 枚举值 `Null/Boolean/Integer/Float/String/Array/Object` |
| `IsNull` / `IsScalar` | 标量 = `null`/布尔/整数/浮点/字符串 |
| `IsBoolean` / `IsInteger` / `IsFloat` / `IsString` / `IsArray` / `IsObject` | 精确类型判断 |
| `AsBoolean()` / `AsInt64()` / `AsDouble()` / `AsDecimal()` / `AsString()` | 宽松转换；失败抛 `InvalidCastException` |
| `AsInt32()` / `AsInt16()` / `AsSByte()` / `AsByte()` / `AsUInt16()` / `AsUInt32()` | 先 `AsInt64()` 再 `checked` 收窄；溢出抛 `OverflowException` |
| `AsUInt64()` | 按 64 位补码位模式读取（`-1` → `ulong.MaxValue`） |
| `AsSingle()` | `(float)AsDouble()`，超出 `float` 范围得到 `±Infinity` |
| `AsArray()` / `AsObject()` | 仅当类型匹配；否则抛 `InvalidCastException` |
| `ToString()` | 调试文本；超过 32 层嵌套会输出 `…` |

相等性：

- 类型必须相同；整数/布尔/浮点按**位模式**比较（`1` 与 `1.0` 不相等；相同位模式的 `NaN` 相等）。
- 字符串按 `Ordinal` 比较；数组/对象按**引用**比较。
- `GetHashCode` 与 `Equals` 保持一致。

存储约定：

- `decimal` 会被转成 `double`，有效数字超过 15~17 位时可能丢精度；需要精确小数请用字符串。
- `ulong` 以补码位模式存入；大于 `long.MaxValue` 的值用 `AsInt64()` 读回是负数，请用 `AsUInt64()`。

### 5.5 ConfigValueKind

```csharp
public enum ConfigValueKind : byte
{
    Null = 0, Boolean, Integer, Float, String, Array, Object,
}
```

枚举顺序参与 `IsScalar` 判断（`<= String` 为标量），请勿依赖也不要在外部重新定义。

### 5.6 ConfigFormatException

语法错误异常：

| 成员 | 说明 |
| --- | --- |
| `int Line` | 出错行号，从 1 开始 |
| `int Column` | 出错列号，从 1 开始 |
| `Message` | 中文原因 + `（第 X 行，第 Y 列）` |

> 当前版本的错误消息为中文；`Line` / `Column` 与消息文本无关于语言，可在 UI 中自行本地化展示。

### 5.7 ConfigFile（自动映射）

建立在文档 API 之上的反射便捷层，已标注 `RequiresUnreferencedCode` / `RequiresDynamicCode`。

| 成员 | 说明 |
| --- | --- |
| `ConfigObject ToObject<T>(T value, bool includeModelComments = true)` | 类 → 配置根对象 |
| `string ToText<T>(T value, bool includeHeader = true, bool includeModelComments = true)` | 类 → 文本 |
| `void Save<T>(string path, T value, bool atomic = true, bool includeHeader = true, bool includeModelComments = true)` | 类 → 文件 |
| `bool TrySave<T>(string path, T value, out string? error, bool atomic = true, bool includeHeader = true, bool includeModelComments = true)` | 不抛异常的保存 |
| `T Load<T>(string path) where T : new()` | 文件 → 类 |
| `bool TryLoad<T>(string path, out T? value, out string? error)` | 不抛异常的加载 |
| `T LoadOrDefault<T>(string path, out string? error)` | 失败时返回 `new T()`，`error` 说明原因 |
| `T Parse<T>(string text) where T : new()` | 文本 → 类 |
| `T FromObject<T>(ConfigObject root) where T : new()` | 根对象 → 类 |
| `void LoadInto<T>(ConfigObject root, T target) where T : class` | 填充已有实例 |

`Load` / `Parse` / `ToObject` 抛出映射过程中的原始异常（用户 getter/setter 异常会解包）；
`TryLoad` / `TrySave` / `LoadOrDefault` 则不抛异常，通过错误文本返回原因。

### 5.8 映射标注

| 特性 | 目标 | 说明 |
| --- | --- | --- |
| `[ConfigName("key")]` | 字段 / 属性 | 指定键名；含 `.` 时按点路径写出/读取 |
| `[ConfigIgnore]` | 字段 / 属性 | 不参与自动映射 |
| `[Description("...")]` | 字段 / 属性 / 类型 | 系统特性；自动注释优先读取，其次 XML `<summary>` |

点路径键在同一类型内不能与另一个成员的键互为前缀（例如 `serial` 与 `serial.port`），否则写出会互相覆盖，映射阶段直接报错。

## 6. 类型转换规则

### 6.1 转换矩阵

| 源 \ 目标 | bool | 整数 | double | decimal | string |
| --- | --- | --- | --- | --- | --- |
| `null` | 默认值 | 默认值 | 默认值 | 默认值 | 默认值 |
| bool | ✔ | `1` / `0` | `1` / `0` | `1` / `0` | `"true"` / `"false"` |
| 整数 | `!= 0` | ✔ | ✔（大整数可能丢精度） | ✔ | 十进制文本 |
| 浮点 | `!= 0`（`NaN` → `true`） | 向零截断，须在 `long` 范围内 | ✔ | 须严格落在 `decimal` 范围内 | 最短往返文本 |
| 字符串 | `true/yes/on/1`、`false/no/off/0` | 十进制或 `0x/0b/0o`，允许 `_` | Invariant 解析，允许首尾空白 | Invariant 解析 | ✔ |
| 数组 / 对象 | ✘ | ✘ | ✘ | ✘ | ✘ |

- ✘ 表示不可转换：`Get*` 返回调用方默认值，`As*` 抛 `InvalidCastException`。
- 浮点 → 整数的边界使用严格判断：`2^63` 不会被静默截断为 `long.MinValue`，而是判为不可转换。
- 浮点 → decimal 同样严格：恰好 `±2^96` 的边界值判为不可转换，不会抛 `OverflowException`。
- `null` 读取字符串返回默认值（`null` 表示“没有值”，而不是空串）。
- 整数 / 布尔 / 浮点 → 字符串使用 `InvariantCulture`，与系统区域设置无关。

### 6.2 整数文本

`TryParseInteger` 与 `GetInt32` / `GetInt64` 使用同一套规则：

```text
[+|-] digits
[+|-] 0x hexdigits
[+|-] 0b bindigits
[+|-] 0o octdigits
```

- `_` 只能位于两个数字之间。
- 十进制超出 `long` 范围 → 解析失败（文本保留为字符串）。
- 十六进制/二进制/八进制按 64 位位模式解释；超过 64 位 → 解析失败。
- 负十六进制最小值为 `-0x8000_0000_0000_0000`。

### 6.3 浮点格式

- 写出使用 .NET `"R"` 最短往返格式，`InvariantCulture`。
- 若结果不含 `.` / `E` / `e`，补 `.0`，保证读回仍是 `Float`（例如 `3.0`）。
- `NaN`、`Infinity`、`-Infinity` 原样写出并可读回。
- 读入后类型的判定顺序：`null` → `true/false` → `NaN`/`±Infinity` → 整数 → 浮点 → 字符串。

### 6.4 宽松读取与强类型读取

```csharp
// 宽松：缺失、null、类型不可转换、数值越界 → 返回默认值
int port = root.GetInt32("serial.port", 9600);

// 强类型：不可转换抛 InvalidCastException；收窄溢出抛 OverflowException
int strict = root.Get("serial.port").AsInt32();
```

## 7. 文件读写与原子保存

```csharp
// 加载（自动识别 UTF-8 / UTF-16 BOM）
ConfigDocument doc = ConfigDocument.Load("config/app.cif");

// 失败不抛异常
if (!ConfigDocument.TryLoad("config/app.cif", out ConfigDocument? loaded, out string? error))
    Console.WriteLine($"加载失败：{error}");

// 保存（默认原子替换）
doc.Save("config/app.cif");

// 保存为不含头注释的文本
doc.Save("config/app.cif", includeHeader: false);

// 非原子写入（兼容特殊文件系统/需要直接覆盖的场景）
doc.Save("config/app.cif", atomic: false);
```

原子保存的保证与边界：

- 目标文件在同一目录内整体替换，读取方要么看到旧内容，要么看到新内容，不会看到写了一半的文件。
- 临时文件唯一，多个进程同时保存同一路径不会互相覆盖临时文件；最终结果为“最后完成者获胜”。
- 写入/替换失败会清理临时文件，原文件不受影响。
- 不保证 `fsync` 级别的掉电持久性。
- 原子替换后，原文件的 ACL 与只读属性不保证保留；目标为只读文件时会失败。

## 8. 错误处理

解析错误统一抛 `ConfigFormatException`：

```csharp
try
{
    ConfigDocument.Parse(text);
}
catch (ConfigFormatException ex)
{
    Console.WriteLine($"第 {ex.Line} 行，第 {ex.Column} 列：{ex.Message}");
}
```

不抛异常的 API：

| API | 失败时 |
| --- | --- |
| `ConfigDocument.TryParse` | 返回 `false`，`out ConfigFormatException` 保留行号/列号 |
| `ConfigDocument.TryLoad` | 返回 `false`，`out string` 返回原因 |
| `ConfigDocument.TrySave` | 返回 `false`，`out string` 返回原因 |
| `ConfigObject.TryGetValue` / `TryGetPath` | 返回 `false`，`out ConfigValue` 为 `default`（`Null`） |

常见错误定位：

| 输入 | 错误 |
| --- | --- |
| `a = [1, 2` | 数组缺少 `]` |
| `a =`（行尾） | 缺少值 |
| `a = 1` 后下一行 `b` | 键后缺少 `=` 或 `{` |
| `a: 1` | 应使用 `=` 而不是 `:` |
| `["a.b"]` | 表头不支持引号 |
| 块内写 `[section]` | 对象内部不支持表头 |
| `a = "未闭合` | 字符串缺少结束引号 |
| 嵌套超过 256 层 | 嵌套层级超过上限 |

## 9. 性能与内存

- **单遍扫描**：`ConfigParser` 是 `ref struct`，直接在 `ReadOnlySpan<char>` 上前进，不构建 Token 列表。
- **核心零反射**：解析器/写出器不含 `System.Reflection`、`dynamic`、序列化器；自动映射为独立可选层。
- **低分配读取**：`ConfigObject` / `ConfigArray` 的 `foreach` 使用 struct 枚举器。
- **查找优化**：`ConfigObject` 在键数超过 12 个时自动建立 `Dictionary<string,int>` 索引，之后按键查找为 O(1)。
- **写出**：`StringBuilder` 预估容量；短数组单行、长数组自动多行。
- **错误定位**：仅在出错时扫描行号，正常路径无额外成本。

`samples/ConfigInfo.Demo` 内置基准（200 节 × 10 键 ≈ 2000 个键值），运行示例即可看到本机数据。
性能随文本规模线性增长；`Parse` 与 `ToText` 的时间复杂度均为 O(n)。

## 10. AOT 与裁剪

```xml
<IsAotCompatible>true</IsAotCompatible>
<IsTrimmable>true</IsTrimmable>
```

- 核心解析/写出（`ConfigDocument` / `ConfigObject` / `ConfigArray` / `ConfigValue`）不使用反射、`dynamic`、
  `JsonSerializer`、源生成器或序列化特性。
- 可直接用于 NativeAOT / IL 裁剪项目；`ConfigValue` 的转换全部为手写代码。
- `ConfigFile` 自动映射是反射便捷层，已标注 `RequiresUnreferencedCode` / `RequiresDynamicCode`：
  裁剪 / AOT 项目请手写 `SaveTo` / `LoadFrom`（见 3.4）。

跨平台：

- 只使用 BCL 跨平台 API（`File` / `Path` / `UTF8Encoding`），无 `P/Invoke`、注册表、Win32 或
  `System.Windows` 依赖；数字格式化/解析固定使用 `InvariantCulture`，换行固定输出 `LF`，
  文件固定 UTF-8 无 BOM，因此同一份配置文本在各系统上语义一致。
- 已验证：`dotnet build ConfigInfo.sln` 0 警告 0 错误，示例自测 **76/76** 通过。

NativeAOT 发布的前置条件是目标平台的 C++ 工具链（Windows 为 MSVC 链接器）。若 `PublishAot=true`
在最后的原生链接步骤报 `Platform linker not found`，请安装 Visual Studio 的
“使用 C++ 的桌面开发”工作负载后重试；AOT/裁剪分析器本身已是 0 警告。

> 示例工程为演示自动映射关闭了 IL2026 / IL3050 提示；库本身仍启用 `IsAotCompatible`，
> 且核心 API 保持零反射。

体验 NativeAOT 发布：

```powershell
dotnet publish samples/ConfigInfo.Demo -c Release -r win-x64 -p:PublishAot=true
```

## 11. 线程安全

- `ConfigDocument`、`ConfigObject`、`ConfigArray`、`ConfigValue` **不是线程安全的**。
- 多线程同时读：在没有任何写入的前提下可以并发读取（读取不改变内部状态，`ConfigObject` 的索引只在写入时重建）。
- 多线程读写：请由调用方加锁，或每个线程使用独立文档。
- 多进程保存同一路径：临时文件唯一，不会互相踩踏；结果是最后完成者获胜。
- 枚举（`foreach` / `GetEnumerator`）期间被修改会抛 `InvalidOperationException`。

## 12. 已知限制

| 限制 | 说明 / 规避 |
| --- | --- |
| 无 schema/校验 | 库只负责读写；业务校验请在映射层完成 |
| 不保留数组内注释与排版 | 条目注释会保留；数组元素内部的注释、空行与缩进不保留 |
| `[section]` 只限顶层 | 块内请用 `key { ... }` |
| 不支持 `:` 分隔 | 统一使用 `=` |
| 字符串不能跨行 | 使用 `\n` 转义 |
| 裸值不能以 `#` / `//` 开头 | 加引号，如 `"#FF0000"` |
| 裸值不能包含 `,` `]` `}` | 加引号 |
| 一行一个标量键值 | 裸值会吞掉行内剩余内容（允许空格与 `=`） |
| 键名区分大小写 | 需要忽略大小写请在业务层处理 |
| 嵌套上限 256 层 | 解析/写出均有限制；`ToString` 调试输出在 32 层截断 |
| `decimal` 精度 | 内部按 `double` 存储，最多约 15~17 位有效数字 |
| 自动映射成员范围 | 仅公共实例字段与公共可读写属性；只读字段、只读属性、索引器、字典、多维数组不参与 |
| 自动映射构造 | 模型及嵌套元素类型需要公共无参构造函数；默认值请在字段/属性初始化器中提供 |
| 自动映射类型范围 | 不支持 `DateTime` / `TimeSpan` / `Guid` 等特殊类型；请改用字符串或手写映射 |
| AOT 下的自动映射 | `ConfigFile` 使用反射；裁剪 / NativeAOT 请手写 `SaveTo` / `LoadFrom` |
| 自动映射读取类型 | `Load<T>` / `FromObject<T>` 按指定的 `T` 读取，派生成员需指定派生类型 |
| `[ConfigName]` 路径 | 同一类型内不能与其他成员键互为前缀（如 `serial` 与 `serial.port`） |
| `ulong` 高位 | 按补码位模式存储，用 `AsUInt64()` 读取 |
| 十进制整数溢出 | 超出 `long` 的字面量保留为字符串，不会转成浮点 |
| UTF-16 无 BOM | 不支持自动识别，请保存为 UTF-8 或带 BOM |
| 大文件 | `Load` 一次性读入内存，超大文件请自行流式处理 |
| 错误消息语言 | 当前为中文；行号/列号可直接用于本地化展示 |

## 13. 构建、测试与示例

```powershell
# 构建
dotnet build ConfigInfo.sln -c Debug

# 单元测试（112 个用例）
dotnet test tests/ConfigInfo.Tests/ConfigInfo.Tests.csproj

# 运行示例（生成示例配置、演示读取/修改/容错/性能）
dotnet run --project samples/ConfigInfo.Demo

# 运行内置自测（76 项断言，返回码 0 表示全部通过）
dotnet run --project samples/ConfigInfo.Demo -- --selftest
```

解决方案结构：

```text
ConfigInfo.sln
├── src/ConfigInfo/                 # 库
│   ├── ConfigDocument.cs           # 文档、解析、文件读写
│   ├── ConfigFile.cs               # 类 ⇄ 文件自动映射（反射便捷层）
│   ├── ConfigAttributes.cs         # ConfigName / ConfigIgnore 标注
│   ├── ConfigObject.cs             # 有序键值对象、路径访问、条目注释
│   ├── ConfigArray.cs              # 有序数组
│   ├── ConfigValue.cs              # 值结构体、转换、文本解析辅助
│   ├── ConfigValueKind.cs          # 值种类枚举
│   ├── ConfigFormatException.cs    # 语法错误（行/列）
│   └── Internal/
│       ├── ConfigHeader.cs         # 标准文件头（解析时识别并跳过）
│       ├── ModelCommentReader.cs   # [Description] / XML summary 读取（自动注释）
│       ├── ConfigParser.cs         # 单遍解析器
│       └── ConfigWriter.cs         # 文本写出器
├── tests/ConfigInfo.Tests/         # xUnit 测试
└── samples/ConfigInfo.Demo/        # 示例程序 + 内置自测
```

> 本机若只安装了 .NET 9/10 运行时，可先设置 `$env:DOTNET_ROLL_FORWARD = 'LatestMajor'` 再运行测试或示例。

## 14. 审计与修复记录

版本 1.2.0 新增（纯增量，不改变 `.cif` 格式）：

| 变化 | 说明 |
| --- | --- |
| 自动映射 | `ConfigFile.Save` / `Load<T>` / `TryLoad` / `LoadOrDefault` / `Parse` / `FromObject` / `LoadInto` |
| 注释读写 | 条目上方注释 / 行尾注释、根对象头尾注释；`Load → Save` 保留，标准头自动去重 |
| 自动注释 | `[Description]` / XML `<summary>` 自动加在条目上方；类型说明写入文件头 |
| 映射标注 | `[ConfigName]`（含点路径）、`[ConfigIgnore]` |
| 映射规则 | 嵌套类、数组 / `List<T>`、枚举、可空值类型、大小写容错、缺失键保留默认值 |
| 安全边界 | 循环引用检测、256 层嵌套上限、路径键冲突检测、可空元素/可空结构体根、根级派生字段不丢失、用户 getter/setter 异常解包 |

本次完整审计（版本 1.1.0）修复的问题：

| # | 问题 | 影响 | 修复 |
| --- | --- | --- | --- |
| 1 | `double → long` 用 `<= (double)long.MaxValue` 判断 | `2^63` 被静默截断为 `long.MinValue` | 改为严格小于；边界值判为不可转换 |
| 2 | `double → decimal` 用 `<= (double)decimal.MaxValue` | 边界值抛 `OverflowException`，与 `Get*` 约定不符 | 改为严格不等号；统一返回默认值/`InvalidCastException` |
| 3 | 写出器不转义含 `.` 的键 | 读回时键被展开成嵌套对象，结构被破坏 | `NeedsQuote` 增加 `.` 与 `:` |
| 4 | 解析器不认识 `NaN` / `Infinity` | 浮点特殊值写出后变成字符串 | 类型推断显式识别 `NaN` / `±Infinity` |
| 5 | 解析器无嵌套深度上限 | 深嵌套文本导致 `StackOverflowException`（进程崩溃） | 解析/写出统一 256 层上限，抛可捕获异常 |
| 6 | `a =` 后跟注释再跟下一行键值 | 注释被跳过，下一行 `b = 2` 被当成 `a` 的值 | 值必须与 `=` 同行开始；数组/对象内部仍支持多行与注释 |
| 7 | 带 BOM 的字符串解析 | BOM 被并入首个键名 | 解析器自动跳过开头 `\uFEFF` |
| 8 | 仅 CR 换行时行号不增长 | 错误行号恒为 1 | `GetLineColumn` 同时处理 LF/CRLF/CR |
| 9 | 下划线规则过宽 | `1_`、`1__0`、`0x_1F` 被当作整数 | 下划线只能位于两个数字之间 |
| 10 | 十六进制累加器无溢出检查 | 超过 64 位的字面量静默回绕 | 超长字面量判为失败（保留为字符串） |
| 11 | `null` 读取字符串返回空串 | 与“缺失返回默认值”的约定不一致 | `null` 视为不可转换，返回默认值 |
| 12 | 枚举期间修改对象无提示 | 可能得到重复/遗漏的枚举结果 | 增加版本号检查，抛出 `InvalidOperationException` |
| 13 | 原子保存固定 `.tmp` 文件名 | 并发保存互相覆盖；失败残留临时文件 | 唯一临时名 + 失败清理 |
| 14 | 缺少不抛异常的解析 API | 调用方需自行 `try/catch` 并丢失行号 | 新增 `TryParse`，保留 `ConfigFormatException` |
| 15 | 孤立代理项直接写出 | 可能产生不可往返/非法的 UTF-16 文本 | 代理对原样输出，孤立代理项转 `\uXXXX` |
| 16 | `ConfigArray` 缺少 `GetSingle`/`GetDecimal` | 与 `ConfigObject` API 不对称 | 已补齐 |
| 17 | 示例中 `(byte)int` 截断 | 帧头值 >255 时静默回绕 | 使用 `Math.Clamp` |
| 18 | 错误提示不够明确 | `a: 1`、块内 `[section]` 报错含糊 | 增加针对性错误消息 |
| 19 | 表头内的引号被当作普通字符切分 | `["a.b"]` 生成 `"a` / `b"` 等怪异键名且不报错 | 表头检测引号并给出明确错误 |

回归测试：`tests/ConfigInfo.Tests` 共 **112** 个用例；示例内置自测 **76** 项断言；构建 0 警告 0 错误。

## 15. 版本与兼容性

| 项目 | 值 |
| --- | --- |
| 库版本 | 1.2.0（新增自动映射，向后兼容） |
| 目标框架 | `net8.0` |
| 支持平台 | Windows / Linux / macOS（任何 .NET 8 支持的平台；无平台专用 API） |
| 语言版本 | `latest`（使用文件作用域命名空间、集合表达式等） |
| 文件格式 | `.cif` 1.0（向后兼容；本次仅修复缺陷，未引入不兼容的语法变更） |
| 依赖 | 无 |
| 可空引用类型 | 已启用 |

格式兼容性说明：

- 1.2.0 为纯增量：新增 `ConfigFile`、两个标注特性与注释读写，底层 API 与 `.cif` 语法未变。
- 自动映射的默认键名为成员名首字母小写；升级旧配置时可使用 `[ConfigName("...")]` 保持原键名。
- 注释现在参与 `Load → Save` 往返；此前版本会丢弃注释。
- 自动注释为 1.2.0 新增能力，可用 `includeModelComments: false` 关闭，不影响 `.cif` 格式。

- 本次修复中，`1_` 与超 64 位十六进制字面量从“被当作数字”改为“保留为字符串”，属于**纠错**而非语法扩展。
- 含 `.` 的裸键语义未变（仍会展开为嵌套对象）；只是写出器现在会给这类字面键加引号，确保往返正确。
- `a =` 后换行再写值以前被容忍，现在会报“缺少值”；这避免了吞掉下一行的静默错乱。
- 表头内的引号（如 `["a.b"]`）以前会被静默切成 `"a` / `b"` 两个键，现在直接报错；
  含 `.` 的字面键请使用键值行的引号写法 `"a.b" = 1`。

---

# English Manual

## Table of Contents

- [1. Overview](#1-overview)
- [2. Feature Summary](#2-feature-summary)
- [3. Quick Start](#3-quick-start)
- [4. The .cif Format](#4-the-cif-format)
- [5. API Reference](#5-api-reference)
- [6. Conversion Rules](#6-conversion-rules)
- [7. File I/O and Atomic Save](#7-file-io-and-atomic-save)
- [8. Error Handling](#8-error-handling)
- [9. Performance and Memory](#9-performance-and-memory)
- [10. NativeAOT and Trimming](#10-nativeaot-and-trimming)
- [11. Thread Safety](#11-thread-safety)
- [12. Known Limitations](#12-known-limitations)
- [13. Build, Test, and Samples](#13-build-test-and-samples)
- [14. Audit and Fix Log](#14-audit-and-fix-log)
- [15. Version and Compatibility](#15-version-and-compatibility)

## 1. Overview

`ConfigInfo` is a small, dependency-free C# library for a hand-editable text configuration
format called `.cif` (ConfigInfo Format). It is designed for scenarios where users edit the
configuration file directly: desktop tools, serial/network debugging assistants, embedded
host applications, and similar utilities.

Design goals:

1. **Readable** — plain text, `key = value`, obvious at a glance.
2. **Forgiving** — `"115200"` can be read as an `int`; small hand-editing mistakes do not
   silently produce missing values.
3. **Stable** — insertion order is preserved, and `ToText → Parse → ToText` is idempotent;
   syntax errors report exact line and column numbers.
4. **AOT-safe core** — parsing and writing use no reflection, no dynamic code, and no serializer
   attributes; the optional object mapper is reflection-based and explicitly annotated.
5. **Bounded** — nesting depth is capped, so malformed input cannot crash the process with
   a `StackOverflowException`.

The library targets `net8.0`, ships as a single assembly, and has no third-party dependencies.

## 2. Feature Summary

| Capability | Details |
| --- | --- |
| Data model | `null`, boolean, 64-bit integer, double, string, array, ordered object |
| Syntax | `key = value`, `[section.path]`, `key { ... }`, `[1, 2, 3]`, `{ a = 1 }` |
| Comments | `#` and `//`, whole-line or end-of-line; read/write with `Load → Save` preservation |
| Strings | Double-quoted with escapes; single-quoted literal (great for Windows paths) |
| Numbers | Decimal, `0x` / `0b` / `0o`, `_` separators, `NaN` / `±Infinity` |
| Reading | Literal keys, dotted paths, strict `As*`, lenient `Get*` with defaults |
| Object mapping | `ConfigFile.Save(path, obj)` / `ConfigFile.Load<T>(path)`; tune with `[ConfigName]` / `[ConfigIgnore]` |
| Auto comments | `[Description]` / XML `<summary>` written as entry comments; disable with `includeModelComments` |
| Writing | Insertion order preserved; short arrays inline, long arrays multi-line |
| Files | UTF-8 without BOM on save; UTF-8 / UTF-16 BOM detection on load; atomic replace |
| Diagnostics | `ConfigFormatException` with 1-based `Line` / `Column` |
| AOT | Zero-reflection core; the `ConfigFile` mapper is a reflection convenience layer (RUC/RDC; hand-write mapping for AOT) |

## 3. Quick Start

### 3.1 Requirements

- .NET SDK 8.0 or later (the library targets `net8.0`).
- A .NET 8+ runtime. If only a newer runtime is installed, enable roll-forward:

```powershell
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
```

### 3.2 Reference the project

```xml
<ItemGroup>
  <ProjectReference Include="..\src\ConfigInfo\ConfigInfo.csproj" />
</ItemGroup>
```

### 3.3 Minimal example

```csharp
using ConfigInfo;

ConfigDocument document = ConfigDocument.Parse("""
    app.name = "Inspector"
    app.version = 'C:\Program Files\App\1.0'
    app.port = 8080

    [serial]
    port = COM3
    baudRate = "115200"   # a string can be read as an int
    frame = [0xAA, 0x55]
    """);

ConfigObject root = document.Root;

string name  = root.GetString("app.name", "unnamed");
int port     = root.GetInt32("app.port");
int baud     = root.GetInt32("serial.baudRate");   // 115200
int[] frame  = root.GetPath("serial.frame").AsArray().ToInt32Array();

root.SetPath("serial.port", "COM7");                 // update an existing nested key
root.SetPath("network.tcp.remotePort", 9000);        // creates intermediate objects
document.Save("config/app.cif");                     // atomic by default
```

### 3.4 Strongly typed mapping: hand the class to the library

No base class, interface, source generator, or configuration is required. Hand a plain class to
`ConfigFile` to save it, and generate the class directly when reading a file.

```csharp
internal sealed class AppSettings
{
    public string ComPort = "COM1";                         // → comPort
    public int BaudRate = 9600;                             // → baudRate
    public byte[] FrameHead = [0xAA, 0x55];                 // → frameHead = [170, 85]
    public SerialSettings Serial = new();                   // → serial { ... }

    [ConfigName("network.remoteIp")] public string RemoteIp = "127.0.0.1";
    [ConfigIgnore] public bool IsConnected;                 // neither written nor read
}

internal sealed class SerialSettings
{
    public int DataBits = 8;                                // → serial.dataBits
    public string Parity = "None";                          // → serial.parity
}

var settings = new AppSettings { ComPort = "COM7" };
ConfigFile.Save("config/app.cif", settings);                // object → file (atomic by default)

AppSettings loaded = ConfigFile.Load<AppSettings>("config/app.cif");   // file → object

// Never throws for a missing or broken file; returns an instance with field defaults:
AppSettings safe = ConfigFile.LoadOrDefault("config/app.cif", out string? error);
```

Mapping rules:

- Public instance fields and public read/write properties are mapped; the default key is the
  member name with a lowercase first letter (`ComPort` → `comPort`).
- Nested classes become child objects; `T[]` / `List<T>` become arrays whose elements may be
  scalars or nested objects.
- Supported: `string`, `bool`, all integer types, `float`, `double`, `decimal`, enums (written by
  name), nullable value types, arrays, `List<T>`, and nested classes.
- Missing keys keep the `new T()` default; keys match exactly first, then case-insensitively;
  unknown keys are ignored.
- Lenient conversion applies: `"115200"` and `0x1F90` both read as integers; narrowing clamps to
  the target range.
- `[ConfigName("serial.port")]` renames a member or writes a flat class as groups via a dotted
  path; `[ConfigIgnore]` excludes a member.
- Dotted paths must be valid (no leading/trailing `.` or `..`) and must not be a prefix of another
  member key in the same type (for example `serial` plus `serial.port` is rejected to prevent
  write-time overwrites).
- The root object is written using its **runtime type**: calling `ToObject`/`Save` through a
  base-class or interface variable does not lose root-level derived members; `LoadInto` also reads
  by the target's runtime type. Nested members and collection elements are written/read by their
  declared types; use the derived type at the root or flatten them if their extra fields matter.
  Reading uses the `T` supplied to `Load<T>` / `FromObject<T>`; specify the derived type to read
  derived members.
- Circular references, unsupported member types (for example `object`), and nesting beyond 256
  levels raise explicit exceptions.
- Auto comments: by default member `[Description]` / XML `<summary>` is written as entry comments,
  and the type's own description becomes the file-level `Root.Comment`; disable with
  `includeModelComments: false`. XML summaries require the model assembly to generate and deploy
  its `.xml` documentation file.
- `ConfigFile.Save` builds a fresh root object and does not inherit comments from the old file.
  To keep old file comments, modify and save through `ConfigDocument.Load`, or call `SetComment`
  on the `ToObject` result before saving.

> The mapper is reflection-based: `ConfigFile` is annotated with `RequiresUnreferencedCode` and
> `RequiresDynamicCode`. For NativeAOT / trimming, use the zero-reflection manual mapping below;
> calling `ConfigFile` produces IL2026 / IL3050 diagnostics.

```csharp
// Zero-reflection manual mapping (AOT-safe): the low-level API is unchanged
internal sealed class AppSettings
{
    public string ComPort = "COM1";
    public int BaudRate = 9600;

    public void SaveTo(ConfigObject root)
    {
        ConfigObject serial = root.GetObject("serial");
        serial.Set("port", ComPort);
        serial.Set("baudRate", BaudRate);
    }

    public static AppSettings LoadFrom(ConfigObject root) => new()
    {
        ComPort = root.GetString("serial.port", "COM1"),
        BaudRate = root.GetInt32("serial.baudRate", 9600),
    };
}
```

## 4. The .cif Format

### 4.1 Files and encoding

- UTF-8 without BOM is recommended, and `Save` always writes UTF-8 without BOM.
- `Load` uses `File.ReadAllText` BOM detection: UTF-8 (with BOM) and UTF-16 LE/BE (with BOM)
  are supported. Without a BOM, the file is decoded as UTF-8; UTF-16 without BOM is not detected.
- `Parse(string)` skips a leading `\uFEFF` (BOM) if present.
- Line endings: `LF`, `CRLF`, and `CR` are accepted; output always uses `LF`.

### 4.2 Comments

```cif
# file-level note (leave one blank line before the first entry)

# serial settings
serial {
    # baud rate
    baudRate = 115200   # unit: bps
}
```

- `#` and `//` start a comment only at the beginning of a line or after whitespace.
  Thus a bare value such as `http://host/path#frag` is preserved.
- Inside quoted strings, `#` and `//` are ordinary characters.
- Comments participate in text round-trips: `Load → Save` preserves leading and inline comments
  (always written with `#`).
  - A block at the beginning of the file, separated from the first entry by a blank line,
    becomes `ConfigObject.Comment` on the root object.
  - Other blocks belong to the entry that follows; a block immediately before `}` becomes
    `ConfigObject.TrailingComment`.
- Usage:

```csharp
var document = new ConfigDocument();
document.Root.Comment = "application settings";
ConfigObject serial = document.Root.GetObject("serial");
serial.Set("port", "COM3");
document.Root.SetComment("serial", "serial settings");   // above the entry
serial.SetComment("port", "port name");
serial.SetInlineComment("port", "for example COM3");     // end of line
serial.TrailingComment = "end of serial settings";
document.Save("app.cif");

// Read comments back
ConfigDocument loaded = ConfigDocument.Load("app.cif");
string? file = loaded.Root.Comment;
string? group = loaded.Root.GetComment("serial");
string? port = loaded.Root.GetObject("serial").GetComment("port");
string? inline = loaded.Root.GetObject("serial").GetInlineComment("port");
```

- `SetComment` / `SetInlineComment` use literal keys and require the key to exist; use
  `GetObject` first for nested objects.
- The generated **standard header** is recognized and skipped by the parser, so repeated
  `Load → Save` does not duplicate it.
- Not preserved: the comment marker (`//` becomes `#`), comment indentation/blank lines, and
  comments inside array elements.

#### Generating comments from model docs

`ConfigFile.ToObject` / `Save` / `ToText` write model documentation as comments by default
(`includeModelComments: true`):

```csharp
using System.ComponentModel;

/// <summary>application settings</summary>
public sealed class AppSettings
{
    [Description("serial port name, for example COM3")]
    public string Port = "COM1";

    /// <summary>baud rate</summary>
    public int BaudRate = 9600;

    public SerialSettings Serial = new();
}

/// <summary>nested serial settings</summary>
public sealed class SerialSettings
{
    /// <summary>data bits</summary>
    public int DataBits = 8;
}

ConfigFile.Save("app.cif", settings);                               // comments included
ConfigFile.Save("app.cif", settings, includeModelComments: false);  // disabled
```

Rules:

- Priority: member `[Description]` > member XML `<summary>` > member type (nested class / enum /
  collection element type) `[Description]` / `<summary>`.
- The type's own description becomes `Root.Comment` (file header).
- XML `<summary>` requires `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and the
  generated `.xml` next to the assembly (single-file falls back to the app directory); a missing
  file is ignored.
- Auto comments only fill gaps; later `SetComment` / `SetInlineComment` calls override them.
- Per-element comments inside arrays / `List<T>` are not supported.

### 4.3 Keys and values

- `key = value` must be written on one line; a value must start on the same line as `=`.
- Keys are case-sensitive.
- Duplicate keys: the last value wins and keeps the position of the first occurrence.
- Keys may contain spaces and non-ASCII characters. The writer quotes keys that contain
  whitespace, control characters, or any of `.` `:` `#` `//` `=` `{` `}` `[` `]` `,` `"` `'` `\`.
- A key containing `:` must be quoted; `a: 1` is reported as a wrong separator.
- Commas between top-level entries are tolerated but not required.

### 4.4 Strings

```cif
path  = 'C:\Program Files\App\1.0'   # single quotes: backslashes are literal
title = "Inspector"
quote = "He said: \"hello\""
multi = "line one\nline two"
```

| Syntax | Behavior |
| --- | --- |
| `'...'` | Literal; `''` represents a single quote; backslashes are not escapes |
| `"..."` | Escapes: `\"` `\\` `\/` `\b` `\f` `\n` `\r` `\t` `\0` `\uXXXX` |
| Bare | Unquoted text; termination rules in [4.11](#411-syntax-cheat-sheet) |

- A quoted string cannot contain a literal newline; use `\n`.
- Unknown escapes (for example `\q`) are errors, not silently dropped.
- Surrogate pairs (emoji, etc.) round-trip; lone surrogates are written as `\uXXXX`.

### 4.5 Numbers

```cif
count   = -42
ratio   = 0.75
big     = 1e9
mask    = 0x1F
flags   = 0b1010_1010
perm    = 0o755
special = NaN
limit   = Infinity
```

| Form | Notes |
| --- | --- |
| Decimal integer | 64-bit signed; out-of-range literals stay **strings** (no silent truncation) |
| `0x` / `0b` / `0o` | Interpreted as a 64-bit unsigned bit pattern; the high bit set reads back negative; use `AsUInt64()` |
| `_` separator | Only between two digits: `1_000` is valid; `1_`, `1__0`, `0x_1F` are not |
| Floating point | Decimal with `.` or an exponent; IEEE 754 `double`; `1e400` becomes `Infinity` |
| Special values | `NaN`, `Infinity`, `+Infinity`, `-Infinity` (case-insensitive) |

- Floats are written in shortest round-trip form, and integral values keep a `.0` suffix
  (`3.0`) so they read back as floats rather than integers.
- `-0.0` preserves its sign bit.
- Hex literals wider than 64 bits (for example `0x1_0000_0000_0000_0000`) remain strings
  instead of wrapping around.

### 4.6 Booleans and null

```cif
debug   = true
enabled = false
nothing = null
```

- `true`, `false`, and `null` are case-insensitive.
- Only these literals become booleans/null during parsing. `yes`, `on`, and `1` remain strings
  in the text but are accepted as `true` by the lenient `GetBoolean` conversion.

### 4.7 Arrays

```cif
empty  = []
frame  = [0xAA, 0x55]
tags   = [com, "tcp", 3]
matrix = [[1, 2], [3, 4]]
items  = [
    { name = A, value = 1 },
    { name = B, value = 2 },   # trailing comma is allowed
]
```

- Elements can be any value: scalars, arrays, or objects.
- Elements must be separated by `,`; a trailing comma is allowed.
- Newlines and comments are allowed inside brackets.
- Writing: arrays that fit within 100 characters are written inline; otherwise they become
  one element per line with a trailing comma.

### 4.8 Groups: sections and blocks

```cif
[serial]
port = COM3

[network.tcp]
mode = Client

device {
    name = sensor-01
    option {
        rate = 10
    }
}
```

- `[a.b.c]` is a **top-level** section header; it creates nested objects `a → b → c`.
  A header cannot span lines.
- Headers do **not** support quotes: `["a.b"]` reports a clear error (otherwise it would
  create bizarre keys such as `"a` / `b"`). For a literal key containing `.`, use the
  quoted key/value form: `"a.b" = 1`.
- `key { ... }` is a block object, allowed at the top level or inside another block.
- `key = { a = 1, b = 2 }` is an inline object (single line).
- An empty object is written as `key = {}`.
- The same object may be reopened by multiple headers/blocks; contents are merged.
- `[section]` is **not** allowed inside a `{ ... }` block (a clear error is reported);
  use `key { ... }` inside blocks.
- The writer always emits block objects, never `[section]` headers.
  `ToText → Parse → ToText` is idempotent.

### 4.9 Dotted keys and paths

A dot in an unquoted key expands into nested objects (same as a section header):

```cif
serial.port = COM3        # same as [serial] / port = COM3
```

Quote the key to keep the dot literal:

```cif
"a.b" = 1                 # literal key "a.b"; no nested object is created
```

Two families of accessors:

| Family | API | Behavior |
| --- | --- | --- |
| Literal key | `Get` / `TryGetValue` / `Set` / `Add` / `ContainsKey` / `Remove` / indexer | Exact key match; dots are not split |
| Dotted path | `GetPath` / `TryGetPath` / `SetPath` / `GetObject` / `GetArray` | Navigates by `.`; the last three create missing objects |
| Lenient reads | `GetString` / `GetInt32` / … | Literal key first; falls back to a dotted path only when the key contains `.` |

- If a path segment exists but is neither an object/array nor `null`, `GetObject` / `GetArray` /
  `SetPath` throw `InvalidOperationException` instead of silently overwriting data. A `null`
  value is replaced by the object/array.
- Nesting is capped at **256 levels** (arrays, objects, dotted paths, and headers combined).
  Exceeding the limit throws `ConfigFormatException` while parsing or `InvalidOperationException`
  while writing — never a stack overflow.
- Fields whose names contain `.` cannot be reached through path APIs; use the literal-key APIs.

### 4.10 Complete example

```cif
# ============================================================
# Inspector configuration
# ============================================================
app {
    name = "Inspector"
    version = "V1.0"
    theme = "Light"
}

serial {
    port = "COM7"
    baudRate = 115200
    dataBits = 8
    parity = "None"
    frameHead = [170, 85]
    options = { rts = true, dtr = false }   # inline object
}

network {
    tcp {
        mode = "Client"
        remoteIp = 127.0.0.1
        remotePort = 0x1F90    # 8080
    }
}

send {
    asText = true
    interval = 1000
}

"literal.with.dot" = 1     # literal key
```

Writing the same document (`includeHeader: false`) produces:

```cif
app {
    name = "Inspector"
    version = "V1.0"
    theme = "Light"
}
serial {
    port = "COM7"
    baudRate = 115200
    dataBits = 8
    parity = "None"
    frameHead = [170, 85]
    options {
        rts = true
        dtr = false
    }
}
network {
    tcp {
        mode = "Client"
        remoteIp = "127.0.0.1"
        remotePort = 8080
    }
}
send {
    asText = true
    interval = 1000
}
"literal.with.dot" = 1
```

> Inline objects (`key = { ... }` in the input) are expanded into block objects when written;
> only an empty object stays as `key = {}`. Objects inside arrays are still written inline.

### 4.11 Syntax cheat sheet

| Syntax | Example | Notes |
| --- | --- | --- |
| Key/value | `a = 1` | Value must start on the same line |
| Block object | `a { b = 1 }` | Multi-line and nesting allowed |
| Inline object | `a = { b = 1 }` | Used as a value |
| Section header | `[a.b]` | Top level only |
| Array | `[1, "x", { a = 1 }]` | Comma-separated; trailing comma allowed |
| Comment | `# ...`, `// ...` | At line start or after whitespace |
| Double-quoted string | `"a\nb"` | Escapes supported |
| Single-quoted string | `'C:\dir'` | Literal; `''` means `'` |
| Null | `null` | Case-insensitive |
| Bare value terminators | newline, `,`, `]`, `}` | Cannot start with `#` or `//` |
| Path separator | `a.b.c` | Quoted keys are not expanded |
| Pair separator | `=` | `:` produces a helpful error |

## 5. API Reference

Namespace: `ConfigInfo`. All types are `public` and require no reflection.

### 5.1 ConfigDocument

| Member | Description |
| --- | --- |
| `ConfigObject Root { get; }` | Root object; never `null` |
| `ConfigDocument()` | Empty document |
| `ConfigDocument(ConfigObject root)` | Wraps an existing root by reference (no copy) |
| `static ConfigDocument Parse(string text)` | Parses text; throws `ConfigFormatException` |
| `static ConfigDocument Parse(ReadOnlySpan<char> text)` | Span overload; avoids an extra string allocation |
| `static bool TryParse(string? text, out ConfigDocument?, out ConfigFormatException?)` | Non-throwing parse that keeps line/column |
| `static ConfigDocument Load(string path)` | Reads and parses a file; BOM-aware |
| `static bool TryLoad(string path, out ConfigDocument?, out string? error)` | Non-throwing load |
| `string ToText(bool includeHeader = true)` | Produces text; header comment included by default |
| `string ToString()` | Same as `ToText()` |
| `void Save(string path, bool atomic = true, bool includeHeader = true)` | UTF-8 without BOM |
| `bool TrySave(string path, out string? error, bool atomic = true, bool includeHeader = true)` | Non-throwing save |

`Save` behavior:

1. Creates the target directory when needed.
2. `atomic: true` (default): writes a unique temp file `name.<guid>.tmp` in the same directory,
   then replaces the target. On failure the temp file is removed and the original stays intact.
3. `atomic: false`: writes directly; a failure may leave a partial file.
4. No `fsync`: guarantees “no half-written file”, not power-loss durability.
5. Atomic replacement creates a new file; the original ACL/permissions (for example a read-only
   marker) are not guaranteed to be preserved, and a read-only target fails.

### 5.2 ConfigObject

Ordered key/value object; insertion order is preserved and keys are case-sensitive.

| Member | Description |
| --- | --- |
| `int Count` | Number of entries |
| `ConfigValue this[string key]` | Literal key; getter throws `KeyNotFoundException` |
| `ConfigValue Get(string key)` | Literal key; throws `KeyNotFoundException` |
| `bool TryGetValue(string key, out ConfigValue value)` | Literal key; does not throw |
| `void Set(string key, ConfigValue value)` | Overwrites in place, or appends |
| `void Add(string key, ConfigValue value)` | Throws `ArgumentException` if the key exists |
| `bool ContainsKey(string key)` | Literal key lookup |
| `string? Comment` / `string? TrailingComment` | Container leading / trailing comment (multi-line uses `\n`) |
| `string? GetComment(string key)` | Leading comment of an entry; `null` if missing |
| `void SetComment(string key, string? comment)` | Sets the leading comment; the key must exist |
| `string? GetInlineComment(string key)` | Inline comment of an entry |
| `void SetInlineComment(string key, string? comment)` | Sets the inline comment; multi-line text is collapsed |
| `bool Remove(string key)` | Removes and returns success |
| `void Clear()` | Removes all entries |
| `string GetString(string key, string defaultValue = "")` | Lenient; missing/null/unconvertible → default |
| `bool GetBoolean(...)` / `int GetInt32(...)` / `long GetInt64(...)` | Same lenient rule |
| `double GetDouble(...)` / `float GetSingle(...)` / `decimal GetDecimal(...)` | Same lenient rule |
| `ConfigValue GetPath(string path)` | Dotted path; throws `KeyNotFoundException` |
| `bool TryGetPath(string path, out ConfigValue value)` | Dotted path; does not throw |
| `void SetPath(string path, ConfigValue value)` | Dotted path write; creates intermediate objects |
| `ConfigObject GetObject(string path)` | Gets or creates an object along a path |
| `ConfigArray GetArray(string path)` | Gets or creates an array along a path |
| `Enumerator GetEnumerator()` | Ordered enumeration; allocation-free `foreach` |

Lenient `Get*` methods accept either a literal key or a dotted path (see [4.9](#49-dotted-keys-and-paths)).

Modifying the object during enumeration (`Set`/`Add`/`Remove`/`Clear`, including changing an
existing value) throws `InvalidOperationException`.

### 5.3 ConfigArray

Ordered array implementing `IList<ConfigValue>` and `IReadOnlyList<ConfigValue>`.

| Member | Description |
| --- | --- |
| `Count` / `IsReadOnly` | `IsReadOnly` is always `false` |
| `ConfigValue this[int index]` | Indexer |
| `Add` / `AddRange` / `Insert` / `Remove` / `RemoveAt` / `Clear` | Standard collection operations |
| `Contains` / `IndexOf` / `CopyTo` | Uses `ConfigValue` equality (arrays/objects by reference) |
| `GetInt32` / `GetInt64` / `GetDouble` / `GetSingle` / `GetDecimal` | Out-of-range or unconvertible → default |
| `GetBoolean` / `GetString` | Same |
| `ToInt32Array` / `ToInt64Array` / `ToDoubleArray` / `ToStringArray` | Batch conversion; unconvertible elements use `0` / `""` |
| `List<ConfigValue>.Enumerator GetEnumerator()` | Allocation-free; modification during enumeration throws |

### 5.4 ConfigValue

A `readonly struct` representing any configuration value without boxing.

```csharp
ConfigValue a = 123;                 // Integer
ConfigValue b = 1.5;                 // Float (double)
ConfigValue c = true;                // Boolean
ConfigValue d = "text";              // String
ConfigValue e = (string?)null;       // Null (explicit string? conversion)
ConfigValue f = ConfigValue.Null;    // Null singleton
ConfigValue g = new ConfigObject();  // Object
ConfigValue h = new ConfigArray();   // Array
```

> A `null` literal cannot be assigned directly to `ConfigValue` (CS0037: the target is a
> non-nullable value type). Use `ConfigValue.Null` or `(string?)null`.

| Member | Description |
| --- | --- |
| `Kind` | `Null/Boolean/Integer/Float/String/Array/Object` |
| `IsNull` / `IsScalar` | Scalar = null/boolean/integer/float/string |
| `IsBoolean` / `IsInteger` / `IsFloat` / `IsString` / `IsArray` / `IsObject` | Exact type checks |
| `AsBoolean` / `AsInt64` / `AsDouble` / `AsDecimal` / `AsString` | Lenient conversion; `InvalidCastException` on failure |
| `AsInt32` / `AsInt16` / `AsSByte` / `AsByte` / `AsUInt16` / `AsUInt32` | `AsInt64` then checked narrowing; `OverflowException` on overflow |
| `AsUInt64` | 64-bit two's-complement reinterpretation (`-1` → `ulong.MaxValue`) |
| `AsSingle` | `(float)AsDouble()`; out of range becomes `±Infinity` |
| `AsArray` / `AsObject` | Only when the kind matches; otherwise `InvalidCastException` |
| `ToString` | Debug text; nesting deeper than 32 levels is truncated to `…` |

Equality:

- Kinds must match. Integer/boolean/float compare **bit patterns** (`1` ≠ `1.0`; two `NaN`s
  with the same bits are equal). Strings use `Ordinal`. Arrays/objects compare by reference.
- `GetHashCode` is consistent with `Equals`.

Storage conventions:

- `decimal` is stored as `double`; precision may be lost beyond ~15–17 significant digits.
  Use strings when exact decimals matter.
- `ulong` is stored as a two's-complement bit pattern; values above `long.MaxValue` read back
  negative through `AsInt64()`; use `AsUInt64()`.

### 5.5 ConfigValueKind

```csharp
public enum ConfigValueKind : byte
{
    Null = 0, Boolean, Integer, Float, String, Array, Object,
}
```

The order is part of the `IsScalar` rule (`<= String`); do not rely on it externally.

### 5.6 ConfigFormatException

| Member | Description |
| --- | --- |
| `int Line` | 1-based line number |
| `int Column` | 1-based column number |
| `Message` | Reason plus `（第 X 行，第 Y 列）` |

> Error messages are currently in Chinese. `Line` and `Column` are language-neutral and can be
> localized in your UI.

### 5.7 ConfigFile (auto mapping)

A reflection convenience layer on top of the document API, annotated with
`RequiresUnreferencedCode` / `RequiresDynamicCode`.

| Member | Description |
| --- | --- |
| `ConfigObject ToObject<T>(T value, bool includeModelComments = true)` | Object → root object |
| `string ToText<T>(T value, bool includeHeader = true, bool includeModelComments = true)` | Object → text |
| `void Save<T>(string path, T value, bool atomic = true, bool includeHeader = true, bool includeModelComments = true)` | Object → file |
| `bool TrySave<T>(string path, T value, out string? error, bool atomic = true, bool includeHeader = true, bool includeModelComments = true)` | Non-throwing save |
| `T Load<T>(string path) where T : new()` | File → object |
| `bool TryLoad<T>(string path, out T? value, out string? error)` | Non-throwing load |
| `T LoadOrDefault<T>(string path, out string? error)` | Returns `new T()` on failure; `error` explains why |
| `T Parse<T>(string text) where T : new()` | Text → object |
| `T FromObject<T>(ConfigObject root) where T : new()` | Root object → object |
| `void LoadInto<T>(ConfigObject root, T target) where T : class` | Populate an existing instance |

`Load` / `Parse` / `ToObject` throw the original mapping exception (user getter/setter exceptions
are unwrapped); `TryLoad` / `TrySave` / `LoadOrDefault` never throw and return the reason as text.

### 5.8 Mapping attributes

| Attribute | Target | Description |
| --- | --- | --- |
| `[ConfigName("key")]` | Field / property | Overrides the key; a dotted name is written/read as a path |
| `[ConfigIgnore]` | Field / property | Excluded from auto mapping |
| `[Description("...")]` | Field / property / type | BCL attribute; auto comments prefer it over XML `<summary>` |

Within one type, dotted keys must not be a prefix of another member key (for example `serial`
and `serial.port`), otherwise writes overwrite each other; validation fails early.

## 6. Conversion Rules

### 6.1 Conversion matrix

| Source \ Target | bool | integer | double | decimal | string |
| --- | --- | --- | --- | --- | --- |
| `null` | default | default | default | default | default |
| bool | ✔ | `1` / `0` | `1` / `0` | `1` / `0` | `"true"` / `"false"` |
| integer | `!= 0` | ✔ | ✔ (large values may lose precision) | ✔ | invariant decimal text |
| float | `!= 0` (`NaN` → `true`) | truncates toward zero within `long` range | ✔ | must be strictly inside `decimal` range | shortest round-trip text |
| string | `true/yes/on/1`, `false/no/off/0` | decimal or `0x/0b/0o`, `_` allowed | invariant, surrounding whitespace allowed | invariant | ✔ |
| array / object | ✘ | ✘ | ✘ | ✘ | ✘ |

- ✘ means unconvertible: `Get*` returns the caller's default; `As*` throws `InvalidCastException`.
- Float → integer uses strict bounds: `2^63` is rejected instead of silently becoming `long.MinValue`.
- Float → decimal is strict at `±2^96`, so boundary values return the default instead of throwing.
- `null` read as a string returns the default value (`null` means “no value”, not an empty string).
- Numeric/boolean → string uses `InvariantCulture` and is independent of the system locale.

### 6.2 Integer text

The same rules apply to `TryParseInteger`, `GetInt32`, and `GetInt64`:

```text
[+|-] digits
[+|-] 0x hexdigits
[+|-] 0b bindigits
[+|-] 0o octdigits
```

- `_` is allowed only between two digits.
- Decimal values outside `long` fail to parse and remain strings.
- Hex/binary/octal use a 64-bit bit pattern; literals wider than 64 bits fail.
- The minimum negated hex literal is `-0x8000_0000_0000_0000`.

### 6.3 Float formatting

- Written with .NET `"R"` (shortest round-trip) and `InvariantCulture`.
- If the result has no `.`, `E`, or `e`, `.0` is appended so the value reads back as `Float`.
- `NaN`, `Infinity`, and `-Infinity` are written verbatim and read back correctly.
- Inference order: `null` → `true/false` → `NaN`/`±Infinity` → integer → float → string.

### 6.4 Lenient vs strict reads

```csharp
// Lenient: missing, null, unconvertible, or out of range → default
int port = root.GetInt32("serial.port", 9600);

// Strict: InvalidCastException when unconvertible; OverflowException when narrowing overflows
int strict = root.Get("serial.port").AsInt32();
```

## 7. File I/O and Atomic Save

```csharp
ConfigDocument doc = ConfigDocument.Load("config/app.cif");

if (!ConfigDocument.TryLoad("config/app.cif", out ConfigDocument? loaded, out string? error))
    Console.WriteLine($"load failed: {error}");

doc.Save("config/app.cif");                       // atomic by default
doc.Save("config/app.cif", includeHeader: false); // no header comment
doc.Save("config/app.cif", atomic: false);        // direct overwrite
```

`Load` / `Parse` read comments too: `Root.Comment` for the file-level block and
`GetComment` / `GetInlineComment` for entries; saving writes them back (see [4.2](#42-comments)).

Guarantees and boundaries of atomic save:

- The target is replaced as a whole within the same directory; readers see either the old or
  the new content, never a half-written file.
- Temp names are unique, so concurrent saves of the same path do not clash; the last writer wins.
- A failed write/replace removes the temp file and leaves the original untouched.
- No `fsync`-level power-loss guarantee.
- After an atomic replace, the original ACL/read-only attribute is not guaranteed to survive;
  a read-only target fails.

## 8. Error Handling

Parse errors throw `ConfigFormatException`:

```csharp
try
{
    ConfigDocument.Parse(text);
}
catch (ConfigFormatException ex)
{
    Console.WriteLine($"line {ex.Line}, column {ex.Column}: {ex.Message}");
}
```

Non-throwing APIs:

| API | On failure |
| --- | --- |
| `ConfigDocument.TryParse` | Returns `false`; the `out ConfigFormatException` keeps line/column |
| `ConfigDocument.TryLoad` | Returns `false`; the `out string` carries the reason |
| `ConfigDocument.TrySave` | Returns `false`; the `out string` carries the reason |
| `ConfigObject.TryGetValue` / `TryGetPath` | Returns `false`; the `out ConfigValue` is `default` (`Null`) |

Common errors:

| Input | Error |
| --- | --- |
| `a = [1, 2` | array is missing `]` |
| `a =` at end of line | missing value |
| key followed by a line without `=` | key is missing `=` or `{` |
| `a: 1` | use `=` instead of `:` |
| `["a.b"]` | section headers do not support quotes |
| `[section]` inside a block | section headers are top-level only |
| `a = "unterminated` | string is missing its closing quote |
| nesting deeper than 256 | nesting limit exceeded |

## 9. Performance and Memory

- **Single pass**: `ConfigParser` is a `ref struct` scanning a `ReadOnlySpan<char>`; no token list.
- **Zero-reflection core**: the parser/writer use no `System.Reflection`, `dynamic`, or serializer.
- **Low-allocation reads**: struct enumerators for `ConfigObject` and `ConfigArray`.
- **Lookup index**: `ConfigObject` builds a `Dictionary<string,int>` once it holds more than 12
  entries; key lookup becomes O(1).
- **Writing**: pre-sized `StringBuilder`; short arrays inline, long arrays multi-line.
- **Error positions**: the line scan only runs when an error occurs.

`samples/ConfigInfo.Demo` includes a benchmark (200 sections × 10 keys ≈ 2000 entries).
Both `Parse` and `ToText` are O(n).

## 10. NativeAOT and Trimming

```xml
<IsAotCompatible>true</IsAotCompatible>
<IsTrimmable>true</IsTrimmable>
```

- The zero-reflection core (`ConfigDocument` / `ConfigObject` / `ConfigArray` / `ConfigValue`) uses no
  reflection, `dynamic`, `JsonSerializer`, source generators, or serialization attributes.
- Safe for NativeAOT and IL trimming; all conversions are hand-written.
- The `ConfigFile` mapper is a reflection convenience layer annotated with
  `RequiresUnreferencedCode` / `RequiresDynamicCode`; hand-write `SaveTo` / `LoadFrom` for AOT
  (see 3.4).

Cross-platform:

- Only portable BCL APIs are used (`File` / `Path` / `UTF8Encoding`); there is no `P/Invoke`,
  registry, Win32, or `System.Windows` dependency. Numbers always use `InvariantCulture`,
  output always uses `LF`, and files are always UTF-8 without BOM, so the same configuration
  text has identical semantics on every OS.
- Verified: `dotnet build ConfigInfo.sln` finishes with 0 warnings and 0 errors, and the sample
  self-test passes **76/76**.

NativeAOT requires the C++ toolchain of the target platform (the MSVC linker on Windows).
If `PublishAot=true` stops at the native link step with `Platform linker not found`, install
the “Desktop development with C++” workload in Visual Studio and retry; the AOT/trim
analyzers themselves already report 0 warnings.

> The sample project suppresses IL2026 / IL3050 to demonstrate the auto mapper. The library
> still enables `IsAotCompatible`, and its core APIs remain reflection-free.

Try a NativeAOT publish:

```powershell
dotnet publish samples/ConfigInfo.Demo -c Release -r win-x64 -p:PublishAot=true
```

## 11. Thread Safety

- `ConfigDocument`, `ConfigObject`, `ConfigArray`, and `ConfigValue` are **not thread-safe**.
- Concurrent reads are safe only when no thread is writing (the object index is rebuilt only on writes).
- Concurrent reads and writes require external locking or one document per thread.
- Concurrent saves of the same path are protected against temp-file collisions; the last writer wins.
- Modification during enumeration throws `InvalidOperationException`.

## 12. Known Limitations

| Limitation | Notes / Workaround |
| --- | --- |
| No schema/validation | Validate in your mapping layer |
| Comments inside arrays and layout are not preserved | Entry comments are preserved; comments inside array elements, blank lines, and indentation are not |
| `[section]` is top-level only | Use `key { ... }` inside blocks |
| No `:` separator | Always use `=` |
| Quoted strings cannot span lines | Use `\n` escapes |
| Bare values cannot start with `#` / `//` | Quote them, for example `"#FF0000"` |
| Bare values cannot contain `,` `]` `}` | Quote them |
| One scalar pair per line | A bare value consumes the rest of its line (spaces and `=` are allowed) |
| Case-sensitive keys | Normalize in the business layer if needed |
| Nesting cap of 256 levels | Applies to parsing and writing; `ToString` truncates at 32 |
| `decimal` precision | Stored as `double` (~15–17 significant digits) |
| Auto-mapping member scope | Public instance fields and public read/write properties only; read-only fields, read-only properties, indexers, dictionaries, and multi-dimensional arrays are skipped |
| Auto-mapping constructors | Models and nested element types need a public parameterless constructor; provide defaults in field/property initializers |
| Auto-mapping type scope | No `DateTime` / `TimeSpan` / `Guid`; use strings or hand-written mapping |
| Auto-mapping under AOT | `ConfigFile` uses reflection; hand-write `SaveTo` / `LoadFrom` for NativeAOT / trimming |
| Auto-mapping read type | `Load<T>` / `FromObject<T>` read by the supplied `T`; specify the derived type to read derived members |
| `[ConfigName]` paths | Dotted keys must not be a prefix of another member key in the same type (`serial` plus `serial.port`) |
| `ulong` high bit | Stored as a two's-complement pattern; read with `AsUInt64()` |
| Decimal integer overflow | Out-of-range literals stay strings rather than becoming floats |
| UTF-16 without BOM | Not auto-detected; save as UTF-8 or with a BOM |
| Large files | `Load` reads the whole file into memory |
| Error message language | Currently Chinese; line/column are language-neutral |

## 13. Build, Test, and Samples

```powershell
dotnet build ConfigInfo.sln -c Debug
dotnet test tests/ConfigInfo.Tests/ConfigInfo.Tests.csproj
dotnet run --project samples/ConfigInfo.Demo
dotnet run --project samples/ConfigInfo.Demo -- --selftest
```

The test suite contains **112** xUnit cases; the sample self-test runs **76** assertions and
returns exit code `0` when everything passes.

If only a .NET 9/10 runtime is installed, set `$env:DOTNET_ROLL_FORWARD = 'LatestMajor'`
before running the tests or the sample.

## 14. Audit and Fix Log

Added in version 1.2.0 (purely additive; the `.cif` format is unchanged):

| Change | Details |
| --- | --- |
| Auto mapping | `ConfigFile.Save` / `Load<T>` / `TryLoad` / `LoadOrDefault` / `Parse` / `FromObject` / `LoadInto` |
| Comments | Entry leading / inline comments and container leading / trailing comments; preserved by `Load → Save`, standard header de-duplicated |
| Auto comments | `[Description]` / XML `<summary>` written above entries; type description becomes the file header |
| Mapping attributes | `[ConfigName]` (including dotted paths), `[ConfigIgnore]` |
| Mapping rules | Nested classes, arrays / `List<T>`, enums, nullable value types, case-insensitive fallback, defaults for missing keys |
| Safety bounds | Circular-reference detection, 256-level nesting cap, path-key conflict detection, nullable elements/nullable struct roots, no root-level derived-member loss, unwrapped user getter/setter exceptions |

Fixes delivered by the full audit (version 1.1.0):

| # | Issue | Impact | Fix |
| --- | --- | --- | --- |
| 1 | `double → long` compared with `<= (double)long.MaxValue` | `2^63` was silently truncated to `long.MinValue` | Strict less-than; boundary is now unconvertible |
| 2 | `double → decimal` compared with `<= (double)decimal.MaxValue` | Boundary value threw `OverflowException` | Strict bounds; returns default / `InvalidCastException` |
| 3 | Writer did not quote keys containing `.` | Re-parsing turned the key into nested objects | Added `.` and `:` to `NeedsQuote` |
| 4 | Parser did not recognize `NaN` / `Infinity` | Special floats became strings after a round-trip | Explicit `NaN` / `±Infinity` inference |
| 5 | No nesting depth limit | Deeply nested input caused `StackOverflowException` | 256-level limit on parse and write |
| 6 | `a =` + comment + next-line key/value | The next line was swallowed as the value | Values must start on the `=` line; arrays/objects still allow multi-line |
| 7 | BOM-prefixed strings in `Parse` | BOM became part of the first key | Parser skips a leading `\uFEFF` |
| 8 | CR-only line endings | All errors reported line 1 | Line/column handles LF, CRLF, and CR |
| 9 | Loose underscore rules | `1_`, `1__0`, `0x_1F` were accepted as integers | Underscores must sit between two digits |
| 10 | No overflow check in the radix accumulator | Literals wider than 64 bits wrapped silently | Too-wide literals fail and stay strings |
| 11 | `null` read as `""` | Inconsistent with “missing returns default” | `null` is unconvertible; default is returned |
| 12 | No guard for mutation during enumeration | Enumeration could skip or repeat entries | Version check throws `InvalidOperationException` |
| 13 | Fixed `.tmp` name for atomic save | Concurrent saves clobbered each other | Unique temp name plus failure cleanup |
| 14 | No non-throwing parse API | Callers had to catch and lost line/column | Added `TryParse` preserving `ConfigFormatException` |
| 15 | Lone surrogates written verbatim | Produced non-round-trippable text | Surrogate pairs verbatim; lone surrogates as `\uXXXX` |
| 16 | Missing `ConfigArray.GetSingle/GetDecimal` | API asymmetry with `ConfigObject` | Added |
| 17 | `(byte)int` truncation in the sample | Frame-head values above 255 wrapped silently | Uses `Math.Clamp` |
| 18 | Unclear error messages | `a: 1` and block-level `[section]` were confusing | Added targeted diagnostics |
| 19 | Quotes inside a section header were split as ordinary characters | `["a.b"]` produced bizarre keys such as `"a` / `b"` without any error | Headers now detect quotes and report a clear error |

Verification: **112** xUnit tests, **76** sample self-test assertions, build with 0 warnings and
0 errors.

## 15. Version and Compatibility

| Item | Value |
| --- | --- |
| Library version | 1.2.0 (adds the auto mapper; backward compatible) |
| Target framework | `net8.0` |
| Supported platforms | Windows / Linux / macOS (any platform supported by .NET 8; no OS-specific APIs) |
| Language version | `latest` (file-scoped namespaces, collection expressions, …) |
| File format | `.cif` 1.0 (backward compatible; this release only fixes defects) |
| Dependencies | None |
| Nullable reference types | Enabled |

Format compatibility notes:

- 1.2.0 is purely additive: it adds `ConfigFile`, two attributes, and comment read/write; the
  core API and `.cif` syntax are unchanged.
- Auto-mapping uses a lowercase-first-letter key by default; use `[ConfigName("...")]` to keep
  existing key names when migrating.
- Comments now participate in `Load → Save`; earlier versions dropped them.
- Auto comments are new in 1.2.0 and can be disabled with `includeModelComments: false`;
  the `.cif` format is unchanged.

- `1_` and hexadecimal literals wider than 64 bits changed from “accepted as numbers” to
  “kept as strings”. This is a **correction**, not a syntax extension.
- Bare keys containing `.` still expand into nested objects when read from text; the writer now
  quotes literal keys that contain `.`, so `Set("a.b", v)` round-trips as a *literal* key.
  Use `SetPath("a.b", v)` when you want nesting.
- Values written on the line after `a =` used to be tolerated silently; they now raise
  “missing value”, which prevents the next line from being swallowed.
- Quotes inside a section header (for example `["a.b"]`) used to be split silently into the
  keys `"a` / `b"`; they now raise an error. Use `"a.b" = 1` for a literal key with a dot.

---

**License / 许可证**: Not declared in this repository.  
**Version / 版本**: 1.2.0 · **Target / 目标框架**: `net8.0`
