/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Skills;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 三种装配形态各自的 <see cref="HarnessAgentOptions"/>：纯提示词档、agent 档、子代理。
/// 全是纯函数，不碰单例。
/// </summary>
internal static class AgentOptionsFactory
{
    // [MFA绕坑] 绕:框架默认向系统提示注入自身内容 因:无"纯透传"档,只能逐项 Disable 删除条件:框架提供 passthrough 模式
    /// <summary>
    /// 纯提示词档选项(扮演与工具人,纯函数,不碰单例)。不变量:框架侧一律关闭、HarnessInstructions 为空——
    /// 任何一项漏关都会向角色扮演的上下文里注入内容,该不变量由测试钉住。
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="history">历史提供器</param>
    /// <param name="contextProviders">上下文提供器</param>
    /// <param name="chatOptions">对话选项(含角色系统提示,工具应为空)</param>
    /// <returns>框架选项</returns>
    internal static HarnessAgentOptions BuildPromptOnlyOptions(CharacterData character,
        ChatHistoryProvider history, List<AIContextProvider> contextProviders, ChatOptions chatOptions,
        CompactionStrategy? compaction = null)
    {
        return new HarnessAgentOptions
        {
            Name = SanitizeAgentName(character.CharacterName, character.CharacterId),
            Description = character.Description,
            ChatHistoryProvider = history,
            HarnessInstructions = string.Empty,
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            // 「零注入」不变量的唯一例外:压缩只做排除与工具结果折叠,不向上下文添加任何内容,
            // 而扮演档的长对话同样会溢出——它没有工具调用,这里等价于纯截断(ADR 0006)
            DisableCompaction = compaction == null,
            CompactionStrategy = compaction,
            DisableToolAutoApproval = true,
            DisableOpenTelemetry = true,
            AIContextProviders = contextProviders,
            ChatOptions = chatOptions,
        };
    }

    /// <summary>
    /// agent 档选项(纯函数,不碰单例)。<b>整段系统提示由本方法按固定顺序拼</b>：
    /// 角色人格(含工作循环) → 用户卡 → 对话模板 → 工具纪律与工作目录 → 工作区规矩。
    ///
    /// 因此 <c>HarnessInstructions</c> 一律为空串：框架对它只做一件事——拼在角色段<b>之前</b>，
    /// 而人格该排在最前(见 ADR 0005)。框架自带的搜索/文件访问关闭,由自装配工具替代。
    /// </summary>
    /// <param name="plan">装配计划（角色、能力、工作目录、工作区规矩、技能源等已解析完毕）</param>
    /// <param name="history">历史提供器</param>
    /// <param name="contextProviders">上下文提供器</param>
    /// <param name="chatOptions">对话选项(含角色系统提示与已装配工具集,shell 工具已在其中)</param>
    /// <returns>框架选项</returns>
    internal static HarnessAgentOptions BuildAgentOptions(AgentAssemblyPlan plan,
        ChatHistoryProvider history, List<AIContextProvider> contextProviders, ChatOptions chatOptions)
    {
        CharacterData character = plan.Character;
        AgentToolConfig config = plan.Config;
        chatOptions.Instructions = AgentInstructionsComposer.Compose(chatOptions.Instructions, config,
            plan.MountVisionTool, plan.WorkingDirectory, plan.WorkspaceInstructions);

        return new HarnessAgentOptions
        {
            Name = SanitizeAgentName(character.CharacterName, character.CharacterId),
            Description = character.Description,
            ChatHistoryProvider = history,
            // 空串是有意的:整段提示已由 AgentInstructionsComposer 按我们的顺序拼进 ChatOptions
            HarnessInstructions = string.Empty,
            DisableWebSearch = true,
            // 历史预算不再由我们裁剪,改由框架在环压缩按当前模型的上下文动态开窗(ADR 0006)
            DisableCompaction = plan.Compaction == null,
            CompactionStrategy = plan.Compaction,
            DisableOpenTelemetry = true,
            DisableTodoProvider = !config.EnableTodoList,
            DisableAgentModeProvider = !config.EnableAgentMode,
            FileMemoryStore = plan.FileMemoryStore,
            // 1.16:框架文件工具只随 FileAccessStore 出现;shell 改为普通工具挂在 ChatOptions.Tools
            FileAccessStore = null,
            AgentSkillsSource = plan.SkillsSource,
            AIContextProviders = contextProviders,
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules = ApprovalModeMapper.BuildRules(plan.Profile.PermissionMode,
                    plan.Profile.PreAuthorizedShellPatterns, plan.Profile.SessionShellApprovalSource),
            },
            ChatOptions = chatOptions,
        };
    }


    internal static string SanitizeAgentName(string displayName, string fallback)
    {
        string name = new(displayName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(name) ? fallback : name;
    }
}
