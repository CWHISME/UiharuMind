# 内置角色卡改 markdown 存放，枚举缩为「代码点名」清单

内置角色卡的**人格正文**从 JSON 字符串里抽出来，改放 `Resources/Cards/<Name>.md`；元数据留在同名 `.json`。
卡片按**身份**分子目录：`Cards/Agents/`（自有角色与委派身份载体）、`Cards/Tools/`（内部技能角色）、
`Cards/Characters/`（普通角色），**IP 卡再按作品分一层**：`Cards/Toaru/`（某科学的超电磁炮）、
`Cards/DeathNote/`、`Cards/DrStone/`、`Cards/CodeGeass/`、`Cards/Danganronpa/`。
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
- **只有一层子目录**：`EnumerateCardBases` 取 `Cards.` 之后第一个点到末尾当 `CharacterId`，
  文件名里再带点就抛异常，所以 `Cards/Agents/Toaru/` 这种嵌套加载不了。
  IP 卡按作品分是**替换**掉 `Agents/` 这一层，不是叠在上面——一张卡只能待在一个目录里。
- `CharacterId` = 文件名，**所以搬目录不换 id**（文件名不动即可），老会话存档与老覆盖文件一律不受影响。
  这也是「按作品分」这件事风险极低的原因。
- 一个目录一种语义：作品目录一律是智能体，工具卡与普通角色各有各的目录。
  作品目录名在 `CardFolder_MatchesItsIdentity` 里显式列出，好过打错一个目录名悄悄长出一张错位的卡。
- `DefaultCharacterResourceTests` 的穷举改成「扫描结果」，另留一条「每个枚举成员都装载到了卡」的兜底。
- 内置卡的功能（工具/技能/MCP）在编辑页对 `IsDefaultCharacter` 一律禁用，只允许改人格、名字、头像与温度。
