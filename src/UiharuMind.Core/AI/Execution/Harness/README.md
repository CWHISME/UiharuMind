# Microsoft.Agents.AI（MFA）绕坑清单

本目录收容**纯粹因框架限制而存在**的垫片；散在别处、无法搬进来的绕坑代码
一律带 `[MFA绕坑]` 标记注释（`绕:什么 因:为何 删除条件:何时可删`），
升级框架版本时 `grep "\[MFA绕坑\]"` 逐条复查。

当前框架版本：`Microsoft.Agents.AI(.Harness) 1.16.0` 正式版（Tools.Shell/Mcp 仍为 1.16 preview/alpha；PrivateAssets，止步于 Core）。
边界契约：框架类型不外流出 `ICharacterRunner` 实现与本目录；框架破坏性变更时需要重写的只有这些。

> **1.13→1.16 升级复查记录（2026-07-31）**：破坏性变更五处已适配——审批规则改收
> `ToolAutoApprovalRuleContext`、`DisableFileAccess` 移除（文件工具只随 `FileAccessStore` 出现）、
> shell 改为 `AsAIFunction()` 普通工具挂载（默认名 run_shell、默认自包审批）、
> `AgentModeProvider`/`MessageInjectingChatClient` 转异步 API。
> ⚠️ 复查方法教训：框架的 XML 文档把 internal 成员也生成了文档,"XML 里有"≠public,以编译为准。

## 本目录（垫片文件）

| 文件 | 绕的什么 | 删除条件 |
|---|---|---|
| `MfaLogger.cs` | 框架内部日志（含工具执行失败的真实异常）默认无处可去，只能实现 ILogger 转发到自有日志 | 框架提供直接日志回调 |
| `MfaLoggerFactory.cs` | 框架经 ILoggerFactory 索取日志器 | 同上 |
| `MfaServiceProvider.cs` | 框架中间件只认 IServiceProvider，为一个日志器不值得引入完整 DI 容器 | 框架提供轻量注入口 |

> `MfaFileEditor.cs`（框架 `FileEditor` 的 replace/replace_lines 逻辑复制品）已删除：
> 编辑语义改为自持（`FileEditPlanner`，见 ADR 0007），不再等框架把那个类公开——
> 框架那套语义（盲改行号 / 单处精确替换）正是我们要摆脱的东西。

## 散点（带 [MFA绕坑] 标记）

| 位置 | 绕的什么 |
|---|---|
| `AgentHost.BuildRoleplayOptions` | 角色扮演档必须逐项关闭框架全部能力，漏一项即向上下文注入内容（不变量由 `RoleplayZeroInjectionTests` 钉住） |
| `Tools/Files/PermissiveFileAccessTools.cs` | 框架 FileAccessProvider 拒绝一切绝对路径且为 internal，只能整套自建文件工具 |
| `SessionChatHistoryProvider.IsOwnedByUs` | 框架注入消息混进待持久化列表，靠 `_attribution` 标记过滤（`HistoryAttributionTests` 钉住） |
| `MemoryContextProvider.ProvideAIContextAsync` | 回传 `context.AIContext` 会使消息翻倍、系统提示拼接两次，只能返回自己的净产出 |

## 已知接受的框架行为（非绕坑，记录在案）

- 附加状态（todos/mode/审批）的序列化 blob 可丢弃：损坏时降级为重建框架会话，
  历史的权威来源是自有会话文件（`SessionChatHistoryProvider` 的设计立场）。
- `HarnessAgentOptions` 的 Experimental 告警（MAAI001）在 Core 与 Tests 全局压制。
