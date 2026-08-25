# 代码执行不设独立工具，走 shell 与受管 venv

agent 要跑 Python（精确算术、结构化数据处理、出图表）。**不引入第二个执行面**：
不加 `ExecuteCode` 之类的工具，agent 用现成的 `Shell` 去调一个由我们管理的虚拟环境里的
解释器。解释器由用户提供，虚拟环境由我们建、由 agent 自己往里装包。

评估过官方的 `Microsoft.Agents.AI.LocalCodeAct`（1.19.0-preview），**否决**。

## 决策

1. **解释器归用户，虚拟环境归我们。** 探测 PATH 上的 `python3`/`python`（探不到允许在设置页
   手填），用它跑一次 `-m venv`，落在 `AppPaths.External.PythonEnv`。之后 agent 用的一直是
   虚拟环境里那个解释器。

   不直接用宿主解释器，是因为 macOS 自带的 python3 与 Debian 系都是 PEP 668
   externally-managed，`pip install` 会被直接拒绝；绕过去（`--break-system-packages`）
   就等于在改用户的系统环境，而审批卡上看到的只是一条平平无奇的 pip 命令。
   虚拟环境让「解释器归用户、包归我们」两件事同时成立。

2. **第三方包由 agent 自己装**，纪律段里指名 `{venv}/bin/python -m pip install`。
   我们不预置科学栈：预置什么都是猜，而装错了还占几百 MB。

3. **建环境是设置页的显式一步，不做惰性创建。** `AgentAssemblyPlan.Resolve` 是全仓唯一读外部
   世界的地方，而且是同步的、从不等网络；建环境要起子进程、解压标准库，几十秒起步。
   装配期只读 `PythonEnvironment.IsReady` 这一个布尔，它进 `AgentAssemblyFacts`——
   用户建完环境，不重开会话下一轮就生效。

4. **探测与创建都带超时，超时按失败处理。** macOS 上 `/usr/bin/python3` 是个存根，
   没装 Xcode Command Line Tools 时执行它会弹 GUI 安装对话框并**无限期挂住子进程**。
   `File.Exists` 判断不出来，只有超时能把那种情况变回一次干净失败。

5. **产出经 `file://` 进对话，不做捕获管道。** 图表写进 `AppPaths.Data.AgentOutputs`，
   模型在回复正文里用 `![说明](file:///…)` 引用它。对话正文本来就按 markdown 渲染，
   而 `LiveMarkdown.Avalonia` 的默认 image handler 就含 `LocalFile`——图片因此自动显示、
   随历史持久化、重开会话仍在。**URI 前缀由纪律段直接给出**，不让模型自己拼
   （Windows 上 `C:\a\b` 要变成 `file:///C:/a/b`，反斜杠与盘符两处都得改）。

6. **产出目录归 `Data` 而非 `Cache`。** 对话正文以链接引用它们，清掉就等于历史里留下一堆坏图；
   它们也不可重建——重跑一次是另一次推理。

## 为什么否决 LocalCodeAct

逐条清算它相对「shell + venv」多给了什么：

| 声称的好处 | 结论 |
|---|---|
| AST 白名单更安全 | **不成立**。校验器只验模型生成的那段代码的 AST，**不验库内部**。`matplotlib.savefig("/任意路径")` 照样落盘。微软自己写明「defense-in-depth controls, not a containment boundary」 |
| 环境确定、包可控 | **不是它的功劳**，是 venv 的功劳。shell 拿 `{venv}/bin/python` 一样确定 |
| 资源限制（timeout / 输出上限） | 近乎白给。shell 侧框架已有 `MaxOutputBytes` 64 KiB |
| 省 token、代码里编排（`call_tool`） | **必须放弃**，理由见下 |
| 文件捕获（新文件自动变 `DataContent`） | 真增量，但被决策 5 以更便宜的方式覆盖，且捕获策略完全归我们 |
| 多行代码免转义 | 真增量，但被「先 `Write` 成 .py 再跑」覆盖，且那条四种 shell 都成立 |
| 子进程不继承宿主环境变量 | 真增量。接受这个缺口——shell 本来就继承 |

而代价是实打实的：

- **强行升级整个框架面。** 它依赖 `Microsoft.Agents.AI.Abstractions 1.19` /
  `Microsoft.Extensions.AI.Abstractions 10.9`，而仓里钉的是 1.16 / 10.7。NuGet 会把**抽象层**
  顶到 1.19、**实现层**留在 1.16——编译 0 错误，炸在运行期，而这几个包一个 `-preview`
  一个 `-alpha`，没有任何 SemVer 承诺。
- **`call_tool` 会静默抽掉现有审批。** 审批是在 `FunctionInvokingChatClient` 那一层拦
  `FunctionCallContent` 实现的；`call_tool` 是 Python 子进程回调到 provider、由 provider
  直接 `InvokeAsync` 那个 `AIFunction`，根本不产生 `FunctionCallContent`。把已包审批的
  `Write`/`Edit` 交给它，模型在 Python 里调就是**悄悄放行**，不是报错。
- **白名单是纯摩擦。** 装了新包还得同步改 `AllowedImports`（而且是**替换**不是追加），
  否则抛 `CodeValidationException`。shell 这条路没有这个问题。
- **两个执行面，模型要二选一。** 纪律段得花篇幅解释边界，而选错的代价是静默的低效。
  一个执行面永远比两个好教。

## 代价明说

- **能力时有时无**：用户没建环境，纪律段就整段不发，模型不知道有 Python。这是刻意的——
  告诉模型一个不存在的解释器，它会照着调然后白烧一次调用（同 ADR 0017「判据取装配结果」）。
- **产出能不能被看见，取决于模型愿不愿意写那个 `![](file://…)`。** 没有兜底捕获。
  换来的是零新机制、零历史手术。
- **`Data/Agent/Outputs` 不自动清理。** 它在用户自己的数据目录下，随手可删。

## 与既有决策的关系

- 爆炸半径与 `Shell` 同级：能写任意路径、能起子进程，只是不联网（这是 shell 也做得到的事的
  子集）。因此**权限档语义一点没动**——它本来就是 `Shell` 的一次调用。
- 顺带澄清 ADR 0010：那条「越界写入贯穿三档」的硬规则**只覆盖 `Write`/`Edit` 两个工具**
  （`ApprovalModeMapper.IsOutOfWorkspaceWrite` 第一行 `if (!IsMutatingFileTool(...)) return false;`）。
  `Shell` 的边界一直是「用户点的那一下」。
- 不进 provider 链，因此 ADR 0005 的提示词顺序不受影响；纪律段按 ADR 0017 自己写一段，
  嵌在「## 命令行」之下的「## Python」里。
