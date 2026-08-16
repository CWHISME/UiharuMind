# AGENTS.md

本文件是「在这个仓库里怎么干活」。术语表在 [docs/CONTEXT.md](docs/CONTEXT.md)。

实现要点：

1. 优先复用已有代码或组件，避免重复造轮子。
2. 如果同类用法可以提成公共组件，则可以将其封装，尽量减少重复代码。
3. 优先考虑性能、可维护性。

高优先级：

1. 在修改代码时，尤其避免一个文件堆砌大量代码，可适当拆分或应用合适的设计模式(如模板类、方法)。
2. 如果发现代码不符合设计原则(包括但不限于九大原则)的问题，应当优先向用户主动提出更符合设计模式(包括但不限于23种经典设计模式)的重构方案，而非直接在屎山代码上做迭代。
3. 在实现需求时应当反思：应该使用继承还是组合？使用接口还是抽象类？在引入设计模式提高扩展性的同时，如何避免带来可读性降低问题？

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

可复用：

各种样式：UiharuMind/Assets/Themes

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
