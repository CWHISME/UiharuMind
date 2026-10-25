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
| `src/UiharuMind`                   | Avalonia UI 层（下称 **App 项目**） |
| `src/UiharuMind.Core`              | 领域与基础设施。无 UI 依赖，是全仓的重心 |
| `src/UiharuMind.Desktop`           | 桌面入口（实际运行的就是它） |
| `src/UiharuMind.CLI`               | 命令行入口 |
| `src/UiharuMind.Core.Tests`        | Core 的测试 |
| `src/UiharuMind.App.Tests`         | App 项目的测试（只测不碰 UI 线程/渲染的纯逻辑） |

可复用：

各种样式：src/UiharuMind/Assets/Themes

## 构建与测试

```bash
# 构建
dotnet build src/UiharuMind.sln
# 测试：直接运行产物 exe，失败详情会打在终端
./src/UiharuMind.Core.Tests/bin/Debug/net10.0/UiharuMind.Core.Tests
./src/UiharuMind.App.Tests/bin/Debug/net10.0/UiharuMind.App.Tests
```

实测耗时（Apple Silicon）：增量构建解决方案约 10 秒，Core 测试全量约 10 秒，App 测试全量约 40 秒（含无头界面），
按类过滤只要一两秒。

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

**重要**：不懂的话先看 `src/scripts/smoke`

有些问题只在「真的用了一阵」之后才显形（长会话切换、内存驻留），无头测试走不完那条路。
这时用开发脚本：

```bash
UIHARU_HOME=/tmp/uiharu-scratch \
  UiharuMind.Desktop --dev-script scenario.jsonl --dev-report report.json
```

脚本一行一步（`page.jump` / `session.open` / `ui.snapshot` / `session.search` / `sessions.search` / `diag.memory` / `wait` / `quit`），
报告里每步带耗时与结果，逐步落盘。群聊冒烟另有 `group.create` / `group.post` / `group.continue` /
`group.wait` / `group.dump`，离席另有 `group.away.start` / `group.away.wait` / `group.away.end`，单聊冒烟有 `session.new` / `session.post` / `session.wait` / `session.dump`
（都会真的花钱跑模型；两个 wait 顺带按参数点掉审批，`session.dump` 连工具调用的原样参数一起导）。实现见 `Features/DevAutomation/`；
改卡、改提示词后的回归场景与检查清单在 `src/scripts/smoke/`（`run.sh <场景名>`，自带隔离档案）。
动了会话流、装载或裁剪，前后各跑一次 `src/scripts/perf/switch-perf.sh`：在真实档案副本上轮流切几个长会话，
汇总探针打点与内存，不调模型。两条口径：
**不带 `--dev-script`、也没开控制通道，就一行都不跑**；每一步只许走公开的视图模型面，
不为自动化单开特权入口——否则测出来的就不是用户那条路。

要边跑边插话（长跑中途拍板、改离席目标、看进度），用开发控制通道（ADR 0059）：同一组步骤，经 CLI 发给运行中的应用。
开发者模式开着或带 `--dev-control` 才开。

```bash
export UIHARU_HOME=/tmp/uiharu-scratch UIHARU_DESKTOP_EXE=<Desktop 产物路径>
UiharuMind.CLI app start                      # 独立起应用，等通道应答
UiharuMind.CLI app call group.post --args '{"group":"…","text":"…"}'
UiharuMind.CLI app run scenario.jsonl         # 与 --dev-script 同格式
UiharuMind.CLI app quit
```

应用支持多实例共享同一份档案（会话与配置的并发写已处理）。只是想再起一个实例给人看效果时，或者测试性能直接用真实档案即可，不必复制；
除非会自动改数据的脚本，需要先 `UIHARU_HOME` 指到一个空的临时路径上。
在 `/src/Build` 有出包脚本。

应用里的模型要驱动应用自己时，开发者模式下有内置技能 `uiharu-dev`（ADR 0061）：正文现生成，步骤清单取自各步骤的 `Usage`。
新增或改了步骤，记得写 `Usage`——它同时是 `app.ops` 的回答与这份技能的内容。

### 日常开发怎么跑（macOS）

macOS 上用签名好的 .app 跑，别直接跑 bin 里的 exe：

```bash
src/Build/dev-run.sh   # = buildMac.sh && open src/Build/Output/UiharuMind.app
```

为什么：TCC（辅助功能/屏幕录制）把授权钉在「签名主体 + bundle id」上（`.app` 是证书签名 `com.cwhisme.uiharumind`），
bin 里的裸 exe 是 ad-hoc 签名、身份不同——从终端/脚本裸跑它查不到授权，**不是 bug**（[ADR 0029](docs/adr/0029-打包与发版，macOS签名必须用自签名证书.md)）。
例外：Rider 直接调试时进程的责任链落在 IDE 上，桌面权限走 Rider 的授权，所以开发调试这条路上桌面功能可用；
冒烟脚本（src/scripts/smoke/run.sh）默认跑 `bin/Release` 裸 exe、责任进程是终端，桌面场景必须 `SMOKE_APP` 指向签名 `.app`，
并先给 UiharuMind 勾辅助功能/屏幕录制授权（勾完重启应用生效）。desktop-basic 已实测全绿（2026-10-12）。
要断点调试：在 `src/Build/Output/UiharuMind.app` 上挂调试器；日常运行走 dev-run.sh。

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

### 依赖规范

[规则4-1] App 项目里的服务（如 `IMessageService`）由**组装入口**取出、经构造函数往下传。组装入口只有三处：
`App` 启动、窗口与视图的后台代码、开窗口调系统的界面外壳静态入口（如 `FileOpener`）。视图数据、视图模型、
辅助类一律从构造接收，这样弹确认、弹提示的流程才测得到——测试传 `App.Tests/TestDoubles/RecordingMessageService`。
框架按类型创建、只能无参构造的（如 `App.ViewModel.GetViewModel<T>()` 建的页面数据），无参构造取一次再转调可注入的那个。
这条由 `App.Tests/Architecture/ServiceAccessBoundaryTests` 机械检查。

## 协作口径

- 请严格按照仓库规则工作，如果出现冲突，一切以仓库口径为准
- **提交信息只写一句话。** 不要正文、不要任何附加尾注。
- 提交或推送只在用户要求时做。
