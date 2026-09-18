# 动态家族与 key 载体全部 LangKey 化，allowlist 机制退役

i18n 强类型化第二程定版：把「运行时拼 key / 变量 key / 字符串 key 载体」全部收编成
`LangKey` 枚举引用（含 Core 侧错误码枚举化），随后 **`LangKeys.allowlist.txt` 静默名单整体退役**；
生成的 LangKey 枚举成员带 XML 文档注释（文案语言可用 `LangKeysCommentCulture` 指定，当前 zh-hans）。

## 为什么

- **第一程（ADR 0033）后仍剩三类「静态扫描不可见」的 key**：动态拼接
  （`$"AgentMcpState{status.State}"`、`"MemoryIndexStage" + stage`）、变量 key 数据表
  （`L("AgentCapabilityXxx")`、`MenuHeaderResourceKey = "MenuServicesKey"`）、三元/switch 直调字面量
  （`GetString(cond ? "A" : "B")` 里 `GetString(` 后面不是引号，生成器扫不到）。
  它们曾靠 allowlist 静默——治标，且 allowlist 本身会产生「误删却没人响」的风险面
  （此前已发生一次误删 55 个在用 key 的事故）。
- **动态 key 本质是「前缀 + 枚举值」**：与其保留运行时拼串，不如在调用点做类型化映射
  （`status.State switch { ... => LangKey.AgentMcpStateConnected }`），每个分支都是真·LangKey 引用，
  拼错/删 key 直接编译错误。
- **Core 侧错误码（MemorySource*）曾被判「App 够不着、只能 allowlist」**：实际解法是
  把错误码本身升成枚举（`EMemorySourceError`），App 侧再做「枚举 → LangKey」映射——不是做不到，是当初没想到把「码」和「文案 key」分成两层。
- **Avalonia 官方 `{x:Static}` 吐译文方案（外部方案文档方案 3）只两类子集可用**
  （不译静态值、必然重建的视图），主界面/长驻视图与我们的运行时切语言机制直接冲突；
  「IDE 改名传播优于嵌套 x:Static」无可靠证据（存疑）。本程不采用，记录在案防止将来重复评估。

## 决定

1. **动态家族类型化**：5 个动态拼接家族（AgentMcpState/AgentSettingSearchState/MemoryIndexStage/
   ThinkingMode/AgentTaskStatus）与三元/switch 直调字面量、`L("Key")` 助手、key 载体
   （RadialMenuModel/MemoryIndexUiText/_inputPlaceholderKey/SetStatus/CreateDefaultSessionName）
   全部改为 `Loc.Text(LangKey.X)` 或 `LangKey` 类型字段/参数。映射 switch 带 `_` 兜底（默认档文案），
   新枚举成员会静默落默认档——接受此取舍，靠代码审查兜底。
2. **Core 错误码枚举化**：`MemorySourceReadResult.ErrorCode` 从 string 改为 `EMemorySourceError?`，
   App 侧 `GetSourceErrorKey(EMemorySourceError?) → LangKey.MemorySource*`。
3. **allowlist 退役**：删文件 + csproj 条目 + 生成器 allowlist 代码 + 相关测试。
   今后新增 key 未使用会被 LK2002 直接点名，**不再有静默通道**。
4. **LangKey 成员 XML 文档注释**：注释用默认/指定文化的文案，方便 IDE 悬停预览；
   文化由 MSBuild 属性 `LangKeysCommentCulture` 指定（缺省用默认文化），缺 key 回退默认文案。
   转义铁律：文案 Trim → 控制字符折叠为单空格 → XML 转义，**禁止原始换行进 `///` 行注释**
   （此坑在第一程炸过一次编译）。

## 影响

- 全仓 C# 侧已无「运行时拼 key / 写死字符串 key」的反模式；生成器 LK2002 从 121 条降到 0。
- resx 从 1074 减到 961（含第一程删 159、误删恢复 55、第二程删 9 个真死）；`Lang.Designer.cs`
  仍带 1074 个属性（过期无害），留待运行时迁入宿主时退役。
- 未做：`MouseButtonOption._displayNameKey` 小尾巴、LocExtension 反射 Binding → AOT 化
  （改法 B：IObservable.ToBinding()，独立专项）。
