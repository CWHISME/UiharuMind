# UiharuMind.Localization

多语言子系统宿主项目，对齐 Lang.Avalonia 的「核心库 + 生成器」形态：
实际工程（App）只负责**使用**，多语言逻辑都收在这里。

```
UiharuMind.Localization/          ← 宿主：多语言运行时逻辑（LocalizationManager / Loc / LocExtension 等，后续阶段迁入）
  Generator/                      ← 编译期工具：强类型 key 生成器 + 使用诊断
```

## 当前状态

- `Generator/`：已投入使用的 Roslyn 生成器。
  - 读 App 项目的 `Resources/Lang/*.resx` 与全部 `*.axaml`（AdditionalFiles）。
  - 产出 `UiharuMind.Generated.LangKey` 枚举（identity mapping：成员名即资源 key）。
  - 诊断：
    - `LK1001` 资源文件解析失败（Error）
    - `LK1002` key 不是合法 C# 标识符（Error）
    - `LK1003`/`LK1004` 卫星语言与默认语言 key 集 diff（Warning）
    - `LK2001` XAML `{loc:Loc X}` 引用不存在的 key（Error）
    - `LK2002` 枚举成员从未被 XAML/C# 引用（死 key 候选，Warning）
- 宿主运行时：**尚未迁入**。当前 `LocalizationManager`/`Loc`/`LocExtension`/`LanguageUtils` 仍在 App 项目；
  迁移时需决策：LangKey 枚举生成在 App 程序集，宿主库只能提供 `GetString(string)` + 注入式资源 provider，
  App 侧再提供 `GetString(LangKey)` 薄扩展。

## 约定

- 生成枚举的根命名空间默认 `UiharuMind.Generated`，可用 MSBuild 属性 `LangKeysRootNamespace` 覆盖。
- 每个枚举成员上方会生成一行 XML 文档注释 `/// <summary>{默认文案}</summary>`，方便 IDE 悬停预览：
  文案取默认文化 resx 的 `<value>`，写入前会 Trim → 把换行/制表等控制字符折叠成单个空格 → XML 转义
  （`&` → `&amp;`、`<` → `&lt;`、`>` → `&gt;`）；文案为空/空白时不输出注释行。
- 注释文案的语言可用 MSBuild 属性 `LangKeysCommentCulture` 指定，例如：

  ```xml
  <PropertyGroup>
    <LangKeysCommentCulture>zh-hans</LangKeysCommentCulture>
  </PropertyGroup>
  ```

  生成器会改用 `Lang.zh-hans.resx` 的 `<value>` 写注释；对应文化文件缺失、或个别 key 在该文化缺译文时，
  回退默认文化文案。不设该属性则始终用默认文化（`Lang.resx`）文案。
- 管道格式无关：新增 JSON/XML 存储只需在 `Parsing/` 加一个 Parser。
