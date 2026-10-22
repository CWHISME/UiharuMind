# UiharuMind

[![许可证][license-shield]][license]
[![最新版本][release-shield]][releases]
[![平台][platform-shield]](#下载)

[English](README.md) · **中文**

UiharuMind 是一个面向日常桌面使用的本地优先 AI 助手。它可以通过 llama.cpp 在本地运行 GGUF 模型，也可以调用任何兼容
OpenAI 接口的远程模型。除了聊天，它还给模型提供在目录里干活的智能体、开着就能在你离席时继续推进的群聊，以及截图 OCR、
翻译、剪切板历史、自动点击这些只差一个快捷键的快捷工具。

## 功能特性

- **本地与远程模型** — 用 llama.cpp 运行时加载 GGUF 模型，或添加任何兼容 OpenAI 接口的地址。API Key 在本机加密保存。
  视觉、生图与音频模型分别登记管理。
- **普通对话与智能体** — 对话页合并为一页，左栏切换器拨类型。*普通对话*就是聊天；*智能体*会获得工具（文件读写、shell、
  Web 搜索、知识库检索……）、工作区目录与权限档。
- **角色扮演** — 内置与自建角色，支持人设、开场白、提示词片段与朗读音色。智能体角色可配置工具集、技能、扩展与子智能体名单。
- **群聊与离席** — 智能体群支持串行/并行轮次与可选主持人，并把成员写盘的产物汇总到产物板。把座位交给化身，群聊继续推进。
- **快捷功能**（全局快捷键）— 截图 OCR / 识图问答 / 标注、快捷询问、剪切板历史、翻译、自动点击录制回放、按住说话听写。
- **知识库、记忆与世界设定** — 对文件、目录与 URL 做 RAG 检索；智能体在工作区里维护跨会话记忆；按关键词激活的世界书可在
  多个角色之间共享。
- **扩展与 MCP** — 内置 `browser`、`desktop`、`image`、`audio` 扩展，MCP server 也以扩展形式接入。模型先查后调，高风险
  调用逐条审批。
- **语音与生图** — 经本地语音引擎或在线服务完成朗读与听写；生图经 stable-diffusion.cpp 或远程生图模型。
- **受管 Python** — 智能体把包装进 UiharuMind 创建的虚拟环境，经 shell 运行，不碰系统 Python。

## 截图

<table>
  <tr>
    <td width="50%"><img src="Images/screenshots/chat.png" alt="对话与智能体会话"></td>
    <td width="50%"><img src="Images/screenshots/agent.png" alt="智能体工作区：工具与技能"></td>
  </tr>
  <tr>
    <td><img src="Images/screenshots/characters.png" alt="角色"></td>
    <td><img src="Images/screenshots/group.png" alt="群聊"></td>
  </tr>
  <tr>
    <td><img src="Images/screenshots/models.png" alt="模型"></td>
    <td><img src="Images/screenshots/settings-general.png" alt="设置"></td>
  </tr>
  <tr>
    <td><img src="Images/screenshots/screen-capture.png" alt="截图与 OCR"></td>
    <td><img src="Images/screenshots/clipboard.png" alt="剪切板历史"></td>
  </tr>
  <tr>
    <td><img src="Images/screenshots/translate.png" alt="翻译"></td>
    <td><img src="Images/screenshots/auto-click.png" alt="自动点击"></td>
  </tr>
</table>

## 下载

到 **[Releases][releases]** 取对应平台的包：

| 平台 | 包 | 怎么跑 |
| --- | --- | --- |
| macOS（Apple Silicon） | `…-osx-arm64.zip` | 打开 `UiharuMind.app`。自签名证书签名，**未经公证**——见下。 |
| Windows x64 | `…-win-x64.zip` | 需要 Windows 10（19041）或更高。解压后运行 `UiharuMind.Desktop.exe`。 |
| Linux x64 | `…-linux-x64.tar.gz` | X11 或 XWayland。解开后运行 `UiharuMind/UiharuMind.Desktop`。 |

三个平台都是自包含发布，不需要装 .NET 运行时。当前版本号带 `-alpha`；较早的发布可能只有 macOS 与 Windows 包。

**macOS 首次打开。** 因为包没有经过公证，Gatekeeper 第一次会拦住它。放行一次即可：

```bash
xattr -dr com.apple.quarantine /Applications/UiharuMind.app
```

或者右键点应用 → **打开** → **打开**。之后在功能首次索要时授予**屏幕录制**与**辅助功能**权限——截图、全局快捷键与输入
模拟都要这两项。只要包的身份不变，覆盖更新不需要重新授权；换证书或退回 ad-hoc 签名就相当于换了一个身份。

## 快速开始

**在线模型** — 打开**模型**页，点**添加远程模型**，填好接口地址（例如 `https://api.example.com/v1/chat/completions`）、
模型 ID 和 API Key，然后到**对话**页标题栏选中它。

**本地模型** — 打开**设置 → 本地模型（文本）**，为你的系统下载一个 llama.cpp 运行时；把 GGUF 文件放进模型目录
（**模型**页的文件夹按钮），在列表里加载一个，然后开始对话。

除此之外不需要别的配置。如果模型没有显示，检查模型目录、运行时是否装上，以及文件是不是受支持的 GGUF。

## 快捷键

都可以在**设置 → 快捷键**里改。

| 功能 | 默认键 |
| --- | --- |
| 截图（OCR / 识图问答 / 标注） | `Alt+Shift+Z` |
| 快捷菜单（快捷询问、文件搜索、文本编辑） | `Alt+Shift+A` |
| 剪切板历史 | `Alt+Shift+S` |
| 快捷翻译 | `Alt+Shift+Q` |
| 自动点击录制 / 回放 | `Alt+Shift+G` |
| 按住说话 | macOS 右 `Option`，其它平台右 `Ctrl` |

## 权限

每个会话跑在三档之一：**只读**（不写、不跑 shell）、**自动编辑**（文件修改自动执行，shell 命令仍要确认）与**完全自动**。
工作区之外的写入即使在完全自动档也会问一次。定时任务与目标模式的唤醒轮走受限的预授权命令集。

## 本地服务器

本地模型加载期间，llama.cpp 运行时就是一个兼容 OpenAI 的服务器，别的应用可以直接把它当本地后端：

```
http://127.0.0.1:1369/v1/chat/completions
```

## 数据放在哪里

所有东西都在 `~/.uiharu` 下——配置、会话、角色、知识库、世界书、剪切板历史、日志与智能体工作区；下载的运行时与模型在
`External/` 里。把 `UIHARU_HOME` 指到别处就能换目录（比如开一份临时档案），托盘菜单里也有**打开数据目录**。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。仓库带子模块（`src/ThirdParty/ClassicDiagnostics.Avalonia`、
`src/ThirdParty/Glacier.Grep`），所以要递归克隆：

```bash
git clone --recursive https://github.com/CWHISME/UiharuMind.git
cd UiharuMind
dotnet build src/UiharuMind.sln
```

要出安装包，跑对应平台的脚本。它们共用 `src/Build/common.sh`，产物落在 `src/Build/Output/`：

```bash
src/Build/buildMac.sh    # UiharuMind-<版本>-osx-arm64.zip
src/Build/buildLinux.sh  # UiharuMind-<版本>-linux-x64.tar.gz
src/Build/buildWin.bat   # UiharuMind-<版本>-win-x64.zip
```

在 macOS 上，请通过签名过的 `.app` 运行（`src/Build/dev-run.sh` = 构建后 `open`），不要直接 `dotnet run`：屏幕录制与辅助
功能权限钉在签名过的 `.app` 身份上，裸可执行文件拿不到。

测试是 xunit v3 + Microsoft.Testing.Platform，测试项目本身就是可执行文件，直接跑产物：

```bash
./src/UiharuMind.Core.Tests/bin/Debug/net10.0/UiharuMind.Core.Tests
./src/UiharuMind.App.Tests/bin/Debug/net10.0/UiharuMind.App.Tests
```

构建、测试与代码规范见 [AGENTS.md](AGENTS.md)，术语表见 [docs/CONTEXT.md](docs/CONTEXT.md)。

## 文档

- 应用内手册：**设置 → 帮助**（中英双份）。
- 使用指南：<https://wangjiaying.top/2025/02/16/UiharuMind%E4%BD%BF%E7%94%A8%E6%8C%87%E5%8D%97/>
- 提问与反馈：[Issues][issues] 与 [Discussions][discussions]。

## 致谢

UiharuMind 站在这些项目之上，各自遵循自己的许可证：

- [llama.cpp](https://github.com/ggml-org/llama.cpp) — 本地 GGUF 推理（`llama-server`）
- [stable-diffusion.cpp](https://github.com/leejet/stable-diffusion.cpp) — 本地生图
- [audio.cpp](https://github.com/0xShug0/audio.cpp) — 本地语音合成与识别
- [Avalonia](https://github.com/AvaloniaUI/Avalonia)、[Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) 与
  [Ursa](https://github.com/irihitech/Ursa.Avalonia) — UI 框架、主题与控件
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) 与
  [TextMateSharp](https://github.com/danipen/TextMateSharp) — 长文本窗与语法高亮
- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) 与
  [Microsoft.Extensions.AI](https://github.com/dotnet/extensions) — 智能体循环与模型抽象
- [PuppeteerSharp](https://github.com/hardkoded/puppeteer-sharp) — 浏览器扩展驱动你自己装的 Chrome
- [SharpHook](https://github.com/TolikPylypchuk/SharpHook) — 全局快捷键与输入模拟
- [SoundFlow](https://github.com/LSXPrime/SoundFlow) — 音频播放与录音
- [AngleSharp](https://github.com/AngleSharp/AngleSharp) — 读网页时的 HTML 解析
- [SkiaSharp](https://github.com/mono/SkiaSharp) — 图像解码与绘制
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)、[CliFx](https://github.com/Tyrrrz/CliFx) 与
  [xunit](https://xunit.net/) — MVVM、开发者命令行与测试

引擎二进制（llama.cpp、stable-diffusion.cpp、audio.cpp）与 GGUF 模型由应用在运行时下载，发布包里不带。

## 许可证

[Apache-2.0](LICENSE)

[license]: LICENSE
[license-shield]: https://img.shields.io/github/license/CWHISME/UiharuMind
[releases]: https://github.com/CWHISME/UiharuMind/releases
[release-shield]: https://img.shields.io/github/v/release/CWHISME/UiharuMind?include_prereleases&label=release
[platform-shield]: https://img.shields.io/badge/platform-macOS%20arm64%20%7C%20Windows%20x64%20%7C%20Linux%20x64-informational
[issues]: https://github.com/CWHISME/UiharuMind/issues
[discussions]: https://github.com/CWHISME/UiharuMind/discussions
