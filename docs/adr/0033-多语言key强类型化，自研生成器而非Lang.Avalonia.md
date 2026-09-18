# 多语言 key 强类型化：自研生成器 + LangKey 枚举，XAML 零改动

多语言 key 的强类型化定版：**自研 Roslyn 生成器**（`UiharuMind.Localization/Generator`）编译期产出
`UiharuMind.Generated.LangKey` 枚举（identity mapping：成员名即资源 key），XAML 消费一行不改，
C# 消费渐进迁移到枚举；不用 Lang.Avalonia。

## 为什么

- **痛点真实存在**：1073 个扁平 resx key 全靠手写字符串（约 623 处 XAML + 99 处 C#），拼错无感知，
  死 key 分不清——静态扫描显示 282 个 key 从未被任何 XAML/C# 引用。
- **Lang.Avalonia 适配失败**：其 Analysis 生成器要求 key ≥3~4 段层级（`命名空间.模块.类.属性`），
  对扁平 key 直接**静默丢弃**（`parts.Length < 3 continue`），等于要求 1073 个 key 全部人工语义重排；
  不提供死 key 报告；库零测试、`UpdateLog` 版本号滞后；resx 插件默认反射扫描与项目类型不匹配。
  结论：接库便宜，强类型贵，而强类型的贵与接谁无关——真正的问题是给扁平 key 提供编译期类型。
- **Avalonia 实测限制**：markup extension 位置参数不支持 string→enum 编译期转换（`AVLN3000`），
  所以「XAML 原样 + 枚举构造」路线堵死；编译期检查只能来自生成器侧诊断，或 `{x:Static}` 常量。

## 决定

1. **自研生成器**：读 `Resources/Lang/*.resx` 与全部 `*.axaml`（AdditionalFiles），
   产出 `LangKey` 枚举；管道格式无关，未来加 JSON/XML 存储只需新增 Parser。
2. **XAML 消费保持 `{loc:Loc X}` 零改动**；拼写错误由生成器诊断 **LK2001（Error）** 拦截
   （上线即抓到 `ConversationView.axaml` 引用了不存在的 `Settings` key）。
3. **C# 消费渐进迁移**：新增 `LocalizationManager.GetString(LangKey)` 与 `Loc.Text(LangKey)` 重载；
   静态字面量改 `LangKey.X`；变量 key 升成 `LangKey` 类型（如 `ShortcutEditItem.TitleKey`）；
   动态拼接 key（`$"AgentMcpState{status.State}"` 一类）保留 string 重载。
4. **诊断**：LK1001 解析失败 / LK1002 非法标识符 / LK1003-1004 卫星文化与默认文化 diff /
   LK2001 XAML 拼写 / LK2002 死 key（Warning，含动态拼接/变量疑点需人工分类）。
5. **宿主形态**：`UiharuMind.Localization` 是宿主（多语言运行时逻辑后续迁入），
   `Generator/` 是其子项目，对齐 Lang.Avalonia 的「核心库 + Analysis 生成器」形态，
   实际工程只负责使用。

## 被否方案

- **Lang.Avalonia 全家桶**：key 层级重排成本巨大 + 静默丢弃扁平 key + 无死 key 报告 + 零测试。
- **常量类 + `{x:Static}`**：编译期检查成立，但 623 处 XAML 全部要套两层嵌套，维护体验差。
- **枚举构造 + string→enum**：Avalonia 不支持（实测 AVLN3000）。
- **纯 analyzer（只校验字符串不换类型）**：能拦拼写，但 C# 侧没有真强类型、无重构支持，
  与「要强类型」目标差半档。

## 影响

- XAML 迁移成本为零；C# 调用点按需渐进迁移（静态→`LangKey.X`，变量 key→`LangKey` 类型，
  动态拼接保留 string）。
- 生成器是自有资产（约 500 行 + 13 个单测），死 key 报告每次编译自动给出。
- 已知边界：XAML 侧检查是「字符串对照枚举」而非符号绑定，无 IntelliSense/改名重构；
  死 key 清理依赖 LK2002 + 人工分类（动态前缀家族保留）。
