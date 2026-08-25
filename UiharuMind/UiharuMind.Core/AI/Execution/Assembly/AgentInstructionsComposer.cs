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

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// agent 档整段系统提示的编排。子代理那份体例不同，在 <see cref="SubAgentAssembly"/> 里。
/// </summary>
internal static class AgentInstructionsComposer
{
    // 标题一律取自 AgentPromptHeadings：工作区规矩段与 MCP 自述段主 agent 与子代理逐字共用
    // ——子代理干的正是探查工作区的活、拿的是同一份 MCP 工具，不该是全场唯一不知道规矩的人。

    /// <summary>
    /// 按固定顺序拼出 agent 档的整段系统提示：
    /// 角色段(人格 + 用户卡 + 对话模板) → 工具纪律与工作目录 → MCP server 自述 → 工作区规矩。
    ///
    /// <b>人格在最前</b>：小模型要先知道自己是谁，再读一大段英文工具纪律。
    /// 这个顺序拿不到手过：框架只会把 <c>HarnessInstructions</c> 拼在角色段之前，
    /// 所以那一层弃用，整段自己拼(见 ADR 0005)。
    ///
    /// MCP 自述紧跟工具纪律：它讲的正是"这批工具怎么用"，与上一段是同一件事的延续；
    /// 而工作区规矩讲的是"这个项目怎么干活"，属于另一个层次，排在最后。
    /// </summary>
    /// <param name="characterPrompt">角色段(CharacterPromptBuilder 的产物)</param>
    /// <param name="config">智能体的能力配置(角色自带)</param>
    /// <param name="visionToolMounted">识图工具是否已装配</param>
    /// <param name="workingDirectory">工作目录绝对路径;空串则不写该段</param>
    /// <param name="workspaceInstructions">工作区 AGENTS.md 内容;空串则不写该段</param>
    /// <param name="mcpInstructions">MCP server 自述(已按 server 分节);空串则不写该段</param>
    /// <param name="shellBinary">实际解析出来的 shell 可执行路径;空串则不写那一句</param>
    /// <param name="pythonInterpreter">受管 Python 环境的解释器路径。<b>只作闸门</b>——
    /// 空串则整段不写；非空时正文里也不印它，环境已由 PATH 前置激活</param>
    /// <param name="pythonOutputDirectory">产出目录绝对路径(给用户看的图表落在这儿)</param>
    /// <param name="segments">
    /// 各段的分段清单，<b>拼接现场登记</b>。能力面板要按段报占用，而事后对整串按标题反切，
    /// 本方法一改标题那边就静默错。空段不入册（它本来也没发出去）
    /// </param>
    /// <returns>整段系统提示</returns>
    internal static string Compose(string? characterPrompt, AgentToolConfig config,
        bool visionToolMounted, string workingDirectory, string workspaceInstructions,
        string mcpInstructions, string shellBinary, string pythonInterpreter,
        string pythonOutputDirectory, out IReadOnlyList<AgentPromptSegment> segments)
    {
        List<AgentPromptSegment> registry = new();
        StringBuilder sb = new();
        AppendSection(sb, characterPrompt, EPromptSection.Character, registry);
        AppendSection(sb, BuildToolDisciplines(config, visionToolMounted, workingDirectory, shellBinary,
            pythonInterpreter, pythonOutputDirectory), EPromptSection.ToolDisciplines, registry);
        if (mcpInstructions.Length > 0)
        {
            AppendSection(sb, McpSection(mcpInstructions), EPromptSection.Mcp, registry);
        }

        if (workspaceInstructions.Length > 0)
        {
            AppendSection(sb, WorkspaceSection(workspaceInstructions), EPromptSection.Workspace, registry);
        }

        segments = registry;
        return sb.ToString();
    }

    /// <summary>
    /// MCP server 自述段（主 agent 与子代理逐字共用）
    /// </summary>
    /// <param name="mcpInstructions">已按 server 分节的自述正文</param>
    /// <returns>整段文本</returns>
    internal static string McpSection(string mcpInstructions)
    {
        return $"{AgentPromptHeadings.Mcp}\n\n{mcpInstructions}";
    }

    /// <summary>
    /// 工作目录段。<b>两种装配形态共用这一段正文</b>，只有标题级别不同：
    /// 主 agent 里它是 <c># 工具</c> 的一个分项（工作目录正是给那些工具用的根），
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
    /// 工作区规矩段（主 agent 与子代理逐字共用）
    /// </summary>
    /// <param name="workspaceInstructions">工作区说明文件内容</param>
    /// <returns>整段文本</returns>
    internal static string WorkspaceSection(string workspaceInstructions)
    {
        return $"{AgentPromptHeadings.Workspace}\n{workspaceInstructions}";
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
    /// <returns>harness 层指令文本；无任何内容时为空串</returns>
    private static string BuildToolDisciplines(AgentToolConfig config, bool visionToolMounted,
        string workingDirectory, string shellBinary, string pythonInterpreter,
        string pythonOutputDirectory)
    {
        StringBuilder sb = new();

        // 工作目录排在最前:后面每一段纪律都以"路径怎么写"为前提
        if (workingDirectory.Length > 0)
        {
            sb.AppendLine(WorkingDirectorySection(workingDirectory, "##"));
        }

        // 各段正文可在设置页覆盖(空 = 用 AgentToolPrompts 默认),段落标题固定由此处统一挂
        if (config.EnableFileAccess)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(AgentPromptHeadings.FileOperations);
            sb.AppendLine(AgentToolPrompts.FileAccessDefault);
        }

        // shell 有自己的一节:它曾是唯一挂了工具却零指示的能力,而缺口的表现是模型
        // 拿 Write 重写全文去做一次 mv(见 AgentToolPrompts.ShellDefault)
        if (config.EnableShellExecution)
        {
            sb.AppendLine();
            sb.AppendLine(AgentPromptHeadings.Shell);
            sb.AppendLine(AgentToolPrompts.BuildShell(config.EnableFileAccess, shellBinary));

            // Python 是 shell 的一个分项,不是独立能力(ADR 0019),故嵌在这个 if 里。
            // 判据取环境是否真的就绪而非某个开关——告诉模型一个不存在的解释器,
            // 它会照着调然后白烧一次调用(同 ADR 0017"判据取装配结果"那条)
            if (pythonInterpreter.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(AgentPromptHeadings.Python);
                sb.AppendLine(AgentToolPrompts.BuildPython(pythonOutputDirectory,
                    config.EnableFileAccess));
            }
        }

        if (config.EnableVisionTool && visionToolMounted)
        {
            sb.AppendLine();
            sb.AppendLine(AgentPromptHeadings.Images);
            sb.AppendLine(AgentToolPrompts.VisionToolDefault);
        }

        // 文件记忆没有自己的纪律段:框架的 FileMemoryProvider 已经注入了一整段,见 AgentToolPrompts
        if (config.EnableKnowledgeSearchTool)
        {
            sb.AppendLine();
            sb.AppendLine(AgentPromptHeadings.KnowledgeBase);
            sb.AppendLine(AgentToolPrompts.KnowledgeSearchDefault);
        }

        // 辨析句只在两者都挂载时才有意义,故不属于任何一段的正文(那两段各自可被用户覆盖)
        if (config.EnableFileMemory && config.EnableKnowledgeSearchTool)
        {
            sb.AppendLine();
            sb.AppendLine(AgentToolPrompts.MemoryDisambiguation);
        }

        if (config.EnableSubAgent)
        {
            sb.AppendLine();
            sb.AppendLine(AgentPromptHeadings.Delegation);
            sb.AppendLine(AgentToolPrompts.SubAgentDefault);
        }

        //一项都没有就整段不出现:光挂一个空的父标题是纯噪声
        if (sb.Length == 0) return string.Empty;

        // 护栏句紧跟父标题:本段整段中文,而它每轮都发、体量压过用户那几句话,
        // 不钉一句"别照着这段的语言回复",小模型的输出语言就会被拽向中文。
        // 挂在这里而不是工作循环段,是因为那段会落进用户存档、用户删得掉(见 AgentToolPrompts)
        return $"{AgentPromptHeadings.Tools}\n\n{AgentToolPrompts.LanguageNeutrality}\n\n" + sb;
    }

    /// <summary>
    /// 解析一个角色挂载的子智能体名单。按档位过滤而非信任存档（旧存档里可能躺着工具人），
    /// 并排除自己（递归）。
    ///
    /// <b>装配与快照必须用同一份</b>：名单连同各自的名字与描述会在装配时固化进子代理工具，
    /// 而「要不要重建装配」由 <see cref="AgentAssemblyFacts"/> 判定——
    /// 两处各写一份过滤规则的话，改名单不重建的那类缺陷会静默复发。
    /// </summary>
    /// <param name="owner">挂载方角色</param>
    /// <returns>可用作子智能体的角色</returns>
}
