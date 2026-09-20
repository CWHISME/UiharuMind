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
工作区代理、截图 OCR、剪贴板历史、知识库检索等。产品功能见 [README.md](README.md)。

解决方案下六个项目：

| 项目                               | 是什么 |
|------------------------------------|---|
| `UiharuMind/UiharuMind`            | Avalonia UI 层（下称 **App 项目**） |
| `UiharuMind/UiharuMind.Core`       | 领域与基础设施。无 UI 依赖，是全仓的重心 |
| `UiharuMind/UiharuMind.Desktop`    | 桌面入口（实际运行的就是它） |
| `UiharuMind/UiharuMind.CLI`        | 命令行入口 |
| `UiharuMind/UiharuMind.Core.Tests` | Core 的测试 |
| `UiharuMind/UiharuMind.App.Tests`  | App 项目的测试（只测不碰 UI 线程/渲染的纯逻辑） |

可复用：

各种样式：UiharuMind/Assets/Themes

## 构建与测试

```bash
dotnet build UiharuMind/UiharuMind.sln
dotnet msbuild UiharuMind/UiharuMind.Core.Tests/UiharuMind.Core.Tests.csproj -t:Test
dotnet msbuild UiharuMind/UiharuMind.App.Tests/UiharuMind.App.Tests.csproj -t:Test
```

测试框架是 xunit v3（`xunit.v3` 4.x），跑在 Microsoft.Testing.Platform（MTP）上，由仓库根的
`global.json`（`test.runner`）选择加入。MTP 测试项目本身是可执行文件，
`dotnet msbuild <csproj> -t:Test` 等价于直接运行产物 exe（`<csproj 同名目录>/bin/Debug/net10.0/<项目名>`）。
**跑测试用这个或直接跑产物 exe，别用 `dotnet test`。**

⚠️ `dotnet test` 在本仓**测不出结果**：项目早已带
`<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`（缺它 .NET 10 SDK 起
`dotnet test` 会直接报错，那是另一个坑），但 SDK 10.0.401 实测 `dotnet test` 两个项目都是
「运行了零个测试 / exit 5」——它压根没发现测试，0 通过不代表测试真过了。判成败一律看
`dotnet msbuild -t:Test` 或产物 exe 的输出（10.0.301 上曾全跑通，说明这个行为随 SDK 版本变，
不要再用 dotnet test）。

过滤语法与 VSTest 不同（`--filter-class` / `--filter-method` / `--filter-trait`…，见
<https://xunit.net/docs/getting-started/v3/microsoft-testing-platform>），参数直接跟在产物
exe 后面。`--filter-class` 要写**完整类型名**（如 `UiharuMind.Core.Tests.AI.RemoteModelInfoTests`），
简写类名会静默得到「运行了零个测试」且退出码不是失败——务必核对输出的「总计」不是 0。

> `ClassicDiagnostics.Avalonia` 是 vendored 的独立解决方案，其 NUnit 测试项目不在
> `UiharuMind.sln` 中，也不走本仓的 MTP 流程。

axaml 的命名空间与 `x:Class` 错误在编译期就会炸（`AVLN2000`），所以对结构性改动，
「解决方案编译通过」是很强的信号。

### 无头界面测试

`UiharuMind.App.Tests/Headless/` 下的测试跑的是**真实控件与真实模板**，只是不要窗口
（Avalonia.Headless）。会话流没有虚拟化，「留多少条目 = 排多久版」这类事只有在这里量得到。

写法是 `HeadlessUi.Run(() => { ... })` 而不是官方的 `[AvaloniaFact]`（`Avalonia.Headless.XUnit`）。
**不是偏好问题，是官方包现在还用不了**：它最新的 12.1.2 对着
`xunit.v3.extensibility.core 3.2.2` 编译，撞上本仓的 4.0.1 会在<b>发现阶段</b>抛
`MissingMethodException`（`TestIntrospectionHelper.GetTestCaseDetails` 签名变了，实测三个
无头测试全红）。等它跟上 4.x 就能把这层去掉，测试体一行都不用改。
新增无头测试类记得挂 `[Collection(HeadlessCollection.Name)]`，否则并行跑会随机撞线程亲和性。

### 开发脚本（驱动真实应用）

有些问题只在「真的用了一阵」之后才显形（长会话切换、内存驻留），无头测试走不完那条路。
这时用开发脚本：

```bash
UIHARU_HOME=/tmp/uiharu-scratch \
  UiharuMind.Desktop --dev-script scenario.jsonl --dev-report report.json
```

脚本一行一步（`page.jump` / `session.open` / `ui.snapshot` / `diag.memory` / `wait` / `quit`），
报告里每步带耗时与结果。实现见 `Features/DevAutomation/`。两条口径：
**不带 `--dev-script` 就一行都不跑**；每一步只许走公开的视图模型面，
不为自动化单开特权入口——否则测出来的就不是用户那条路。

⚠️ 拿真实档案跑之前先 `UIHARU_HOME` 指到副本上，脚本会真的改那份数据。

## 打包与发版

打包与发版的口径（版本号唯一来源、发布参数、构建脚本与 CI、macOS 签名）见
[docs/adr/0029-打包与发版，macOS签名必须用自签名证书.md](docs/adr/0029-打包与发版，macOS签名必须用自签名证书.md)。

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

- 请严格按照仓库规则工作，如果出现冲突，一切以仓库口径为准
- **提交信息只写一句话。** 不要正文、不要任何附加尾注。
- 提交或推送只在用户要求时做。
