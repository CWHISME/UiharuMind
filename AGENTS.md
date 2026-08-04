# AGENTS.md

本文件是「在这个仓库里怎么干活」。术语表在 [docs/CONTEXT.md](docs/CONTEXT.md)。

## 仓库概览

Avalonia 12 桌面应用，.NET 10。本地跑 GGUF 模型（llama.cpp）+ 远程模型，含角色扮演对话、
工作区 agent、截图 OCR、剪贴板历史、知识库检索等。产品功能见 [README.md](README.md)。

解决方案 `UiharuMind/UiharuMind.sln` 下八个项目：

| 项目 | 是什么 |
|---|---|
| `UiharuMind.Core` | 领域与基础设施。无 UI 依赖，是全仓的重心 |
| `UiharuMind` | Avalonia UI 层（下称 **App 项目**） |
| `UiharuMind.Desktop` | 桌面入口（实际运行的就是它） |
| `UiharuMind.CLI` | 命令行入口 |
| `UiharuMind.Android` | Android 入口，**需额外 workload，本机通常构建不了** |
| `UiharuMind.Browser` | WASM 入口，**需额外 workload，本机通常构建不了** |
| `UiharuMind.Core.Tests` | Core 的测试 |
| `UiharuMind.App.Tests` | App 项目的测试（只测不碰 UI 线程/渲染的纯逻辑） |

## 路径地图

有四层同名目录，写脚本时**用绝对路径，别数相对层数**——这里踩过坑。

```
Studys/UiharuMind/                     本地工作壳（不在 git 内，放草稿与计划文档）
└── UiharuMind/                        ← git 仓库根，本文件在这里
    ├── AGENTS.md  CLAUDE.md(→AGENTS.md)  .editorconfig
    ├── README.md  LICENSE  Images/
    ├── docs/CONTEXT.md                术语表
    └── UiharuMind/                    ← 解决方案目录
        ├── UiharuMind.sln
        ├── UiharuMind/                ← App 项目（第四层同名）
        ├── UiharuMind.Core/
        ├── UiharuMind.Core.Tests/
        ├── UiharuMind.App.Tests/
        └── UiharuMind.Desktop/  .CLI/  .Android/  .Browser/
```

## 构建与测试

在解决方案目录 `UiharuMind/` 下执行：

```bash
dotnet build UiharuMind.Core/UiharuMind.Core.csproj
dotnet build UiharuMind/UiharuMind.csproj              # App 项目，axaml 错误在这里暴露
dotnet build UiharuMind.Desktop/UiharuMind.Desktop.csproj
dotnet test  UiharuMind.Core.Tests/UiharuMind.Core.Tests.csproj
dotnet test  UiharuMind.App.Tests/UiharuMind.App.Tests.csproj
```

**`dotnet build UiharuMind.sln` 必然报两个 `NETSDK1147` 错误**（Android/Browser 缺
`wasm-tools-net8`、`android` workload）。那与你的改动**无关**，不要试图修它，也不要因此
以为自己改坏了。要验证全仓，逐个构建上面五个项目。

axaml 的命名空间与 `x:Class` 错误在编译期就会炸（`AVLN2000`），所以对结构性改动，
「App 项目编译通过」是很强的信号。

## 不可破坏的约束

`UiharuMind.Core.csproj` 给 Agent Framework 相关包打了 `PrivateAssets=compile`，
**UI 层若直接引用其类型会变成编译错误**，迫使一切经 `ICharacterRunner` 收口。
这是本仓最重要的架构约束。

遇到「UI 层找不到某个 Agent Framework 类型」时，**正确做法是把需求收进 `ICharacterRunner`**，
而不是给 App 项目加包引用——那会当场拆掉这条缝。

注意 `PrivateAssets` 必须是 `compile` 而非 `all`：`all` 会连运行时资产一起挡掉，
导致这些包及其传递依赖不进入 app 输出目录，运行时抛 `FileNotFoundException`。

## 结构约定

**命名空间与目录严格对齐**（唯一例外是只放程序集级 attribute 的 `XmlnsDefinition.cs`）。
改目录就等于改命名空间，两边必须一起动。

App 项目按概念切片：

```
UiharuMind/UiharuMind/
├── Features/<Feature>/    一个用户能说出名字的功能：View + ViewModel + 其专属控件与窗口同处一目录
└── Shared/                被两个以上 feature 引用的东西，内部再按类型分
    └── Controls/ Converters/ Windows/ Services/ Utils/ Shell/ Data/ Markup/ UIHolder/ Interfaces/
```

三条硬规则：

1. **归属按「谁在引用它」判定，不按「它叫什么」。** 名字会骗人——`ModelSelectComboBoxView`
   带 Model 字样，但只有会话页在用，所以它属于 `Conversation` 而不是 `Models`。
2. **只被一个 feature 用的东西归那个 feature，不进 `Shared/`。** `Shared/` 一旦平铺就又变回抽屉。
3. **禁止再出现 `ViewData`、`Common`、`Others` 这类不承诺任何东西的目录名。** 名字不能回答
   「新文件该放哪」，目录就会越塞越满。

另有一个 C# 陷阱：**feature 名不要和常用类型撞车**。命名空间优先于 `using` 导入的类型，
所以 feature 叫 `Log` 会让 `Log.Debug(...)` 解析成命名空间而非日志类（因此现在叫 `LogViewer`）。
成员访问形式的 `X.Y`（属性/字段）不受影响。

## 代码规范

正确使用注释：注释精简、无冗余注释，简单代码可忽略，必要代码才进行合理注释。

对于项目现有不符合规范的代码，要求可以忽略，但是新增请按照本规范进行。

### 命名规范

> [规则1-3] ~ [规则1-7]（下划线命名法、大驼峰/小驼峰、`I` 前缀、`E` 前缀）已由
> [.editorconfig](.editorconfig) 机械强制，严重性为 warning。编号保留空洞是为了不让你
> 以往引用过的编号失效。

[规则1-1] 英文单词命名。禁止使用拼音或无意义的字母命名。

[规则1-2] 直观易懂。使用能够描述其功能或有意义的英文单词或词组。

### 编码规范

[规则2-1] 声明变量时，一行只声明一个变量。

[规则2-2] 类的字段声明统一放置于类的最前端。

```csharp
public class Student
{
    private string _firstName;
    private string _lastName;

    public string GetFirstName()
    {
        return _firstName;
    }
}
```

### 注释规范

[规则3-1] 公共方法注释，采用 `///` 形式自动产生 XML 标签格式的注释，包括方法介绍、参数含义、
返回内容。私有方法可以不用注释。

```csharp
/// <summary>
/// 设置场景名称
/// </summary>
/// <param name="sceneName">场景名</param>
/// <returns>如果设置成功返回True</returns>
public bool SetSceneName(string sceneName)
{
}
```

[规则3-2] 公共字段注释，采用 `///` 形式。私有字段可以不用注释。

[规则3-3] 私有字段注释，注释位于代码后面，中间 Space 键隔开。

```csharp
private string _firstName; //姓氏
```

[规则3-4] 方法内的代码块注释。

```csharp
public void UpdateHost()
{
    // 和服务器通信
    ...

    // 检测通信结果
    ...
}
```

## 协作口径

- **提交信息只写一句话。** 不要正文、不要 `Co-Authored-By`、不要任何附加尾注。
  沿用仓内风格 `type(scope): 一句中文说明`。
- **实机测试由用户做。** 改动做到编译通过 + 测试全绿，然后给出手测清单；不要自动启动 app。
- 提交或推送只在用户要求时做。
- 个人项目口径：**不清算单例、不换框架**。`UIManager`、`SettingConfig`、`LlmManager` 这类
  全局静态是既定选择，不要为了「解耦」给它们逐个套接口——只在真的会有第二个适配器时才开缝。
