# 内置角色卡改 markdown 存放，枚举缩为「代码点名」清单

内置角色卡的**人格正文**从 JSON 字符串里抽出来，改放 `Resources/Cards/<Name>.md`；元数据留在同名 `.json`。
卡片按身份分子目录：`Cards/Agents/`（智能体）、`Cards/Tools/`（内部技能角色）、`Cards/Characters/`（普通角色）。
`DefaultCharacter` 枚举从「内置卡目录」缩成「**代码会按名字点名**的角色」清单；内容卡（白猫、白露、魔禁班底…）
由 `DefaultCharacterManager` 扫描 `Cards/`（递归子目录）装载，`CharacterId` = 文件名。

## 为什么

- **改人格不再难受**：原先人格是一段多行 markdown 被压成 JSON 里的一行 `\n` 转义串，手改极易出错。
  抽成 `.md` 后所见即所得。
- **枚举此前兼了三职**：加载清单 / `CharacterId` 命名空间 / 代码点名。前两职是**数据**，只有第三职需要强类型。
  拆开后加一张内容卡 = 丢一个文件，零代码。
- **改名要带迁移**：枚举名即 `CharacterId`，改名会断老会话存档与老覆盖文件。`BuiltInCharacterId` 提供
  旧名 → 现行名映射（只读不写），`CharacterManager.GetCharacterData` 与覆盖文件加载都经它归一。
  旧角色扮演卡 `UiharuKazari` 退役，**刻意不映射**——老会话落到哨兵 `None`。
- **内置头像按路径引用**：`CharacterData.CharacterIcon` 允许存 `avares://…/Avatars/<Name>.png`，
  `IconUtils` 按 `RequestedThemeVariant` 解析成 `Avatars/{Light|Dark}/<Name>.png`。base64（用户上传/导入）不受影响。

## 代价

- 内容卡不能 `nameof` 点名，只能用字符串 `CharacterId`——但它们本就不该被代码点名。默认智能体
  `ChenXiAgent` 例外：它是代码点名的，故留在枚举里。
- 子目录只是**分类提示**，身份以卡上的 `IsAgent` / `IsInternal` 为准；`CardFolder_MatchesItsIdentity`
  这条测试钉住两者不许打架。
- `DefaultCharacterResourceTests` 的穷举改成「扫描结果」，另留一条「每个枚举成员都装载到了卡」的兜底。
- 内置卡的功能（工具/技能/MCP）在编辑页对 `IsDefaultCharacter` 一律禁用，只允许改人格、名字、头像与温度。
