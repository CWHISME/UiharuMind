# 模型 Shell 可切换：设置界面选壳，装配期固化

> **状态**：accepted（2026-10-24）。

## 背景

模型用 `Shell` 工具执行命令时，框架（`Microsoft.Agents.AI.Tools.Shell` 1.22.0-preview）在
Windows 上按 `pwsh` → `powershell` → `cmd` 的顺序从 PATH 兜底选 shell；本机没有 pwsh 时实际
落到 **Windows PowerShell 5.1**。而这个仓库的开发流程（构建、冒烟脚本 `src/scripts/smoke/run.sh`、
性能脚本 `switch-perf.sh`、macOS 的 `dev-run.sh`）全是 bash 语法，模型在 PowerShell 里照 bash
写命令会直接失败；Git Bash（本机已装）带完整 POSIX 工具，跨平台写法一致。

框架本身支持两种覆盖方式：`AGENT_FRAMEWORK_SHELL` 环境变量、`LocalShellExecutorOptions.Shell`
构造参数——但环境变量既不可见也不持久化，用户需要一个设置界面的入口。

## 决定

1. **配置只存一个字符串**：`AgentSettingConfig.ShellBinaryPath`（空 = 跟随系统默认）。预设
   下拉是「快捷填写路径」的 UX，不是另一个配置维度；任意自定义路径天然支持。
2. **装配期固化**：`AgentAssemblyPlan.Resolve` 读配置写入 `plan.ShellBinaryPath`，
   `AgentAssembler` / `SubAgentAssembly` 把它传给 `ShellExecutorFactory.Create`。装配保持
   「`Resolve` 唯一读单例与磁盘、`Assemble` 纯函数」的既有分割（ADR 0050 同款原则）。
3. **执行器只认输入**：`ShellExecutorFactory.Create(dir, env, shellBinaryPath?)` 非空则设
   `options.Shell`；空/null 走框架默认解析。后台任务、路径简写（`AgentPathResolver.ShorthandFor`）
   都从 `ResolvedShellBinary` 派生，自动跟随。
4. **设置界面**：智能体设置 → 常规页新增「模型 Shell」卡片（受管 Python 环境同款结构）：
   下拉 = 自动 + 平台探测到的常见预设（Windows：Git Bash / PowerShell 7 / Windows PowerShell /
   cmd.exe；POSIX：bash / sh，探测不到的列不出）+ 自定义路径；一个「探测」按钮报告当前实际
   生效的 shell；单项恢复默认。探测与预设目录在 `ShellPresetCatalog`（公共只读），口径直接读
   框架 `ResolvedShellBinary`，不复制 SDK 解析逻辑。常规页随迁入联网搜索与受管 Python 环境，
   原「能力开关」页撤并（见 [ADR 0091](0091-智能体设置页撤能力开关，常规页收纳全局能力.md)）。
5. **工具描述自动跟随**：`BuildDescription` 写的就是 `ResolvedShellBinary`，换壳后模型看到的
   shell 路径与语法提示随之改变。

## 取舍

- **枚举 + 自定义双态**：配置存枚举不如存路径直白——路径本身就是「选了什么」的完整事实，
  枚举还要再做路径映射与迁移，**否**。
- **直接设 `AGENT_FRAMEWORK_SHELL` 环境变量**：SDK 原生支持、零代码，但设置后要重启进程、
  对用户不可见、与配置体系（`AgentSettingConfig`）割裂，**否**。
- **在 `AgentAssembler` 里现读配置**：违反「装配不碰单例」的既有分割，也让测试没法显式传壳，
  **否**（选择在 `Resolve` 固化）。
- **shell 选择计入 `AgentAssemblyFacts` 触发自动重建**：换壳后旧会话仍可用旧 shell 跑完，
  强制重建会打断在跑会话，**否**；设置描述里写明「对新装配的会话生效」。

## 已知代价

- **已装配会话不生效**：改完设置，已在跑的会话沿用旧 shell 直到会话重建（切工作区/权限档/改卡）。
- **配错路径要启动才报错**：SDK 只在 `Process.Start` 时抛错，设置页探测按钮缓解——选了不存在的
  路径立即显示「当前生效：路径」而不会验证可执行，跑命令才失败。
- **Git Bash 的 MSYS 语义**：路径写法（`/c/...` vs `C:\...`）、环境变量继承与原生 Windows 不同，
  换到 Git Bash 后模型命令的行为以 bash 语义为准（这正是换壳的目的）。
- **仅全局单值**：不能按角色/会话配不同 shell；需求尚不明确，不做。

## 本期不做

- 按角色、按会话的 shell 覆盖。
- shell 存在性与可执行性的自动校验（探测只报告解析结果）。
- 换壳后对已装配会话的自动重建。
