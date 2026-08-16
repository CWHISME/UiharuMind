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
    /// 所以那一层弃用，整段自己拼(见 ADR 0005)。
    /// </summary>
    /// <param name="characterPrompt">角色段(CharacterPromptBuilder 的产物)</param>
    /// <param name="config">智能体的能力配置(角色自带)</param>
    /// <param name="visionToolMounted">识图工具是否已装配</param>
    /// <param name="workingDirectory">工作目录绝对路径;空串则不写该段</param>
    /// <param name="workspaceInstructions">工作区 AGENTS.md 内容;空串则不写该段</param>
    /// <returns>整段系统提示</returns>
    internal static string Compose(string? characterPrompt, AgentToolConfig config,
        bool visionToolMounted, string workingDirectory, string workspaceInstructions)
    {
        StringBuilder sb = new();
        AppendSection(sb, characterPrompt);
        AppendSection(sb, BuildToolDisciplines(config, visionToolMounted, workingDirectory));
        if (workspaceInstructions.Length > 0)
        {
            AppendSection(sb,
                "# Workspace Instructions (from the project's AGENTS.md)\n" + workspaceInstructions);
        }

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return;
        if (sb.Length > 0) sb.Append("\n\n");
        sb.Append(section.TrimEnd());
    }

    /// <summary>
    /// 工具纪律段 = 按<b>实际装配的工具集</b>派生的使用纪律(外加工作目录这一事实段)。
    /// 纪律行面向弱模型:短句、祈使、指名工具;关掉的工具绝不出现(纯噪声)。
    ///
    /// <b>刻意不含工作循环</b>(先想再做/边做边说/失败换路/收尾总结)。那段现在是角色提示词的一节
    /// (<see cref="AgentToolPrompts.AgentWorkLoop"/>),理由见 ADR 0004:框架默认那段用户看不见,
    /// 还带一句"You are a helpful AI assistant"抢在角色人格之前。
    ///
    /// 本段由 <see cref="ComposeAgentInstructions"/> 接在角色段之后。
    /// </summary>
    /// <param name="config">智能体的能力配置(角色自带)</param>
    /// <param name="visionToolMounted">识图工具是否已装配</param>
    /// <returns>harness 层指令文本</returns>
    private static string BuildToolDisciplines(AgentToolConfig config, bool visionToolMounted,
        string workingDirectory)
    {
        StringBuilder sb = new();

        // 工作目录排在最前:后面每一段纪律都以"路径怎么写"为前提
        if (workingDirectory.Length > 0)
        {
            sb.AppendLine("## Working directory");
            sb.AppendLine(AgentToolPrompts.BuildWorkingDirectory(workingDirectory));
        }

        // 各段正文可在设置页覆盖(空 = 用 AgentToolPrompts 默认),段落标题固定由此处统一挂
        if (config.EnableFileAccess)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## File operations");
            sb.AppendLine(AgentToolPrompts.FileAccessDefault);
        }

        if (config.EnableVisionTool && visionToolMounted)
        {
            sb.AppendLine();
            sb.AppendLine("## Images");
            sb.AppendLine(AgentToolPrompts.VisionToolDefault);
        }

        // 文件记忆没有自己的纪律段:框架的 FileMemoryProvider 已经注入了一整段,见 AgentToolPrompts
        if (config.EnableKnowledgeSearchTool)
        {
            sb.AppendLine();
            sb.AppendLine("## Knowledge base");
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
            sb.AppendLine("## Delegation");
            sb.AppendLine(AgentToolPrompts.SubAgentDefault);
        }

        return sb.ToString();
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
