# 长文本一律离开会话流，内核选 AvaloniaEdit

工具卡片上的大文本（工具结果、参数原文）不再有「就地展开全文」这条路。卡片**只**拿得到
一小段**预览**，**全文**一律在独立的 `FullTextWindow` 里看。

- 预览规则 `ToolResultTruncation`：**40 行 / 2KB**，先到者为准。结果与参数共用同一套。
- 全文窗内核是 `AvaloniaEdit.TextEditor`，包在 `LongTextView` 里。选它是为了它内建的
  **按行虚拟化**——`SelectableTextBlock` 与 `TextBox` 在 Avalonia 都没有文本虚拟化。
- 硬切规则 `LongLineWrapper`：单行超过 1000 字符就切开，喂给全文窗之前跑一遍。
- `LongTextView` 同时当 `StringContentEditWindow` 的内脏，全仓因此只有一个「吃得下大文本的文本控件」。

术语对是**预览 / 全文**：卡片上叫预览，窗里叫全文。

## 为什么

1. **上一轮（截断渲染）只削平了斜率，没消掉线性。** 会话流是裸 `ItemsControl`
   （`ConversationView.axaml:142`），没有虚拟化，每张卡片的文本布局都常驻。上一轮把单卡压到常数
   ~7-9ms，但卡片数仍线性——8ms × 几十张仍是几百 ms。降低**单卡常数**是当前唯一不动骨架的杠杆。

2. **16KB 那道闸对单行数据形同虚设。** 双轨阈值里行数轴拦不住 minified JSON / MCP 结果——
   它们本来就是一行。字符轴 16KB 是按「200 行视口」估出来的，宽了一个量级：
   16K 字符 `TextWrapping=Wrap` 每次布局都要重新断行，而 `ScrollViewer MaxHeight` 只裁**视口**、
   不裁**文本布局**。降到 2KB 直接把这笔钱砍掉 8 倍。

3. **「就地展开全文」这条路本身是错的，不是参数没调好。** 它把几十万字换进
   `SelectableTextBlock`，在没有虚拟化的流里等于当场冻住界面。无论阈值怎么调，只要这条路还在，
   用户点一下就还是会卡。删掉它，卡顿路径才是真的没了。

4. **参数原文此前零截断。** 转录器把参数原样 join（`ConversationTranscript.cs:121`），
   一次 `Write` 带几百 KB `content`，卡片一展开就直接进排版。这个洞跟结果那个是同一个病，
   之前只治了一半。

5. **弹窗不解决任何性能问题，内核才解决。** 480KB 塞进 `SelectableTextBlock`，在窗口里一样要几秒。
   Avalonia 里有虚拟化的只有 `ListBox` / `ItemsRepeater` 的**条目**层。AvaloniaEdit 把这件事
   在文本层做完了，还白送搜索、行号、跨行选中、换行开关。

## 取舍

- **没有自建「按行切条目 + 虚拟化列表」。** 那条路零依赖、样式天然跟仓内一致，但代价是
  **跨行拖选复制没了**（`ListBox` 的选中是条目级，不是文本级），搜索要自己写，将来要编辑等于重来。
  工具结果最常见的动作恰恰是「选一段贴出去」。用 AvaloniaEdit 换回这些能力，代价是一个 ~1.3MB 的包。

- **`Avalonia.AvaloniaEdit` 12.0.0 与 Avalonia 12.0.4 对版，这一点是先验证再设计的。**
  它有 `net10.0` 目标、依赖 Avalonia 12.0.0。主题只挂
  `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml`，**刻意不挂 `<FluentTheme />`**——
  那会与 `SemiTheme` 打架。App.axaml 里原有的两条注释掉的 StyleInclude 就是上一次尝试留下的。

- **不挂 FluentTheme 的代价是要自己补六个 Fluent 资源键，这笔账当时没算到。**
  症状极不友好：编译期一声不吭，点开搜索面板那一刻抛
  `KeyNotFoundException: Static resource 'ControlContentThemeFontSize' not found`。
  把 AvaloniaEdit 全套主题（`Themes/Fluent/AvaloniaEdit.xaml` → `Base.xaml` →
  `TextEditor` / `Editing/TextArea` / `Search/SearchPanel` / 三个 CodeCompletion）过了一遍，
  它期待宿主提供的**只有九个键**，Semi 提供了其中三个：

  | 键 | 谁提供 | 用途 |
  |---|---|---|
  | `ToolTipBackground` / `ToolTipForeground` / `ToolTipBorderBrush` | Semi | 补全提示框 |
  | `ControlContentThemeFontSize` / `ContentControlThemeFontFamily` | **我们补** | 搜索面板字号与字体 |
  | `ToolTipBorderThemeThickness` | **我们补** | 补全提示框边框 |
  | `SystemChromeMediumColor` / `SystemBaseLowColor` / `SystemAccentColor` | **我们补** | 搜索面板底/边框、编辑区选中底色 |

  分两处放是有原因的，不能合并：前三个是 `StaticResource`（**解析期**求值），必须住在
  `Application.Resources`，补进 `Application.Styles` 够不着；后三个是 `DynamicResource` 的
  `Color`，放进 `Assets/ThemeColors.axaml` 的六套主题变体里，才能跟着深浅色走。
  Semi 那三个**刻意不重定义**——它们在 `Application.Resources` 里会盖掉全应用的 tooltip 配色。

  `SystemAccentColor` 给的是**半透明**值（`#66` 前缀）：它在 AvaloniaEdit 里唯一的消费者是
  `TextAreaSelectionBrush`，而那是画在文字**底下**的，不透明色会把选中区的字盖死。

- **没有把 `StringContentEditWindow` 合并掉。** 它的三个调用方（会话改标题、AutoClick 改名、
  改对话消息）全是**模态 + 有返回值 + 确定/取消**，而全文窗是**非模态 + 无返回值 + 可多开**。
  塞进一个类就得同时长出模态/非模态、有/无返回值、单/多实例三根开关，调用方还得自己记得
  「这次该不该等返回值」。共享的东西其实是**窗口里那个文本控件**，不是窗口——复用单位下沉一层，
  两个外壳各自保持自己的生命周期语义，改名那三处还白得了大文本能力。

- **预览规则与硬切规则刻意分开，不合并成一条。** 看着像同一件事（都是"别让文本撑爆布局"），
  但约束方向相反：预览吃的是 `Wrap` 成本（正比于**字符总数**），硬切吃的是单行测量成本
  （正比于**单行长度**，因为 AvaloniaEdit 按行虚拟化、且默认关自动换行）。合并会让两个常数
  互相绑架——调预览的宽度会莫名其妙改变全文窗的分行。

- **diff 那一支一个字没改。** 曾经打算把它也收进 40 行 + 全文窗，查下来是伪需求：
  Core 侧 `PermissiveFileAccessTools.MaxEditDiffLines = 80` 已经把回给模型的 diff 文本封死在
  80 行，而卡片上的 diff 是从那段结果正文**认回来**的（`ParseToolResult` 要求以 `"Applied "` 开头，
  只有 `Edit` 走这条路）。所以工具卡的 diff 最多 81 行，`DiffLineView.MaxDisplayLines = 300`
  在这条路上是死常数。真正吃 300 行的是**审批卡**（`BuildForToolCall`，从完整 plan 构建，
  不受 80 行管），那是另一件事。

- **↗ 入口常驻，不跟「有没有被截断」联动。** 「想放大看 / 想搜个关键字」跟「超没超阈值」无关。
  截断提示行仍然只在真被截时出现。

- **只预留了 `IsEditable`，没有做可切换编辑与写盘。** 文件搜索窗接进来当通用文本查看器是顺理成章的
  下一步，但写盘会带出编码检测、脏标记、关窗确认、大文件回写——那是「数据可丢」那一族风险，
  不该跟一次性能修复同批上车。

## 代价（明说）

- **多了一个 ~1.3MB 的依赖，且它的能力我们九成用不上**（折叠、代码补全、多种高亮）。
  换来的是不用自己维护文本虚拟化。

- **AvaloniaEdit 的默认配色是 VS 风，要向 Semi 手工对一遍，深浅两套。** 这块没有测试兜底，
  只能靠手测。它是本轮最可能出观感问题的地方。

- **`StringContentEditWindow` 换内脏影响三个既有调用方**，全部无测试。改会话标题那种单行短文本
  用带行号的编辑器会很怪，因此那边按短文本档配置（关行号、开自动换行）——等于同一个控件有两种手感，
  靠调用方配对。

- **工具结果全文仍然常驻内存**（`ToolCallItem.ResultText`）。卡的是排版不是内存，而且全文窗要打得开
  就得随取随有。几十个大结果 = 几十 MB 这件事本轮不管，记在 leftover。

- **中等大小的结果（几 KB 的 grep 输出、目录列表）现在也会被截**，比以前更频繁地要点一下 ↗。
  这是把「卡片是用来扫一眼的」这句话当真之后的必然结果。
