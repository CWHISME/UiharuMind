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

    /// 目录段：工作目录、草稿目录、记忆目录合在一个标题下，三条各按装配条件出现。
    public static string BuildDirectories(bool hasWorkingDirectory,string workingDirectory, string? draftToken, string? memoryToken,
        bool forSubAgent = false, bool shellMounted = false)
    {
        StringBuilder sb = new();
        if (hasWorkingDirectory)
        {
            sb.AppendLine($"你当前的工作目录是【{workingDirectory}】");
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
                : "给用户看的也可以放这里，文件链接在回复中需要使用 Markdown 格式才能被解析，如图片：![]($DRAFT/文件名)、文件：[]($DRAFT/文件名)。");
        }

        if (memoryToken != null)
        {
            // 只交代事实不立规矩:首行会进索引,模型知道了这个代价,自然会把首行写成摘要、少记琐事
            sb.AppendLine($"- 记忆目录 {memoryToken}：你在本工作区跨会话保留的笔记，按主题一个 .md 文件。" +
                          $"`Read {memoryToken}` 会返回记忆索引，每份笔记的路径、修改日期和首行（超过 {MemoryIndex.SummaryMaxLength} 字截断），读全文用 {memoryToken}/文件名。" +
                          "开始一件事前先看一眼索引，以前的你可能记过相关的东西。" +
                          "索引里只看得到首行，" +
                          "只写个标题的话，以后的你得打开文件才知道里面是什么。只记对话里的沉淀。" +
                          // 实机见过:拿过时的进度记忆当事实答给用户,被指出后才去翻 git log
                          "记忆里带时间的状态（进度、计数、某事是否已做）说出口前先用 git log 或现场数据核一遍，过期的顺手删掉。" +
                          // 实机见过:写了更正笔记却没改旧笔记,两份并存,下一个读到旧那份的又照着错(记忆是工作区内各成员共用的)
                          $"推翻某条结论时，先 `Grep` {memoryToken} 找出还记着旧结论的笔记，把仍有用的内容并进更正后的笔记，" +
                          (shellMounted ? "旧文件用 `Shell` 删掉。" : "旧笔记的首行改成「已过时，见 新笔记名」。"));
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

    /// 命令行工具纪律段正文。按<b>文件工具是否也在场</b>拼：
    public static string BuildShell(bool fileAccessMounted, string shellBinary)
    {
        StringBuilder sb = new();

        sb.AppendLine("- 用 `Shell` 运行的命令默认在工作目录下执行。");

        if (fileAccessMounted)
        {
            sb.AppendLine(
                "- 文件系统层面的操作——移动、改名、删除、新建目录——用 `Shell` 做。");
            // 刻意不举 cat/sed/awk 这些具体命令:框架按平台解析 shell(bash/sh/PowerShell/cmd 四种),
            sb.AppendLine(
                "- 但文件内容的读与改默认仍走文件工具：读取用 `Read`，" +
                "单点改动用 `Edit`（每次改动可见、可审计）。例外是贯穿多个文件的同一种批量机械改动，" +
                "那种情况优先用 `Shell` 或脚本一次做完。");
        }

        return sb.ToString();
    }

    /// 受管 Python 环境纪律段。
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
    /// 怎么查、怎么调归两把元工具的描述，这里只给一个指向
    /// </summary>
    /// <param name="extensions">扩展：名字与说明（说明可空）</param>
    /// <returns>段落正文</returns>
    public static string BuildExtensions(IReadOnlyList<(string Name, string Description)> extensions)
    {
        StringBuilder sb = new();
        sb.Append("以下能力不在你的工具列表里，用 `").Append(ExtensionCalls.HelpName).Append("` 查命令，`")
            .Append(ExtensionCalls.CallName).Append("` 执行：\n");
        foreach ((string name, string description) in extensions)
        {
            sb.Append("- ").Append(name);
            if (!string.IsNullOrWhiteSpace(description)) sb.Append("：").Append(description.Trim());
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }
}