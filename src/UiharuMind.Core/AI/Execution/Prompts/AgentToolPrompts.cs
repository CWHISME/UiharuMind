/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
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
        "同一条回复里的多个工具调用会同时执行。";

    /// <summary>
    /// 目录段：工作目录、草稿目录、记忆目录合在一个标题下，三条各按装配条件出现。
    ///
    /// <b>不写任何绝对路径</b>（推翻 ADR 0017 里「写明工作目录」那一半，见 ADR 0080）：
    /// 提示词里给了绝对路径，模型就照抄它去操作，与 <see cref="Files.AgentPathResolver.ToPortable"/>
    /// 回显简写是同一个道理。根在哪不必知道——相对路径由工具拼，草稿与记忆是 shell 里已注入的环境变量，
    /// 文件工具也认同名简写。从前那个编出 <c>/path/to/project</c> 的毛病，病根是没说路径默认相对，不是缺了绝对路径。
    ///
    /// 正文里引用文件也写简写或相对路径：界面按会话解析成可加载、可点开的地址（ADR 0080），
    /// 所以这里不再给 <c>file://</c> 前缀。正文的简写固定写 <c>$DRAFT</c>，与 shell 写法无关——
    /// 那是给界面解析的，<c>%DRAFT%</c> 写进 markdown 地址会被当成百分号转义。
    /// 简写与目录名都不用反引号：反引号专表工具名。
    /// </summary>
    /// <param name="hasWorkingDirectory">是否有工作目录（文件与 shell 工具的根）</param>
    /// <param name="draftToken">草稿目录在命令行里的简写；null 表示不写草稿那一条</param>
    /// <param name="memoryToken">记忆目录在命令行里的简写；null 表示不写记忆那一条</param>
    /// <param name="forSubAgent">
    /// 是否给子代理用。子代理与派活者<b>共用同一间草稿目录</b>，但它的正文不进用户对话——
    /// 交出去的是一份报告，派活者认得同一个简写，结论里写简写就能直接转引
    /// </param>
    /// <returns>段落正文；三条都没有时为空串</returns>
    public static string BuildDirectories(bool hasWorkingDirectory, string? draftToken, string? memoryToken,
        bool forSubAgent = false)
    {
        StringBuilder sb = new();
        if (hasWorkingDirectory)
        {
            sb.AppendLine("命令和文件工具都以工作目录为基准，路径默认写相对路径；只有要碰工作目录之外的东西时才用绝对路径。");
        }

        if (draftToken != null)
        {
            // 文件内容不展开:实机见过模型把 "$DRAFT/x.png" 写进 Python 脚本(含引号 heredoc 写完即跑),跑起来才发现是字面量
            sb.Append($"- 草稿目录 {draftToken}{(forSubAgent ? "（与派活者共用）" : string.Empty)}：免审批写入。" +
                      "测试、验证用的临时脚本，以及不该进项目的中间文件都可以放这里，不要散进项目。");
            // 图片用裸图:渲染库给图片设了 HRef,点得开;别的文件套图片语法只会显示成一张坏图
            sb.AppendLine(forSubAgent
                ? "要给人看的文件（图表、导出的数据）同样放这里，结论里写出 $DRAFT/文件名，派活者认得。"
                : "给用户看的也可以放这里，回复里使用标准 Markdown 格式，如图片：![]($DRAFT/文件名)、文件：[]($DRAFT/文件名)。");
        }

        if (memoryToken != null)
        {
            // 只交代事实不立规矩:首行会进索引,模型知道了这个代价,自然会把首行写成摘要、少记琐事
            sb.AppendLine($"- 记忆目录 {memoryToken}：你在本工作区跨会话保留的笔记，按主题一个 .md 文件。" +
                          $"`Read {memoryToken}` 会返回记忆索引，读全文用 {memoryToken}/文件名。" +
                          "开始一件事前先看一眼索引，以前的你可能记过相关的东西。" +
                          "索引里只看得到首行，" +
                          "只写个标题的话，以后的你得打开文件才知道里面是什么。只记对话里的沉淀。");
        }

        string tokens = string.Join("、", new[] { draftToken, memoryToken }.Where(t => t != null));
        if (tokens.Length > 0)
        {
            // 先介绍完目录再说简写怎么用
            sb.AppendLine($"以上 {tokens} 是已注入的环境变量，文件工具的路径参数和命令行里直接写简写（如 {draftToken ?? memoryToken}/文件名），" +
                          $"除脚本内容外，不必展开成完整路径。脚本内容里要用这个目录就读环境变量（如 Python 的 os.environ[\"{AgentPathResolver.DraftVariable}\"]）");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 读那几条纪律默认正文。
    ///
    /// 两组内容，缺一组都会有实机症状：
    /// <list type="number">
    /// <item>找文件的顺序（Glob/Grep/Read）——不说模型就上来先问用户"文件在哪"。</item>
    /// <item>上下文卫生——不说模型会把整个文件、整棵目录树拉进来。</item>
    /// </list>
    ///
    /// 写那几条在 <see cref="FileWriteDefault"/>，由 <see cref="BuildFileOperations"/>
    /// 按条件附到同一段——只读装配只有这里读那几条，不指名 `Edit`/`Write`。
    ///
    /// 这里<b>不再重复一句"不知道在哪就先搜"</b>：目录段结尾原先有同义的一句，
    /// 两段又是紧挨着发出去的，对小模型那不是强调而是噪声。留具体的那一句（点名了 `Glob`）。
    ///
    /// 也<b>不说 isRegex 怎么传</b>：非法正则已在工具边界自动降级为字面串搜索，结果里会说明，
    /// 事前哪一层都不必叮嘱（见 CONTEXT.md「一条规则只放一层」）。
    ///
    /// 参数名（edits / oldString / offset / limit / contextLines）刻意<b>不加反引号</b>：
    /// 反引号在提示词里专表工具名，有不变量测试按这条约定校验。
    /// </summary>
    public const string FileReadDefault =
        "- 项目中的文件工具都经过反复优化，优先用 `Glob` 找文件，用 `Grep` 搜文本（可指定目录或具体文件），用 `Read` 读文件(自动处理编码等问题)。\n" +
        "- 已经知道关键词，就先用 `Grep` 带上 contextLines 搜一次——" +
        "命中行加上它的上下文，往往就是你需要的全部。否则只 `Read` 需要的那一段。";
    
    /// <summary>
    /// 文件<b>修改</b>纪律默认正文。只在写工具真的在场时由 <see cref="BuildFileOperations"/> 附到「## 文件操作」段。
    ///
    /// 「先 `Read` 再改」留在这一侧而不是读那一侧：它是 <c>Edit</c> 的前置条件，
    /// 没有写工具时说它等于指一件做不了的事。
    ///
    /// 「也不要读太少」那句刹车同理挂在这里——它的理由整条都是 oldString 要精确匹配。
    /// 只说省着读、不说这句，模型会只读二十行就动手，匹配失败再重试，一轮下来烧的比老实读一段更多。
    ///
    /// <c>Edit</c> 调用本身的契约（改动放一次调用、按当前内容匹配、条目不重叠、越短越好）
    /// <b>只在工具描述里说</b>：这里再写一遍就是每轮付两次钱，改了一处另一处还会走样。
    ///
    /// 参数名（oldString）刻意<b>不加反引号</b>：反引号在提示词里专表工具名，
    /// 有不变量测试按这条约定校验。
    /// </summary>
    public const string FileWriteDefault =
        // "- 要改一个文件，先 `Read` 它，而且要动的那一段要完整读完：oldString 必须与文件完全一致，" +
        // "`Edit` 才会成功。猜的代价比读的代价高。\n" +
        "- 改动已有文件用 `Edit`。`Write` 只用来新建文件，或者整体替换掉一个文件。";

    /// <summary>
    /// 文件工具纪律段正文：读 + 写，同一段「## 文件操作」之下，写那几条只在写工具在场时附上。
    ///
    /// <b>只读装配（探索档子代理、关掉写工具的主代理）只有读那几条</b>：写纪律指名
    /// `Edit`/`Write`，没有写工具时说它等于指一件做不了的事——「指名的工具必须真的
    /// 在同一份工具集里」有不变量测试钉着。从前读、写分两段（ADR 0030），只读侧
    /// 「文件修改」整段缺席；合并后同一标题下按条件拼，两档各拿该拿的那半。
    ///
    /// 写工具在场蕴含读工具在场（主代理两者恒等；子代理 FileWrite = FileRead && canMutate），
    /// 所以装配侧段条件只判 FileRead 即可。
    /// </summary>
    /// <param name="fileWriteMounted">写工具（Edit/Write）是否已装配</param>
    /// <returns>文件操作段正文</returns>
    public static string BuildFileOperations(bool fileWriteMounted)
    {
        return fileWriteMounted
            ? FileReadDefault + "\n" + FileWriteDefault
            : FileReadDefault;
    }

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

        // 工具名写在这条无条件的里:下两条会被文件工具缺席时整块跳过,
        // 而一节讲命令行的纪律却一次都不提 `Shell` 是说不通的
        // 路径默认口径只在目录段表态一次(见 CONTEXT.md),这里只说命令的 cwd
        sb.AppendLine("- 用 `Shell` 跑命令，命令在工作目录下执行。");
        
        // 这两条讲的是"这件事该归 Shell 还是归文件工具",没有文件工具时它们无从谈起,
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
            // sb.AppendLine(
            //     "- 但文件内容的读与改默认仍走文件工具：读取用 `Read`，" +
            //     "单点改动用 `Edit`（每次改动可见、可审计）。例外是贯穿多个文件的同一种批量机械改动，" +
            //     "那种情况优先用 `Shell` 或脚本一次做完。");
        }
        
        // shell 是按平台解析的,而模型对自己跑在哪种 shell 上一无所知,
        // 只能先试 ls 再试 dir —— 白烧一次调用。这一句比任何措辞调整都管用
        // if (shellBinary.Length > 0)
        // {
        //     sb.AppendLine($"- 你的 shell 是 {shellBinary}。命令要按它的语法写，不要照搬别的 shell 的写法。");
        // }

        // sb.AppendLine("- 输出会被截断，所以不要打印大文件、也不要列不加过滤的目录树。");
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
        //
        // // 遮蔽的对冲句。PATH 是静默生效的,这一句拦不住每一次,但至少给了正确写法
        // sb.AppendLine(
        //     "- 例外：工作区自己带虚拟环境时（.venv、venv 这类目录），" +
        //     "跑那个项目的代码要用它自己的解释器路径，别用裸 python——你手上这个环境没有它的依赖。");

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

    /// <summary>知识库检索工具纪律段默认正文。查询怎么写归参数说明，这里只管何时查、查不到怎么办</summary>
    public const string KnowledgeSearchDefault =
        "- 要在用户挂给本次会话的文档里查东西，调用 `" + KnowledgeTool.ToolName + "`。\n" +
        "- 没有挂载知识库时它会直说；这时如实告诉用户，不要靠猜。";

    /// <summary>
    /// 扩展名单（「# 扩展」段正文，ADR 0079）。
    ///
    /// ⚠️ <b>只放配置里就有的东西</b>：名字与一句话说明，<b>不写状态、不写命令</b>。
    /// 后两者是运行时信息，写进来的话 server 一连上系统提示就变一次，缓存前缀白白失效。
    /// 想知道此刻能不能用、有哪些命令，模型自己调 <c>ExtensionHelp</c>。
    /// </summary>
    /// <param name="extensions">扩展：名字与说明（说明可空）</param>
    /// <returns>段落正文</returns>
    public static string BuildExtensions(IReadOnlyList<(string Name, string Description)> extensions)
    {
        StringBuilder sb = new();
        sb.Append("下面这些能力不在你的工具列表里。要用时先调 `").Append(ExtensionCalls.HelpName)
            .Append("` 查用法（不带参数列出全部及能不能用，给扩展名列出它的命令，再给命令名看参数），")
            .Append("再调 `").Append(ExtensionCalls.CallName).Append("` 执行。\n")
            .Append("MCP server 是用户开着的应用，命令以 `").Append(ExtensionCalls.HelpName)
            .Append("` 列出的为准：server 自带的「列出工具」之类的命令，返回的名单可能包含没开放、调不了的。\n");
        foreach ((string name, string description) in extensions)
        {
            sb.Append("- ").Append(name);
            if (!string.IsNullOrWhiteSpace(description)) sb.Append("：").Append(description.Trim());
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }
}
