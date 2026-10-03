# 开发控制通道：本机 socket 驱动运行中的应用，命令沿用开发脚本那套

> **状态**：accepted，**已实现**（2026-10-03）。部分推翻 `DevScriptRunner` 注释里「启动期一次性、没有入站通道」那条。

决定：**开发者模式**开着（或带 `--dev-control` 启动）时，应用在档案目录下开一个本机控制通道；`UiharuMind.CLI` 加 `app` 一组命令连上去，
一次发一步，步骤就是开发脚本现有的那些（`group.post`、`group.away.wait`、`diag.memory`……）。
两样都没有，通道不开，与今天完全一样。

## 为什么

开发脚本是启动期一次性的：脚本一跑起来，中途插不进话。长跑因此只能「起一段、等它结束或杀掉、读流水、拍板、再起一段」：

- 要插话、要改离席目标，只能杀进程重开，段与段之间的内存曲线也断了；
- 启动脚本挂在调用方的后台任务下，后台任务的时长上限就是应用的寿命（段 18a 跑到 120 分钟被连带杀掉）；
- 想看进度只能翻日志，没法问应用「现在谁在说话、在等什么」。

## 口径

**命令就是开发脚本那套，不另起一套**
- 执行一步的那段逻辑（解析 → 切到界面线程 → 计时 → 收结果或错误）从 `DevScriptRunner` 抽成共用的执行器，
  脚本与控制通道都调它。加一步仍是加一个 `IDevCommand` 类，两边同时能用。
- 仍然**只走公开的视图模型面**。通道只是换了个下发步骤的方式，不多开任何入口。
- `--dev-script` 保留（冒烟脚本 `src/scripts/smoke/` 照旧能跑）；CLI 另给 `app run <script.jsonl>`，经通道逐行发。

**协议**
- 一行一个 JSON。请求 `{"id":…,"token":…,"op":…,"args":{…}}`，回复与脚本报告条目同形：`{"id":…,"ok":…,"elapsedMs":…,"result"|"error":…}`。
- 每个请求各跑各的，不排成一条队：`group.away.wait` 挂着等的时候，另一条连接要能插进 `group.post`。
  真正改状态的都在界面线程上，天然串行。
- 客户端断开不取消已经在跑的那一步（等待类步骤只是在看，不影响群）。
- 另加三条只在通道里有意义的：`app.ping`（就绪探测）、`app.quit`（与托盘「退出」同一条路）、`app.ops`（列出全部步骤）。

**防护**
- **开关是开发者模式，不进设置**：Core 的 `DeveloperMode`，一个单独的标记文件 `DeveloperMode`，与屏蔽角色的解锁（`DeveloperUnlocked`）互不牵连。
  入口与屏蔽角色同在导入角色窗口的链接框：填开发者口令即打开。口令不明文进代码，只存加盐后的 SHA-256。
  运行中打开 / 关闭，通道跟着开 / 关。
- 普通用户没有这个入口，通道就不会开。对开发者常开不算新口子：连得上的只有本机同一用户，而同一用户的进程本来就能直接读档案里的会话与密钥。
- `--dev-control` 保留：冒烟、性能脚本用的是隔离的临时档案，那里没有解锁标记；`app start` 会带上它。
- 通道在档案目录下：三平台都用 Unix socket `$UIHARU_HOME/run/control.sock`（Windows 10 1803 起支持，省掉命名管道那一支），
  `run/` 目录权限 0700（Windows 上随用户目录的 ACL）。
- 每次启动生成随机令牌，写 `run/control.token`（0600），退出时删掉；请求不带对的令牌，一律拒绝并断开。
  CLI 从同一个档案目录读令牌，所以连得上的只有能读这个档案目录的本机用户。
- 一个档案目录只开一个通道：socket 已存在且连得通，说明这个档案已经有实例在跑，后起的不开通道并记一条警告；
  连不通是残留，删掉重建。

**CLI 一侧（`uiharu app …`）**
- `app start [--home <dir>]`：独立启动 Desktop（不挂在调用方的进程树上）并带 `--dev-control`，等 `app.ping` 通了才返回。
- `app call <op> [--args <json>] [--timeout <秒>]`：发一步，把回复原样打到标准输出，失败时退出码非零。
- `app run <script.jsonl> [--report <path>]`：逐行发，报告与 `--dev-report` 同形。
- `app quit`。
- 档案目录与应用同一套解析（`UIHARU_HOME` 优先），只连、不开档案。

## 实现

- Core `Core/DevControl/`：`DevControlEndpoint`（位置）、`DevControlServer`（监听、令牌、`app.ping`）、`DevControlClient`。不依赖界面，测试走真 socket。
- App `Features/DevAutomation/`：`DevStepExecutor`（一步怎么执行，脚本与通道共用）、`DevControlHost`（开关与 `app.ops` / `app.quit`，第一步前叫出主窗口一次）。
- CLI `Commands/App/`：`app start --exe <Desktop>`（或 `UIHARU_DESKTOP_EXE`）、`app call`、`app run`、`app quit`，都认 `--home`。
  `app start` 经 `nohup … </dev/null >/dev/null 2>&1 &` 起应用：标准输出若接着调用方的管道，调用方要等应用退出才收得到 EOF（段 20 Shell 挂死同一个坑）。
- 回复不转义中文（群流水要能直接读）；CLI 失败退出码非零：1 步骤失败、2 用法或通道没开、3 超时。

## 不做的

- **应用里的模型调这个 CLI**（自己建群、以第三方视角操作群聊）：通道做稳之后另议，要先想清楚自己的会话怎么挡、花钱怎么审批、哪些步骤开放给模型。
  ⚠️ 开发者模式下这件事其实已经**可能**发生：成员的 Shell 也是同一用户，读得到令牌。到那一步要一并收口（例如令牌按调用方区分权限）。
- **截图与键鼠**：坐标级操作，焦点、坐标、时序全靠猜，`DevScriptRunner` 当初就因此选了意图级步骤。真需要看画面的界面验收再单独议。

## 怎么验

用它跑一段长跑：`app start` 起应用、`app call group.away.start` 开离席，中途 `app call group.post` 插一句、`group.away.end` 后换目标重开一次（与用户改目标同一条路），
不重启跑第二段，最后 `app quit`。两段的内存读数在同一个进程里连续。
