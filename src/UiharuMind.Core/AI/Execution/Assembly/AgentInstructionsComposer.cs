/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// agent 档整段系统提示的编排。子代理那份体例不同，在 <see cref="SubAgentAssembly"/> 里。
/// </summary>
internal static class AgentInstructionsComposer
{
    // 标题一律取自 AgentPromptHeadings：工作区规矩段与 MCP 自述段主代理与子代理逐字共用
    // ——子代理干的正是探查工作区的活、拿的是同一份 MCP 工具，不该是全场唯一不知道规矩的人。

    /// <summary>
    /// 按固定顺序拼出 agent 档的整段系统提示：
    /// 基座(所有角色共用、系统锁定) → 角色段(人格 + 用户卡 + 对话模板) → 工具纪律与工作目录 → MCP server 自述 → 工作区规矩 → 人格 coda(末尾回锚)。
    /// 角色段(人格)标题由装配层在卡无自带标题时补上（见 CharacterSection）；卡自带标题则归卡所有。
    ///
    /// <b>基座在人格之前</b>（文档 §7 组装顺序）：基座是「怎么当一个人」的底线，人格是这个人本身。
    /// 人格仍紧跟在基座之后——小模型要先知道自己是谁，再读一大段英文工具纪律。
    /// 这个顺序拿不到手过：框架只会把 <c>HarnessInstructions</c> 拼在角色段<b>之前</b>，
    /// 所以那一层弃用，整段自己拼(见 ADR 0005)。
    ///
    /// MCP 自述紧跟工具纪律：它讲的正是"这批工具怎么用"，与上一段是同一件事的延续；
    /// 而工作区规矩讲的是"这个项目怎么干活"，属于另一个层次，排在最后。
    /// 人格 coda 钉在更后：它是整段最后一个声音，吃结尾权重（静态版重锚，见提案 v8 §7.4）。
    /// </summary>
    /// <param name="characterPrompt">角色段(CharacterPromptBuilder 的产物)</param>
    /// <param name="config">智能体的能力配置(角色自带)</param>
    /// <param name="visionToolMounted">识图工具是否已装配</param>
    /// <param name="workingDirectory">工作目录绝对路径;空串则不写该段</param>
    /// <param name="workspaceInstructions">工作区 AGENTS.md 内容;空串则不写该段。
    /// 主代理与子代理统一只取有无(正文由模型按指针自读,见 <see cref="WorkspacePointerSection"/>);
    /// 指针方案若出现"没读就编"的 case,截断版正文可经 <see cref="WorkspaceSection"/> 切回</param>
    /// <param name="mcpInstructions">MCP server 自述(已按 server 分节);空串则不写该段</param>
    /// <param name="shellBinary">实际解析出来的 shell 可执行路径;空串则不写那一句</param>
    /// <param name="pythonInterpreter">受管 Python 环境的解释器路径。<b>只作闸门</b>——
    /// 空串则整段不写；非空时正文里也不印它，环境已由 PATH 前置激活</param>
    /// <param name="outputRoomDirectory">草稿目录绝对路径(会话自己的产出房间)；空串则不写该段</param>
    /// <param name="memoryDirectory">记忆目录绝对路径(ADR 0028)；空串则不写该段</param>
    /// <param name="delegationRoster">可委派名单正文；空串则不写那一节</param>
    /// <param name="personaCoda">人格 coda（<c>CharacterData.GetPersonaCoda</c> 的产物）；空串则不写该段</param>
    /// <param name="segments">
    /// 各段的分段清单，<b>拼接现场登记</b>。能力面板要按段报占用，而事后对整串按标题反切，
    /// 本方法一改标题那边就静默错。空段不入册（它本来也没发出去）
    /// </param>
    /// <returns>整段系统提示</returns>
    internal static string Compose(string? characterPrompt, AgentToolConfig config,
        bool visionToolMounted, string workingDirectory, string workspaceInstructions,
        string mcpInstructions, string shellBinary, string pythonInterpreter,
        string outputRoomDirectory, string memoryDirectory, string delegationRoster,
        string personaCoda, out IReadOnlyList<AgentPromptSegment> segments)
    {
        List<AgentPromptSegment> registry = new();
        StringBuilder sb = new();
        AppendSection(sb, AgentBasePrompts.Base, EPromptSection.Base, registry);
        AppendSection(sb, CharacterSection(characterPrompt), EPromptSection.Character, registry);
        AppendSection(sb, BuildToolDisciplines(config, visionToolMounted, workingDirectory, shellBinary,
            pythonInterpreter, outputRoomDirectory, memoryDirectory, delegationRoster),
            EPromptSection.ToolDisciplines, registry);
        if (mcpInstructions.Length > 0)
        {
            AppendSection(sb, McpSection(mcpInstructions), EPromptSection.Mcp, registry);
        }

        if (workspaceInstructions.Length > 0)
        {
            // 指针点名装配时实际存在的那个文件：装配方早知道是 AGENTS.md 还是 CLAUDE.md，
            // 含糊着写会逼模型先 Glob 消歧（还可能扫出嵌套目录里的同名说明文件）。
            // 工作目录段已给绝对路径，这里只需点名文件名，模型零搜索直接 Read。
            AppendSection(sb,
                WorkspacePointerSection(WorkspaceInstructionsLoader.ResolveFileName(workingDirectory)),
                EPromptSection.Workspace, registry);
        }

        // coda 单独立段：它就是人格的压缩，合计仍归「角色提示」档（见能力面板汇总口径）；
        // 但明细里不能再叫「角色提示」——首尾两段同名，用户分不清哪个是正文、哪个是末尾回锚
        AppendSection(sb, PersonaAnchorSection(personaCoda), EPromptSection.PersonaAnchor, registry);

        segments = registry;
        return sb.ToString();
    }

    /// <summary>
    /// 角色段：标题 + 角色卡正文。
    /// 标题只在角色卡<b>没有</b>自带一级标题时由装配层补 <c># 角色</c>——
    /// 默认卡 ChenXi 的段首本就是 <c># 角色</c>，裸卡补出来与它同名，恰好统一；
    /// 新建智能体预填的 <c># 工作循环</c> 也占着段首，那张卡有自己的段结构，同样不插。
    /// 裸卡（如新写的人格稿）才有这个缺口：基座第 4 条「以『角色』节为准」由此得到字面对得上的落点。
    ///
    /// 判的是<b>一级</b>标题：<c>##</c> 开头只是子节，代替不了父标题——不补的话，
    /// 整段按 markdown 结构读会挂到上一节（基座）名下，层级说的是一件与事实不符的事。
    /// </summary>
    /// <param name="characterPrompt">角色卡渲染正文（CharacterPromptBuilder 的产物）</param>
    /// <returns>标题 + 正文；正文为空时返回空串</returns>
    internal static string CharacterSection(string? characterPrompt)
    {
        if (string.IsNullOrWhiteSpace(characterPrompt)) return string.Empty;
        if (StartsWithLevelOneHeading(characterPrompt)) return characterPrompt;
        return $"{AgentPromptHeadings.Character}\n\n{characterPrompt}";
    }

    /// <summary>
    /// 人格锚点段：标题 + 回锚句。
    /// 标题不能省：裸贴在工作区指针后面时，按 markdown 结构读整句成了「工作区规矩」的一节，
    /// 层级说的是一件与事实不符的事（工具纪律段要求「# 工具」父标题是同一道理）。
    /// 卡自带一级标题时以卡为准，与 <see cref="CharacterSection"/> 同一口径，不重复插。
    /// </summary>
    /// <param name="personaCoda">回锚句（<c>CharacterData.GetPersonaCoda</c> 的产物）；空串则不写该段</param>
    /// <returns>标题 + 正文；正文为空时返回空串</returns>
    internal static string PersonaAnchorSection(string? personaCoda)
    {
        if (string.IsNullOrWhiteSpace(personaCoda)) return string.Empty;
        if (StartsWithLevelOneHeading(personaCoda)) return personaCoda;
        return $"{AgentPromptHeadings.PersonaAnchor}\n\n{personaCoda}";
    }

    /// <summary>
    /// 首行是否为一级 ATX 标题（<c># 标题</c>）。<c>##</c> 及更深的只是子节，
    /// 判成"自带标题"就会漏补父标题——而漏补的那段按 markdown 结构读会挂到上一节名下。
    /// 无空格的 <c>#foo</c> 按 CommonMark 不是标题，是普通文本，同样不能代替父标题。
    /// </summary>
    /// <param name="text">待判文本</param>
    /// <returns>首行是一级标题返回 true</returns>
    private static bool StartsWithLevelOneHeading(string text)
    {
        string trimmed = text.TrimStart();
        if (!trimmed.StartsWith('#')) return false;
        int hashes = 0;
        while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
        if (hashes != 1) return false;
        if (hashes == trimmed.Length) return true;
        char next = trimmed[hashes];
        return next == ' ' || next == '\t' || next == '\n' || next == '\r';
    }

    /// <summary>
    /// MCP server 自述段（主代理与子代理逐字共用）
    /// </summary>
    /// <param name="mcpInstructions">已按 server 分节的自述正文</param>
    /// <returns>整段文本</returns>
    internal static string McpSection(string mcpInstructions)
    {
        return $"{AgentPromptHeadings.Mcp}\n\n{mcpInstructions}";
    }

    /// <summary>
    /// 工作目录段。<b>两种装配形态共用这一段正文</b>，只有标题级别不同：
    /// 主代理里它是 <c># 工具</c> 的一个分项（工作目录正是给那些工具用的根），
    /// 子代理里没有那个外层，它自己就是一个顶级段。
    /// </summary>
    /// <param name="workingDirectory">工作目录绝对路径</param>
    /// <param name="heading">标题级别前缀（<c>"#"</c> 或 <c>"##"</c>）</param>
    /// <returns>整段文本</returns>
    internal static string WorkingDirectorySection(string workingDirectory, string heading)
    {
        return $"{AgentPromptHeadings.WorkingDirectory(heading)}\n{AgentToolPrompts.BuildWorkingDirectory(workingDirectory)}";
    }

    /// <summary>
    /// 工作区规矩段：标题 + 截断后的正文。
    /// <b>当前无生产调用点</b>（主代理与子代理统一走 <see cref="WorkspacePointerSection"/>）：
    /// 保留为指针方案失效时的回退实现，截断语义由测试钉住。
    /// </summary>
    /// <param name="workspaceInstructions">工作区说明文件内容</param>
    /// <returns>整段文本</returns>
    internal static string WorkspaceSection(string workspaceInstructions, string fileName = "")
    {
        return $"{AgentPromptHeadings.Workspace}\n\n{TruncateWorkspaceInstructions(workspaceInstructions, fileName)}";
    }

    /// <summary>
    /// 工作区规矩段：只要指针，不要正文（试行）。
    ///
    /// 系统提示每轮完整重发，AGENTS.md 全文放这里等于每轮交一次税；
    /// 模型自己 Read 进来的是历史消息，付一次摊到整个会话（压缩折叠后指针还在，可重读）。
    /// 主代理与子代理<b>统一走这条</b>（子代理原先的"短会话摊不平、弱模型服从率低"顾虑见
    /// <c>SubAgentAssembly</c> 注释——若实测出现"没读就编"的 case，整个装配点切回
    /// <see cref="WorkspaceSection"/> 截断版即可，无第二处要动）。
    /// </summary>
    /// <param name="fileName">实际存在的说明文件名（Loader 解析的结果）；空串时退回含糊的「(或 CLAUDE.md)」</param>
    /// <returns>标题 + 指路指针，不含正文</returns>
    internal static string WorkspacePointerSection(string fileName = "")
    {
        string pointer = string.IsNullOrEmpty(fileName)
            ? "本会话的工作目录下有一份 AGENTS.md（或 CLAUDE.md），写着这个项目的协作规矩与禁区。"
            : $"直接使用 Read 工具传入 {fileName} 参数读取项目的协作规矩与禁区。";
        return $"{AgentPromptHeadings.Workspace}\n\n{pointer}\n"
               + "动手前先读一遍全文。";
    }

    /// <summary>
    /// 工作区说明全文上限。AGENTS.md 是 tenure 最长的固定开销之一（实测本仓 4469 字，
    /// 占系统提示一半以上），而其中构建命令之外的规范/协作口径并不是每轮都要重读。
    /// 超限时只取前半、行边界处截断，剩下的给一个指路指针——模型手里有文件工具，
    /// 提交/规范相关事项前自己去读全文。阈值卡在 2000：本仓恰好留在"无头界面测试"之后
    /// （"别用 dotnet test"那条每轮高价值规则在界内）；换了别的工作区，它也只是"取正文前两屏"，
    /// 剩下的由指针兜底。
    /// </summary>
    internal const int MaxWorkspaceInstructionsChars = 2000;

    /// <summary>
    /// 工作区说明截断（纯函数，可单测）。短文本原样返回；超限按行截断并缀指路指针。
    /// 指路不写绝对路径：工作目录段里已有绝对路径，模型按"工作目录下的 {fileName}"能找到。
    /// </summary>
    /// <param name="workspaceInstructions">工作区说明文件全文</param>
    /// <param name="fileName">实际存在的说明文件名；空串时用含糊的「AGENTS.md（或 CLAUDE.md）」</param>
    /// <returns>原文或"前半 + 指针"</returns>
    internal static string TruncateWorkspaceInstructions(string workspaceInstructions, string fileName = "")
    {
        if (workspaceInstructions.Length <= MaxWorkspaceInstructionsChars) return workspaceInstructions;
        int cut = workspaceInstructions.LastIndexOf('\n', MaxWorkspaceInstructionsChars);
        string head = (cut > 0 ? workspaceInstructions[..cut] : workspaceInstructions[..MaxWorkspaceInstructionsChars])
            .TrimEnd();
        string name = string.IsNullOrEmpty(fileName) ? "AGENTS.md（或 CLAUDE.md）" : fileName;
        return head
               + $"\n\n（工作区说明全文约 {workspaceInstructions.Length} 字，以上只取前 {head.Length} 字。"
               + $"提交代码、改动规范相关事项前，先读工作目录下 {name} 全文。）";
    }

    private static void AppendSection(StringBuilder sb, string? section, EPromptSection kind,
        List<AgentPromptSegment> registry)
    {
        if (string.IsNullOrWhiteSpace(section)) return;
        // 登记的是 TrimEnd 之后那份:清单里的正文必须与真正发出去的逐字相同,
        // 否则「查看全文」看到的和模型读到的不是一个东西
        string text = section.TrimEnd();
        if (sb.Length > 0) sb.Append("\n\n");
        sb.Append(text);
        registry.Add(new AgentPromptSegment(kind, text));
    }

    /// <summary>
    /// 工具纪律段 = 按<b>实际装配的工具集</b>派生的使用纪律(外加工作目录这一事实段)。
    /// 纪律行面向弱模型:短句、祈使、指名工具;关掉的工具绝不出现(纯噪声)。
    ///
    /// <b>刻意不含工作循环</b>(先想再做/边做边说/失败换路/收尾总结)。那段现在是角色提示词的一节
    /// (<see cref="AgentToolPrompts.AgentWorkLoop"/>),理由见 ADR 0004:框架默认那段用户看不见,
    /// 还带一句"You are a helpful AI assistant"抢在角色人格之前。
    ///
    /// 本段由 <see cref="Compose"/> 接在角色段之后，整体挂在一个 <c># 工具</c> 父标题之下。
    /// <b>那个父标题不是装饰</b>：角色段（agent 档默认角色卡）以 <c># 工作循环</c> 起头，
    /// 本段若直接从 <c>## 工作目录</c> 开始，按 markdown 结构读就整个成了
    /// 「工作循环」的子节——层级说的是一件与事实不符的事。
    /// </summary>
    /// <param name="config">智能体的能力配置(角色自带)</param>
    /// <param name="visionToolMounted">识图工具是否已装配</param>
    /// <param name="outputRoomDirectory">草稿目录绝对路径；空串则不写该段</param>
    /// <returns>harness 层指令文本；无任何内容时为空串</returns>
    private static string BuildToolDisciplines(AgentToolConfig config, bool visionToolMounted,
        string workingDirectory, string shellBinary, string pythonInterpreter,
        string outputRoomDirectory, string memoryDirectory, string delegationRoster)
    {
        // 段序与条件都归 ToolDisciplineSections 那一张清单,主代理与子代理共用。
        // 这里只负责把"装配结果"翻译成清单认识的事实
        return ToolDisciplineSections.Build(new ToolDisciplineSections.ToolDisciplineFacts
        {
            // 主代理的文件工具恒含写工具:只读裁剪是子代理探索档专有的
            FileRead = config.EnableFileAccess,
            FileWrite = config.EnableFileAccess,
            Shell = config.EnableShellExecution,
            // 判据取环境是否真的就绪而非某个开关——告诉模型一个不存在的解释器,
            // 它会照着调然后白烧一次调用(同 ADR 0017"判据取装配结果"那条)
            Python = pythonInterpreter.Length > 0,
            WebAccess = config.EnableWebSearch,
            Vision = config.EnableVisionTool && visionToolMounted,
            KnowledgeBase = config.EnableKnowledgeSearchTool,
            Delegation = config.EnableSubAgent,
            DelegationRoster = delegationRoster,
            WorkingDirectory = workingDirectory,
            OutputRoom = outputRoomDirectory,
            Memory = memoryDirectory,
            ShellBinary = shellBinary,
        });
    }
}
