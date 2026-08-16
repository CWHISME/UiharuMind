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
    /// <summary>
    /// 三种形态共有的那五项口径，一处定义。
    ///
    /// <c>HarnessInstructions</c> 为空串：整段系统提示由我们自己按顺序拼，
    /// 框架对这一层只做一件事——拼在角色段<b>之前</b>，而人格必须排在最前（ADR 0005）。
    /// 框架自带搜索关闭，由自装配工具替代。压缩按需给（ADR 0006）。遥测不要。
    /// </summary>
    /// <param name="compaction">历史压缩策略；为 null 则关闭压缩</param>
    /// <returns>填好共有项的选项，调用方接着补自己那部分</returns>
    private static HarnessAgentOptions CreateBaseOptions(CompactionStrategy? compaction)
    {
        return new HarnessAgentOptions
        {
            HarnessInstructions = string.Empty,
            DisableWebSearch = true,
            DisableCompaction = compaction == null,
            CompactionStrategy = compaction,
            DisableOpenTelemetry = true,
        };
    }

    /// <summary>
    /// 关掉框架的全部有状态能力。扮演档与子代理共用：前者要的是零注入，
    /// 后者是一次性的纯工具循环，两者都不该有 todo / mode / 技能 / 文件记忆。
    /// agent 档不走这里——它那几项按角色的能力配置逐项决定。
    /// </summary>
    /// <param name="options">待关闭的选项</param>
    /// <returns>同一个选项对象，便于串写</returns>
    private static HarnessAgentOptions DisableStatefulProviders(HarnessAgentOptions options)
    {
        options.DisableFileMemory = true;
        options.DisableTodoProvider = true;
        options.DisableAgentModeProvider = true;
        options.DisableAgentSkillsProvider = true;
        return options;
    }

    /// <summary>
    /// 子代理的选项底子：与扮演档同样是「基本口径 + 框架有状态能力全关」。
    /// 两者形态迥异却共享这个底子，因为它们是同一种东西——不带框架附加状态的一次性调用。
    /// </summary>
    /// <param name="compaction">历史压缩策略</param>
    /// <returns>填好共有项的选项</returns>
    internal static HarnessAgentOptions CreateSubAgentBaseOptions(CompactionStrategy? compaction)
    {
        return DisableStatefulProviders(CreateBaseOptions(compaction));
    }

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
        // 「零注入」不变量的唯一例外是压缩:它只做排除与工具结果折叠,不向上下文添加任何内容,
        // 而扮演档的长对话同样会溢出——它没有工具调用,这里等价于纯截断(ADR 0006)
        HarnessAgentOptions options = DisableStatefulProviders(CreateBaseOptions(compaction));
        options.Name = SanitizeAgentName(character.CharacterName, character.CharacterId);
        options.Description = character.Description;
        options.ChatHistoryProvider = history;
        options.DisableToolAutoApproval = true; //扮演档没有工具,连审批中间件都不必挂
        options.AIContextProviders = contextProviders;
        options.ChatOptions = chatOptions;
        return options;
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

        // 历史预算不再由我们裁剪,改由框架在环压缩按当前模型的上下文动态开窗(ADR 0006)
        HarnessAgentOptions options = CreateBaseOptions(plan.Compaction);
        options.Name = SanitizeAgentName(character.CharacterName, character.CharacterId);
        options.Description = character.Description;
        options.ChatHistoryProvider = history;
        // 与另两种形态不同:这几项按角色的能力配置逐项决定,故不走 DisableStatefulProviders
        options.DisableTodoProvider = !config.EnableTodoList;
        options.DisableAgentModeProvider = !config.EnableAgentMode;
        options.FileMemoryStore = plan.FileMemoryStore;
        // 1.16:框架文件工具只随 FileAccessStore 出现;shell 改为普通工具挂在 ChatOptions.Tools
        options.FileAccessStore = null;
        options.AgentSkillsSource = plan.SkillsSource;
        options.AIContextProviders = contextProviders;
        options.ToolApprovalAgentOptions = new ToolApprovalAgentOptions
        {
            AutoApprovalRules = ApprovalModeMapper.BuildRules(plan.Profile.PermissionMode,
                plan.Profile.PreAuthorizedShellPatterns, plan.Profile.SessionShellApprovalSource),
        };
        options.ChatOptions = chatOptions;
        return options;
    }


    internal static string SanitizeAgentName(string displayName, string fallback)
    {
        string name = new(displayName.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(name) ? fallback : name;
    }
}
