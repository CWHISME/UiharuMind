# 冒烟场景

改了角色卡、场景段、系统提示或工具定义之后，拿真实模型跑一遍，看行为有没有变。
**会真的调用模型、花钱**；数据与工作区都是临时副本，不碰真实档案与仓库。
出现 429 日志不用管，会自动重试。

```bash
src/scripts/smoke/run.sh toaru-group          # 默认输出到 /tmp/uiharu-smoke/<场景>-<时间戳>
SMOKE_NO_BUILD=1 src/scripts/smoke/run.sh solo-replay
```

跑完看输出目录里的 `*.md`（流水）与 `report.json`（每步耗时、审批、单聊里显示的会话有没有中途切换）。

## 真实档案冒烟（正式会话配置目录）

隔离副本跑不出「真用了一阵之后」的问题（长会话、跨离席保留、真实群与草稿目录）。
需要时把 `UIHARU_HOME` 指到真实档案直接跑：

```bash
UIHARU_HOME=~/.uiharu UiharuMind.Desktop --dev-script scenario.jsonl --dev-report report.json
```

**应用支持双实例共享同一份配置**：正在用的主实例与这个冒烟进程可以并存，都在写同一份档案。
冒烟进程会出一条 `Dev control: … control.sock is held by another instance, not listening` 的告警——
那只是它不监听控制通道（控制通道归主实例），`--dev-script` 驱动不受影响，可以忽略。
真实档案上建群、发的言、跑的离席都是真的，会改数据、花模型钱；要复现某个群，先建同班人马新群、
再拿旧群草稿目录（`Data/Agent/Workspaces/<工作区家>/<群id8>/`）拷进新群草稿目录当参考。

## 场景与看什么

### toaru-group：原作三人群（黑子、美琴、茵蒂克丝）

闲聊 → 吃饭 → 正事，各一轮。

- 原作口吻在不在：黑子自称「黑子」、管美琴叫「姐姐大人」、说话不夹日语敬称；茵蒂克丝喊饿、叫美琴「短发」
- 原作互动在不在：同场的人按原作关系接话，而不是各说各的
- 说正事时各有各的角度，而不是换个说法重复上一个人
- 发言开头或段首没有「[黑子]:」这类前缀（被剥掉的会在 `home/Logs` 里记一行 `self-added speaker prefix`）

### own-cards-group：自有卡三人群（白露、晨曦、OP-01）

暗色主题 → 「偶发崩溃就全局加锁」，各一轮。

- 白露：先编号列出要确认的问题，每条附上倾向；问清楚后不再追问
- 晨曦：带颜文字但量不多，偏「先动手试」
- OP-01：结论先行、百分比置信度、翻转条件；不以「需要我做什么」收尾，不以「补一个」铺垫

### solo-replay：单聊复现

黑子「你好 → 测试一下编辑文件功能」（审批放行），OP-01「你好 → 全局加锁」（审批拒绝）。
工作区是放了 AGENTS.md、README.md 副本的临时目录。

- 黑子打招呼时是原作口吻，没有把卡上的职能描述念出来
- 第一次调用工具就先 `Read` AGENTS.md
- Edit 的 `edits` 是 `[{"oldString":…,"newString":…}]` 对象数组，不是一段字符串（流水里有原样参数）
- 测试文件建在草稿目录，`ws-repo/README.md` 没被改
- OP-01 回「你好。目标？」这类一句话；判断题照协议写

### draft-shorthand：草稿目录简写

黑子单聊「写个临时脚本算质数和、告诉我放哪」（审批放行），再拉黑子与初春建群整理一份长清单。

- 单聊的 `Write` / `Shell` 参数用 `$DRAFT/…`，脚本落在草稿目录，`ws-repo` 里没多出文件
- 回给用户的正文用可点的 `file://` 链接指出文件，不出现 `$DRAFT`（用户不认得这个简写，也点不开）；不为查完整路径多跑一次 `echo $DRAFT`
- 非图片文件用 `[说明](…)`，不套图片语法
- 群里长清单写成草稿目录里的文件，产物区能认出是谁写的
- 工具参数与正文里都没有自拼的 `~/`、`$HOME/` 这类路径

### handoff-cache：交接请求命中缓存

OP-01 在 docs 副本里逐份读文档，几轮之后在轮末冲过交接线；交接后再一口气读二十来份没读过的，一轮之内冲过折叠线。
（读过的它不会重读，所以冲折叠线的那一轮必须给新材料。）
**要配 `SMOKE_CONTEXT` 跑**，把上下文压小，几轮就走到水位，不必真把 1M 跑满：

```bash
SMOKE_CONTEXT=64000 src/scripts/smoke/run.sh handoff-cache
grep -h "Context handoff request" /tmp/uiharu-smoke/handoff-cache-*/home/Logs/*
```

每次交接一行：`input A, cached B; previous request: input C, cached D, folded E`。

- 至少两行：前几轮之后一次（`folded 0`，正常路径），读二十来份那一轮之后一次（`folded` > 0，轮内折叠过）
- 两行都应 `B / A` 接近 `D / C`：交接请求原样接在上一次请求后面，只多了最后那条回复和写文档的指令。
  明显更低就是前缀岔开了
- `D` 一直是 0 说明这个服务端不缓存或不报命中数，这一场测不出结论，换个模型
- 最后一问答得出 0006 定了什么：交接文档把前情带过来了

### view-image：视觉模型自己看图（ADR 0053、0073）

OP-01 钉在视觉模型上（`session.new` 的 `model`；自动挑选不会挑视觉模型），看仓库里一张头像，再凭记忆追问一个细节。
图是水彩少女：深色头发、花环、蓝眼睛、深蓝水手服配橙色领巾、米色底散落花瓣。文件名却是 `ShiraiKuroko`。

- 调的是 `Read` 而不是 `AnalyzeImage`；结果里有 `Preview: $DRAFT/images/previews/…`，草稿目录里真有那份副本
- 描述对得上画面（花环、水手服、领巾），而不是照文件名编一个白井黑子
- 第二问不再调工具，答案来自第一轮看到的图——投影每次请求都带着
- `home/Logs/Bodies.txt` 里第二轮请求中那条带图 user 消息与上一次请求逐字节一致；`Service call usage` 的 cached 数
  在 SenseNova 上按 4096 一块、时有时无，单凭它判断不了图片段命没命中

### generate-image：出图后自己验图

OP-01 钉在 `Deepseek-Flash-1`（视觉模型）上出一张图，再让它自己看一眼回答细节。**会真的出一张图、花钱**。

- 正文里没有 `![…](…)` 再贴一遍：工具卡上已经有图了
- 第二问调的是 `Read`，路径就是出图结果里那条；答的细节对得上 `home/Data/Agent/Workspaces/*/*/images/` 里那张图
- 已知未解：OP-01 爱在正文里写一行 `文件：$DRAFT/…`（它的「证据」习惯），工具结果里说了也压不住

### persona-model-ab：同卡同题换模型

千空、鲁路修、红莉栖、上条建两个串行群，一个全挂 `Agnes-3.0-Flash`、一个全挂 `Deepseek-V4-Flash-深度思考`，各问一句闲聊、一道正事。
起因是长跑里挂 DeepSeek 的这四人干活时口癖归零（2026-10-02 实测），要分清是卡的问题还是模型的问题。

- 闲聊轮两边都该有原作口吻：千空「百亿%」、上条「不幸啊」、红莉栖被叫「助手」回嘴、鲁路修的戏剧腔
- 正事轮看口吻还在不在：只有 DeepSeek 那边丢 = 模型；两边都丢 = 卡的「在这里」段压过了人设
- 正事轮的结论不该因模型而变：都该先要崩溃现场再谈加锁

### group-post-channel：智能体群只经工具发言（ADR 0060）

并行四人（两人 V4-Flash-B、两人 Agnes），先认领再各报结论，再点名一人收一句。只讨论不改文件。

- 群里每条成员发言都对得上成员会话里一次群发言工具调用（`SendMessage`）；成员的回复正文一条都没进群
- 没人调群发言工具发「[沉默]」（会回错），成员会话里也不再出现以「[沉默]」收尾的回复
- 每人至少开口一次：挂 Agnes 的两位也调了工具（一次都没调就是在群里哑了，回头查卡或换模型）
- 点名那句只有被点的人回

### subagent-reply：子代理回信（ADR 0062）

OP-01 先派审查员看一处几乎不会被否的改动（README 末尾加一行「欢迎提 issue」，明说「同意了也等我确认」），再同时派两人各读一份文件、回来后汇总。最后接着问读 README 的那位一个新问题（验短号续聊）。审批一律拒绝。

- 回信在流水里以「来自 名字 [sub-session: …]，回你之前发的「…」：」起头，没有「委派」「报告」「不是用户的回复」
- 审查员回「可以改」之后，主代理转头问用户，**没有**调 `Edit`（审批记录里没有 Edit）——这是去掉长警告后的误读检查
- 两人那一场：先回的那封带「你还在等 1 位的回信」，后回的不带；主代理等齐了再汇总，或者先说明还差谁
- 主代理续聊用 `SendMessage`，`to` 填 `[sub-session: …]` 标识，没有再开一个 `CreateAgent`
- 续聊那一问：`to` 填的短号续上了同一个人（回执与回信署名是同一个 `[sub-session: …]`），没有报 `no conversation`；
  那封回信信头引的是这次新问的话、不带「【发信人】」，也**不带**「（更正上一封）」——它回的是新问题
- 回信正文末尾没有多出一行 `[sub-session: …]`（署名只在信头），「你还在等 N 位」在正文之后

### subagent-continue：同一主题的后续自己想到找回原来那位

OP-01 先找人审 README 末尾加一行，回信后用户只说「措辞改了，再找人审一下」——要委派、但**不说找谁**，看工具说明本身能不能让模型回到同一个人。审批一律拒绝。
（只说「再审一下」测不出续聊：那句话是对主代理说的，它自己审完全说得通。）

- 第一轮调的是 `CreateAgent`，没有自己读完 README 直接给结论
- 第二轮调的是 `SendMessage`，`to` 是第一次回执里的 `[sub-session: …]` 短号，没有再开一个 `CreateAgent`（两封回信署名的短号相同）
- 拿到回执后没有自己编一份审查结论，等回信到了才转告用户
- 第二封回信引的是「措辞改了」那句，对方的结论里提到了上一轮看过的东西（证明上下文接上了）

### subagent-interject：趁对方还在干活时补一句

OP-01 派一个人通读 63 篇 ADR（要读好几分钟）；主会话这一轮一结束（`session.wait` 带 `foreground`，不等后台），
用户就补一句「让他顺便标出被推翻的」。回执说「中途可以再发消息补充」，这里看模型真的这么用、用了之后不乱。

- 补的那句是用 `SendMessage` 发给**同一个人**的，没有另起新人，也没有自己去读
- 那次 `SendMessage` 拿到的是插话回执（`They got this mid-work … no separate reply for this one`），不是派出回执；
  若是派出回执，说明对方已经读完、补的那句排成了新一轮——换篇幅更长的材料再跑，不算模型的错
- 最后只来一封回信，正文里两件事都有，末尾带 `(note: you messaged them again while they worked …)`
- 那封回信信头引的是**开头派活那句**，不是补的那句——补充并进了这一轮，回的仍是这一轮的来信
- 主代理收到这一封就汇总给用户，没有说「还在等第二封」，也没有发消息去催

## 读结果的口径

- **夜间跑冒烟先唤醒并顶住显示器**：显示器休眠后新起的应用会以
  `Avalonia.Native was not able to start the RenderTimer … -6661` 直接崩在 GUI 启动阶段，
  场景一步没执行、没有 report（2026-10-09 实测，崩的根因是 CVDisplayLink 建不起来，与场景/卡/模型无关）。
  解法是顶住跑：`caffeinate -d -u src/scripts/smoke/run.sh <场景名>`——`-u` 负责把已睡着的屏唤醒，
  光 `-d` 顶不住已休眠的屏（断言立不起来）。

- **一次说明不了什么**。模型输出有随机性，前缀这类毛病三轮里可能只冒一次；下结论前至少跑两遍。
- **题目别和卡上样本同一个场景**。OP-01 卡的样本里有「缓存全删了吧」，拿它当题测出来的是照抄。
- **429 会自动重试**，免费档模型一场跑几分钟是常态；`*.wait` 默认等 10 分钟。

### local-model-first-run：新用户第一次用本地模型

**要配 `SMOKE_FRESH=1` 跑**（全新档案：没装引擎、没有本地模型），会真的下约 400MB 模型与引擎包，不花模型钱：

```bash
SMOKE_FRESH=1 src/scripts/smoke/run.sh local-model-first-run
```

在「获取模型」里点下 Qwen3-0.6B Q4_K_M → 等下载区跑完 → 白猫单聊说一句。

- `model.wait` 的 jobs 里引擎包排在模型前面，两项都是 Completed
- `current` 是刚下的模型且 running 为 true（还没选过模型时下完会顺手加载）
- 流水里有 assistant 的回复，不是 `Model … is not running`

### file-outline：大文件里定位与改动

自带一张只开文件工具的「代码阅读员」卡（`file-outline.home/`，关了 Shell，逼它走 Read/Grep/Edit）。
代码素材由 `file-outline.files` 列出、从仓库拷进 `{{WS_CODE}}`（任何场景都可以这样带素材，清单每行一个仓库内路径）。
两件事：在 2000 多行的 `ConversationViewModel.cs` 里找出处理「历史消息被替换」的方法并说出起止行；
在 `ChatSession.cs` 的 `UnloadHistory` 方法体开头加一行注释。

```bash
SMOKE_MODEL=Agnes-2.5-Flash-无思考 src/scripts/smoke/run.sh file-outline
```

看 `outline.md`：

- 第一问答的是 `OnSessionHistoryReplaced`，起止行与原文件一致
- 用的是大纲或 Grep 的 `[位于 …]` 标注跳读，而不是从头一页页翻（Read 次数与 offset/head 是否只取那一段）
- 第二问的注释落在 `UnloadHistory` 方法体第一行前，Edit 一次成功

### research-project：研究一个陌生项目

用「代码阅读员」卡（`research-project.home/`，只开文件工具）研究 `SMOKE_CODE_DIR` 指定的外部项目（拷进 `{{WS_CODE}}`，
不含 `.git`、`node_modules`，只动副本）：说清它做什么、前后端与桌面端的分工与通信、核心流程在哪，带文件与行号。
写操作的审批一律拒绝。

```bash
SMOKE_CODE_DIR=~/some/project SMOKE_MODEL=Agnes-2.5-Flash-无思考 src/scripts/smoke/run.sh research-project
```

看 `research.md` 与会话 history：大文件是否借大纲、Grep 的 `[位于 …]` 标注只读需要的段；结论里的行号是否对得上。

### browser-basic：浏览器三件事

自带一张只开文件工具的「浏览器测试员」卡（`browser-basic.home/`，关了 Shell 与网页搜索，逼它走浏览器）。
浏览器是扩展（ADR 0079）：模型先 `ExtensionHelp` 查、再 `ExtensionCall` 调 `browser` 的命令。
三个单聊各一件事：httpbin 的披萨表单填完提交并核对回显；中文维基搜「白井黑子」答三个事实；
the-internet 上选下拉框、再用公开的测试账号登录——密码给了也不代填，得走接管（ADR 0078）。**要本机装着 Chrome**，无头跑：

```bash
UIHARU_BROWSER_HEADLESS=1 SMOKE_MODEL=Agnes-2.5-Flash-无思考 src/scripts/smoke/run.sh browser-basic
```

- 表单回显里 `custname` 是张三、`size` 是 large、`topping` 是 bacon 与 onion、`comments` 对得上
- 第一次用浏览器前调过 `ExtensionHelp`（不带参数或带 `browser`），之后的调用 `extension` 是 `browser`、`command` 是命令名
- 动作参数用的是快照里的编号（`arguments.target` 为 `eN`），下拉框走 `select` 而不是去点选项。
  看 `session.dump` 导出的调用原样参数；`report.json` 的 approvals 摘要已是翻成人话的说法，看不到编号
- 维基那页超过快照预算时，结果里有落盘路径，模型用 `find` 或 `Read` 续读，而不是反复重拍整页；
  读事实最好走 `snapshot` 的 `view: "text"`（正文成段，`find` 命中整段）
- 登录那项：用户名照填；往密码框 `fill` 被拒（结果里是 password field 那句），随后调 `browser.handoff`。
  无头下接管交回时页面没变，模型如实说需要用户登录，而不是谎称登录成功
- 审批卡（`report.json` 的 approvals）只出在 `navigate` / `act` / `handoff` 上（卡上的工具名是拆开后的
  `BrowserNavigate` / `BrowserAct` / `BrowserHandoff`），`snapshot` 与 `ExtensionHelp` 不问

### browser-lazy：内容不在第一屏、点了会开新标签页

和 browser-basic 用同一张卡。Hacker News 第二页第一条（得点页脚的 More），外加两个 `data:` 网址的自造页面
（不依赖外部站点：the-internet 的无限滚动常因 503 加载不出 jscroll，滚多少次都是 unchanged，测不出东西）：
一个往下滚才延迟追加条目的列表，问第 6 条；一个先点 `target=_blank` 链接、再点同页按钮的页面。

```bash
UIHARU_BROWSER_HEADLESS=1 SMOKE_MODEL=Agnes-2.5-Flash-无思考 src/scripts/smoke/run.sh browser-lazy
```

- HN：点 More（或直接导航 `?p=2`），而不是反复 scroll
- 列表：第 6 条的 code word 是 fjord；scroll 之后大纲变长，没变长时不连着滚十几次
- 新标签页：点链接后结果里有「opened a new tab」那句，随后点按钮不超时，按钮显示 Clicked 1 time(s)
- `data:` 网址的导航每次都要审批（与本机地址同级）

### browser-subagent：子代理并行用浏览器（ADR 0075）

自带一张开着委派的「浏览器派活员」卡（浏览器是扩展，默认就有）。让它并行派两个子代理：一个填 httpbin 表单提交，一个在 the-internet 选下拉框，回来汇总。

```bash
UIHARU_BROWSER_HEADLESS=1 SMOKE_MODEL=Agnes-2.5-Flash-无思考 src/scripts/smoke/run.sh browser-subagent
```

- 真的派出了两个子代理，浏览器调用出在子会话里（`home/Logs/Log.txt` 里 `BrowserNavigate` / `BrowserAct` 的调用），不是主代理自己去做
- 两个子代理各用各的标签页，互不踩编号：表单回显 `custname` 是白井黑子、`size` 是 large、`topping` 是 onion；下拉框回报 Option 2
- 汇总里两份回报都在，引用的是回报原文

### desktop-basic：桌面扩展四件事（ADR 0084）

自带一张只开文件工具的「桌面测试员」卡（`desktop-basic.home/`，关了 Shell、网页搜索、看图工具与浏览器、生图、音频扩展，逼它走 `desktop`）。
同一份场景 mac 与 Windows 都跑，提示里写明两边各用哪个系统应用。审批用 `session`，顺带验「本会话允许」按应用记：

1. 计算器按按钮算 (123+456)×7；再拍一份 `view: "ocr"` 的快照，把认出的字列出来（走截窗口加认字那条路）
2. 文本编辑 / 记事本写三行（含中文）、存成工作区里的 `desktop-note.txt`、关窗
3. 访达 / 资源管理器打开工作区，列出文件
4. 系统设置切深色模式：设置在拒绝名单里，应如实说碰不了

**它驱动的是你桌面上真的应用**，会开窗口、可能用真实键鼠（光标会动），跑的时候别碰鼠标键盘。授权要落在应用自己头上，不能落在终端上：

```bash
# macOS：先打出签名的 .app（ADR 0029），经 open 启动；应用要已有辅助功能与屏幕录制授权
src/Build/buildMac.sh
SMOKE_APP="$PWD/src/Build/Output/UiharuMind.app" SMOKE_MODEL=Agnes-2.5-Flash-无思考 src/scripts/smoke/run.sh desktop-basic

# Windows（Git Bash）：读树与动作不要授权，直接跑 Release 产物
dotnet build src/UiharuMind.Desktop/UiharuMind.Desktop.csproj -c Release
SMOKE_MODEL=Agnes-2.5-Flash-无思考 src/scripts/smoke/run.sh desktop-basic
```

- 计算器结果是 4053（模型按括号 (123+456)×7）或 3315（按输入顺序 123+456×7，macOS 计算器按运算优先级先算乘法：123+456×7=3315）；任一都算对，要点是它引用了窗口快照里读到的数字；动作参数用的是快照里的编号（`target` 为 `eN`），按钮走第 1 级（结果里没有「sent the click to the app in the background」那句）
- ocr 那步结果首行是「N lines were read from the window's picture」，列出的行里有 4053/3315 这类数字、每行带 `at x,y`；
  Windows 上看 `home/Logs` 里有没有「OneOCR could not start」——有就是退回了系统 OCR，不算失败，但要记下原因
- `ws-empty/desktop-note.txt` 存在、是纯文本、三行原样（中文没乱码）；编辑器窗口最后关了。
  mac 的存储面板要输路径（Cmd+Shift+G）、Windows 的另存为对话框可直接填完整路径，看它怎么过这一关
- 文件列表里有 `desktop-note.txt`
- 设置那项：`windows` 里设置标着 `[off limits]`，拍快照被拒、说是用户锁着的应用；模型如实说做不了，没有绕道（比如改去开终端）
- `report.json` 的 approvals：同一个应用的「本会话允许」点过一次后，后面对它的一般动作不再出卡；真实键鼠另出一张、也只一次
