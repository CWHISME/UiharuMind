/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Agents.AI;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.AI.Execution.Prompts;

/// <summary>
/// 工具纪律段的正文。段落标题由装配侧统一加，这里只管正文。
///
/// 曾经支持在设置页逐段覆盖，已退役：那四段调的是「模型怎么用工具」而非角色人格，
/// 用得极少却让排查要看两处，还多养一个装配快照字段(见 ADR 0003)。
///
/// <b>全段中文</b>，且只有中文一份(见 ADR 0017)。不做「跟界面语言切换」的双份：
/// 两份平行散文没有任何测试能校验它们说的是同一件事，必然走样。
/// 用户要英文，改角色卡或片段库即可——那两处本来就是可编辑的。
/// </summary>
public static class AgentToolPrompts
{
    
    /// <summary>
    /// 并行调用的护栏句。装配侧开了 <c>AllowConcurrentInvocation</c>,同一回复内的多个调用不再按序执行,
    /// 先写后读、先改后编译、同文件多处改这类隐含顺序的组合会乱。不在代码层按工具分档串行,
    /// 唯一防线是让模型自己拆轮——所以挂在工具纪律段最前。
    /// </summary>
    public const string ConcurrentCalls =
        "同一条回复里的多个工具调用会同时执行。彼此有先后依赖时(先写后读、先改后编译、同一文件多处修改)分成多轮，每轮只发不互相依赖的调用。";

    /// <summary>
    /// 智能体的工作循环段：先弄清事实、边做边说、失败换路、收尾总结。弱模型最依赖这几条。
    ///
    /// 这段<b>不进 harness 层</b>，而是作为角色提示词的一部分（内置智能体的存档里就写着它，
    /// 新建智能体档角色时预填这一段，片段库里也有一份可随时插回）。
    /// 曾经的做法是把框架的 <see cref="HarnessAgent.DefaultInstructions"/> 拼在 harness 段开头，
    /// 那样有两个毛病：用户在界面上看不到每轮都发出去的这段话；而且框架那段的第一句是
    /// "You are a helpful AI assistant..."，harness 段又排在角色段之前，于是每个智能体的人格
    /// 都要先跟一句"你是通用助手"抢身份。见 ADR 0004。
    ///
    /// 标题用一级：与角色卡里的 Task、Style 两节同级——它现在是角色提示词的一节。
    /// </summary>
    public const string AgentWorkLoop =
        "# 工作循环\n" +
        "- 先把事实弄清楚再动手，不凭印象操作。复杂的活拆成明确的步骤。\n" +
        "- 调用失败或返回了意料之外的东西，就换一条路，不要原样再试一遍。\n" +
        "- 收尾时简短总结：做了什么、发现了什么、下一步建议。";

    /// <summary>
    /// 工作目录段：这一段是事实而非建议。
    ///
    /// 这段曾经不存在:工作目录只被拿去构造工具,从没进过任何提示词。
    /// 后果是模型不知道根在哪,于是自己编一个占位路径(实机见过
    /// <c>Glob(pattern: "*.*", root: "/path/to/project")</c>),白烧一次工具调用。
    ///
    /// <b>2026-08 起明确表态「默认给相对路径」</b>，推翻了这里原先「刻意不表态相对还是绝对」
    /// 的自我约束(理由与推翻理由都见 ADR 0017)。当初怕的是三处各说各的、互相矛盾，
    /// 而矛盾已经消掉了：两个搜索工具的目录参数统一叫 directory，失败时还会回显解析后的绝对路径。
    /// 此时缺一句明确默认，反倒才是模型乱编绝对路径的来源——
    /// 绝对路径要求模型「知道」一个它其实不知道的前缀，相对路径它靠 `Glob` 就能查到。
    /// 路径不加反引号:反引号在提示词里专表工具名,有不变量测试按这条约定校验。
    /// </summary>
    /// <param name="workingDirectory">文件与 shell 工具的根目录绝对路径</param>
    /// <returns>提示词段落正文</returns>
    public static string BuildWorkingDirectory(string workingDirectory)
    {
        return $"你的工作目录是 \"{workingDirectory}\"。\n" +
               "路径默认给相对工作目录的相对路径；只有要碰工作目录之外的东西时才用绝对路径。";
    }

    /// <summary>
    /// 草稿目录段：会话自己的产出房间。不是 Python 专用的——测试脚本（含 py 文件）、
    /// 不该进项目的中间文件都放这里，不要散进项目里。
    ///
    /// 路径用双引号而目录名不用反引号：反引号专表工具名。
    /// </summary>
    /// <param name="roomDirectory">房间绝对路径</param>
    /// <param name="forSubAgent">
    /// 是否给子代理用。子代理与派活者<b>共用同一间房</b>，但它的正文不进用户对话——
    /// 交出去的是一份报告，展示归派活者。给它 markdown 图片语法只会让它写出一段
    /// 没人渲染的引用，而派活者真正需要的是一个能直接转引的绝对路径
    /// </param>
    /// <returns>提示词段落正文</returns>
    public static string BuildOutputRoom(string roomDirectory, bool forSubAgent = false)
    {
        StringBuilder sb = new();
        sb.AppendLine(
            $"你的草稿目录（免审批写入区）是 \"{roomDirectory}\"。测试、验证用的临时脚本（含 py 文件），" +
            "以及不该进项目的中间文件(例如临时 git clone 源码)，都放这里，不要散进项目里。\n" +
            "此路径必须逐字使用，不要改写或自造：写错位置（例如其它会话的产出房间）会被视为" +
            "跨会话写入而触发审批，无人确认时会一直等待。");

        if (forSubAgent)
        {
            sb.Append(
                "- 要给人看的文件（图表、导出的数据），同样放这里，" +
                "并在结论里写出它的绝对路径——展示由派活方负责，你只管产出和报路径。");
            return sb.ToString().TrimEnd();
        }

        // 要给用户看的文件是这段的另一半:对话正文按 markdown 渲染,本地文件图片
        // 走 file:// 才加载得出来。前缀直接给出,不让模型自己拼 URI
        // (Windows 上 C:\a\b 要变成 file:///C:/a/b,反斜杠与盘符两处都得改)。
        // 引用就用裸图:渲染库给图片设了 HRef,点得开,不必再包一层链接
        string uriPrefix = ToFileUriPrefix(roomDirectory);
        sb.Append(
            "- 要给用户看的文件（图表、导出的数据），同样放这里。正文里照这个格式引用它：" +
            $"![说明]({uriPrefix}文件名)。只报一句文件名、或者路径写到别处，" +
            "对话里就什么都不会出现。");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 记忆目录段（ADR 0028）：agent 在本工作区跨会话保留的笔记，落在工作区家目录的
    /// <c>Memory/</c> 子目录，用普通文件工具读写——没有专门的记忆工具。
    ///
    /// 开场是否查看由模型自决：记忆是「需要时才看」的资源，不进工作循环当必做步骤。
    /// 内容边界：只记对话里的沉淀，不镜像 repo——代码/文档/git 以文件为准，再抄一份就会两份漂移。
    /// `Shell` 那句只在命令行工具在场时出现（提示语指名的工具必须真的在同一份工具集里）。
    /// </summary>
    /// <param name="memoryDirectory">记忆目录绝对路径</param>
    /// <param name="shellMounted">命令行工具是否已装配</param>
    /// <returns>提示词段落正文</returns>
    public static string BuildMemory(string memoryDirectory, bool shellMounted)
    {
        StringBuilder sb = new();
        sb.AppendLine(
            $"你的记忆目录是 \"{memoryDirectory}\"。这是你在本工作区跨会话保留的笔记：" +
            "按主题一个 .md 文件（例如 prefs.md、decisions.md），需要时先 `Glob` 再 `Read`，" +
            "开场是否查看由你自己判断。只记对话里的沉淀（偏好、决策理由、踩坑、进行中的任务状态），" +
            "不要重复 repo 里已有的内容——代码与文档以文件为准，再抄一份就会两份漂移。");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 文件工具纪律段默认正文。
    ///
    /// 两组内容，缺一组都会有实机症状：
    /// <list type="number">
    /// <item>找文件的顺序（Glob/Grep/Read）——不说模型就上来先问用户"文件在哪"。</item>
    /// <item>上下文卫生——不说模型会把整个文件、整棵目录树拉进来。</item>
    /// </list>
    ///
    /// 写那一半在 <see cref="FileWriteDefault"/>。<b>分开是因为只读装配也要读</b>：
    /// 探索档子代理有 `Glob`/`Grep`/`Read` 却没有 `Edit`/`Write`，从前两半并成一段，
    /// 它要么整段拿不到（连上下文卫生都没有），要么整段拿到（被指名一个不存在的工具）。
    ///
    /// 这里<b>不再重复一句"不知道在哪就先搜"</b>：工作目录段结尾原先有同义的一句，
    /// 两段又是紧挨着发出去的，对小模型那不是强调而是噪声。留具体的那一句（点名了 `Glob`）。
    ///
    /// 也<b>不说 isRegex 怎么传</b>：非法正则已在工具边界自动降级为字面串搜索，
    /// 那件事写在参数说明里(只在装配 Grep 时付费)，写进这段则是每轮付费、零收益。
    ///
    /// 参数名（edits / oldString / offset / limit / contextLines）刻意<b>不加反引号</b>：
    /// 反引号在提示词里专表工具名，有不变量测试按这条约定校验。
    /// </summary>
    public const string FileReadDefault =
        "- 用 `Glob` 找文件，用 `Grep` 搜文本。\n" +
        "- 位置不清楚就先跑一次 `Glob`，不要回头问用户。\n" +
        "- 上下文是你最稀缺的资源。绝不要把一整个大文件、未经过滤的目录清单、或者一次宽泛搜索的结果整个拉进来。\n" +
        "- 已经知道关键词，就先用 `Grep` 带上 contextLines 搜一次——" +
        "命中行加上它的上下文，往往就是你需要的全部。否则用 offset 和 limit 只 `Read` 需要的那一段。\n" +
        "- 需要完整理解整个文件时，用 `Read` 传 limit=-1 一次读完。";

    /// <summary>
    /// 文件<b>修改</b>纪律段默认正文。只在写工具真的在场时发（见 <see cref="AgentPromptHeadings.FileModifications"/>）。
    ///
    /// 「先 `Read` 再改」留在这一侧而不是读那一侧：它是 <c>Edit</c> 的前置条件，
    /// 没有写工具时说它等于指一件做不了的事。
    ///
    /// 「也不要读太少」那句刹车同理挂在这里——它的理由整条都是 oldString 要精确匹配。
    /// 只说省着读、不说这句，模型会只读二十行就动手，匹配失败再重试，一轮下来烧的比老实读一段更多。
    ///
    /// 参数名（edits / oldString）刻意<b>不加反引号</b>：反引号在提示词里专表工具名，
    /// 有不变量测试按这条约定校验。
    /// </summary>
    public const string FileWriteDefault =
        "- 要改一个文件，先 `Read` 它。\n" +
        "- 改动已有文件用 `Edit`。`Write` 只用来新建文件，或者整体替换掉一个文件。\n" +
        "- 对同一个文件的所有改动放进一次 `Edit` 调用里，作为 edits 的多个条目。\n" +
        "- 每个 oldString 匹配的是文件当前的内容，不是你在同一次调用里前面那些条目的结果。" +
        "在仍然唯一的前提下把它写得越短越好——不要为了把两处相隔很远的改动连起来，" +
        "就塞进一大段没有改动的行。\n" +
        "- 但也不要读太少：oldString 必须与文件完全一致，`Edit` 才会成功，" +
        "所以要动的那一段就老实读完。猜的代价比读的代价高。";

    /// <summary>
    /// 命令行工具纪律段正文。按<b>文件工具是否也在场</b>拼：
    ///
    /// 这一段曾经<b>整个不存在</b>：shell 是唯一挂了工具却零指示的能力。
    /// 后果实机见过：模型要把一个文件挪个位置，却用 `Write` 把 587 行内容重新输出了一遍——
    /// 因为文件纪律段那份清单里只有 Glob/Grep/Read/Edit/Write，
    /// <b>移动、改名、删除在清单里根本不存在</b>，它只能从有的东西里凑。
    /// 那次事后它自称是"思维惯性、不是提示词问题"，但模型对自己为何选了某个工具没有特权视角，
    /// 那是事后合理化：缺口在清单上，是我们没写。
    ///
    /// 「文件系统操作用 `Shell`」这句刻意<b>不写进文件纪律段</b>（<see cref="FileReadDefault"/> / <see cref="FileWriteDefault"/>）：
    /// shell 可以被关掉，而那一段在 shell 关掉时照样发出去，
    /// 于是会给模型指一个不存在的工具——违反「关掉的工具绝不出现」。
    ///
    /// 主代理与完全自动档的子代理共用这一段：子代理拿的是同一个 `Shell`。
    /// </summary>
    /// <param name="fileAccessMounted">文件工具是否已装配</param>
    /// <param name="shellBinary">实际解析出来的 shell 可执行路径；空串则不写那一句</param>
    /// <returns>提示词段落正文</returns>
    public static string BuildShell(bool fileAccessMounted, string shellBinary)
    {
        StringBuilder sb = new();

        // 前两条讲的是"这件事该归 Shell 还是归文件工具",没有文件工具时它们无从谈起,
        // 而且会指名 Read/Edit/Write —— 那三个只随 EnableFileAccess 出现。
        // 提示语指名的工具必须真的在同一份工具集里,有不变量测试按这条钉着
        if (fileAccessMounted)
        {
            sb.AppendLine(
                "- 文件系统层面的操作——移动、改名、删除、新建目录——用 `Shell` 做。");
            // 刻意不举 cat/sed/awk 这些具体命令:框架按平台解析 shell(bash/sh/PowerShell/cmd 四种),
            // 在 cmd 下这三个根本不存在,PowerShell 下也只有 cat 是别名——举错例子比不举更糟
            // 批量替换的例外放这里而不放文件纪律段:那段在 shell 关闭时照发,指一个不存在的段落
            // 会违反"指名的工具必须真的在同一份工具集里"。脚本替换要预检(先 Grep 确认范围)、
            // 要收尾验证(再 Grep 0 残留);审批上它过的是 shell 命令级审批,不是 Read/Edit 的内容级
            sb.AppendLine(
                "- 但文件内容的读与改默认仍走文件工具：读取用 `Read`，" +
                "单点改动用 `Edit`（每次改动可见、可审计）。例外是贯穿多个文件的同一种批量机械改动，" +
                "那种情况优先用 `Shell` 或脚本一次做完。");
        }

        // 工具名写在这条无条件的里:前两条会被文件工具缺席时整块跳过,
        // 而一节讲命令行的纪律却一次都不提 `Shell` 是说不通的
        sb.AppendLine("- 用 `Shell` 跑命令。命令在工作目录下执行，路径同样按相对工作目录给。");

        // shell 是按平台解析的,而模型对自己跑在哪种 shell 上一无所知,
        // 只能先试 ls 再试 dir —— 白烧一次调用。这一句比任何措辞调整都管用
        if (shellBinary.Length > 0)
        {
            sb.AppendLine($"- 你的 shell 是 {shellBinary}。命令要按它的语法写，不要照搬别的 shell 的写法。");
        }

        sb.AppendLine("- 输出会被截断，所以不要打印大文件、也不要列不加过滤的目录树。");
        // 破坏性操作只描述性质,不举 rm -rf / git reset --hard:那两个在 Windows 上都不成立
        sb.Append(
            "- 破坏性或不可逆的操作（递归删除、批量覆盖、丢弃未提交的改动、任何推向远端的动作），" +
            "先说清你要做什么，再做。");

        return sb.ToString();
    }

    /// <summary>
    /// 受管 Python 环境纪律段。
    ///
    /// <b>这一段不对应任何工具</b>——Python 由 <c>Shell</c> 跑，我们只是告诉模型它已经就位。
    /// 刻意不引入独立的代码执行工具，理由见 ADR 0019：两个执行面模型要二选一，
    /// 而它相对 shell 的增量抵不上那份代价。
    ///
    /// <b>不写解释器绝对路径</b>：环境已经通过 <c>PATH</c> 前置激活（见
    /// <c>PythonEnvironment.BuildActivationEnvironment</c>），裸 <c>python</c> 就是它。
    /// 早先那版把绝对路径写进这里，于是每次调用都要模型自己给一个含空格的长路径加引号
    /// ——忘一次就是一条断命令加一轮白烧。
    /// </summary>
    /// <param name="fileAccessMounted">文件工具是否已装配（决定教哪种写代码的方式）</param>
    /// <returns>整段正文</returns>
    public static string BuildPython(bool fileAccessMounted)
    {
        StringBuilder sb = new();

        sb.AppendLine(
            "- 你的 shell 里 python 与 pip 已经指向一个专供你使用的虚拟环境，不是系统 Python。" +
            "直接写 python、pip，不要去找解释器的绝对路径。");
        sb.AppendLine("- 缺第三方包就自己装：pip install <包名>。装进的是这个环境，不影响系统。");

        // 遮蔽的对冲句。PATH 是静默生效的,这一句拦不住每一次,但至少给了正确写法
        sb.AppendLine(
            "- 例外：工作区自己带虚拟环境时（.venv、venv 这类目录），" +
            "跑那个项目的代码要用它自己的解释器路径，别用裸 python——你手上这个环境没有它的依赖。");

        // 多行代码怎么送进去是按平台分岔的:heredoc 只在 POSIX shell 成立,
        // cmd 与 PowerShell 下根本没有。所以有文件工具时一律走"写成文件再跑",那是四种 shell 都成立的
        if (fileAccessMounted)
        {
            sb.AppendLine(
                "- 超过一行的代码，先用 `Write` 写成一个 .py 文件，再用 `Shell` 跑它。" +
                "不要把多行代码塞进命令行——引号和转义会被 shell 改写，出错了还看不出是哪一步坏的。");
        }
        else
        {
            sb.AppendLine(
                "- 命令行里塞多行代码容易被引号和转义搞坏。写不下就分成几个短的 -c 调用。");
        }

        return sb.ToString();
    }

    /// 目录的 file:// 前缀(带尾斜杠)。让模型自己拼 URI 会在 Windows 上翻车——
    /// C:\a\b 要变成 file:///C:/a/b,反斜杠与盘符两处都得改
    private static string ToFileUriPrefix(string directory)
    {
        try
        {
            string full = Path.GetFullPath(directory);
            if (!full.EndsWith(Path.DirectorySeparatorChar)) full += Path.DirectorySeparatorChar;
            return new Uri(full).AbsoluteUri;
        }
        catch (Exception)
        {
            return string.Empty; //路径非法时退化成空前缀,总比抛在装配路上强
        }
    }

    /// <summary>
    /// 识图工具纪律段默认正文。
    ///
    /// 附件格式<b>用双引号而不是反引号</b>：反引号在提示词里专表工具名，
    /// 有不变量测试按这条约定校验（「指名的工具必须真的在同一份工具集里」）。
    /// 这里从前写的是反引号——主代理那份校验不到（要真工具集才能比对），
    /// 子代理从前用的是另一句，于是这条违规一直活着，直到两档共用同一段才被撞出来。
    /// </summary>
    public const string VisionToolDefault =
        "- 附件是以 \"[Attached file: <path>]\" 的形式送到的。要看清一张图画的是什么，" +
        "就拿那个路径调用 `ViewImage`。绝不要靠文件名去猜。";

    /// <summary>
    /// 联网工具纪律段默认正文。措辞沿用子代理侧原有的那一句——它本来就只说了
    /// 「先搜再取正文」这一件事，而那恰好是两个工具的正确配合方式。
    /// </summary>
    public static readonly string WebAccessDefault =
        $"- 查网上的资料用 `{WebSearchTool.ToolName}`，" +
        $"再对看着有戏的结果用 `{WebFetchTool.ToolName}` 取正文。";

    /// <summary>知识库检索工具纪律段默认正文</summary>
    public const string KnowledgeSearchDefault =
        "- 要在用户挂给本次会话的文档里查东西，调用 `" +
        KnowledgeTool.ToolName + "`，给一个简短聚焦的查询——它是向量检索，关键词比整句话管用。\n" +
        "- 它返回若干段落，或者告诉你没有挂载知识库。没有挂载就直说，不要靠猜。";

    /// <summary>
    /// 委派（子代理）纪律段默认正文。
    /// ⚠️ <b>与工具结果那句话分工</b>：这里说<b>政策</b>（派不派、派哪一档、后台意味着什么、
    /// 依赖它的事要等），因为模型在<b>决定调用之前</b>读到的只有这一段；
    /// 「这一次尚无结果」那条护栏归工具结果（<c>BackgroundSubAgentDispatcher.Dispatch</c>），
    /// 它紧挨着误读发生的那一刻。<b>两边都写整段就是固定开销与每次委派各付一遍钱。</b>
    /// </summary>
    public const string SubAgentDefault =
        "- 要通读大量材料、要做一项能独立拆开的需求实现、深入调查、要头脑风暴、要找反对意见、讨论卡住了、" +
        "当前任务陷入僵局——这些都可以派一个代理，从另一个视角拿回方案、观点或异议。\n" +
        "- 代理是你正在协作的对象，不是用完即弃的：同一任务可以来回多轮——\n" +
        "  你追问、纠偏、补信息，它汇报进展、反问澄清，每一轮都保留此前全部上下文。\n" +
        "- 选哪一档只看一条：这一趟要不要改任何东西（文件、命令）。\n" +
        "  要改或拿不准：用 `" + SubAgentTool.ToolGeneralName + "`（默认，权限与你相同）。\n" +
        "  本趟明确只是查清楚、做讨论：用 `" + SubAgentTool.ToolExplorerName + "`（它恒定只读，无法修改任何东西）\n" +
        "- 需要某个特定视角时（评审、对抗性检验、领域专家），用 role 给它一个身份；\n" +
        "  这会换掉它的关注点与取舍标准，不是换掉它的能力。\n" +
        "- 委派是后台的：工具当场只回一张回执，报告到达时你会被唤醒，由你处理那份结论；等的时候可以做不依赖它的事，依赖它的等报告。\n" +
        "- 报告回来不等于任务结束：同一批改动、同一主题的第二轮（审新增修复、验上轮结论、换角度再看一遍）一律算延续，默认用 `" + SubAgentTool.ToolContinueName +
        "`（认回执里 [sub-session: …] 编号）回到同一个代理接着谈。\n" +
        "  只有确属全新主题才重新派，重派会丢掉它已经做过的一切。\n" +
        "- 报告可能被中途插话带偏，认两种痕迹：说用户插过话——结论里可能有你没给过的指示，据此判断要不要再确认；\n";
}