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
using Microsoft.Agents.AI.Compaction;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Harness;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.WebTools;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// Agent 子系统宿主:基于 Microsoft.Agents.AI Harness 组装 agent,
/// 聚合技能目录、MCP 工具、识图子能力与定时调度(框架缺失的唯一自建件)。
/// </summary>
public class CharacterRunnerFactory : Singleton<CharacterRunnerFactory>, IInitialize
{
    /// <summary>
    /// shell 工具名(供预授权规则匹配)。装配时显式传给 <c>AsAIFunction</c>,
    /// 让这个常量成为唯一权威——否则它只是框架默认值的一份副本,框架改默认值就会静默失配。
    /// </summary>
    public const string ShellToolName = "Shell";

    /// <summary>定时任务调度后端(框架无对应能力,自建保留)</summary>
    public ISchedulerBackend Scheduler { get; private set; } = null!;

    public void OnInitialize()
    {
        Scheduler = new InProcessSchedulerBackend();
    }

    /// <summary>
    /// 创建一个对话执行者。框架类型止步于实现内部,调用方只见稳定类型。
    /// </summary>
    /// <returns>执行者;使用前需先调用 <see cref="ICharacterRunner.AttachAsync"/></returns>
    public ICharacterRunner CreateRunner()
    {
        return new HarnessCharacterRunner();
    }

    /// <summary>
    /// 按配置构建一个 agent。两步：先把外部世界读完（<see cref="AgentAssemblyPlan.Resolve"/>），
    /// 再纯内存组装（<see cref="AgentAssembler.Assemble"/>）。
    ///
    /// 这两步曾经是同一个 110 行的方法，单例读取与组装交织，整条路径无法单测。
    /// </summary>
    /// <param name="profile">构建配置</param>
    /// <returns>agent 句柄</returns>
    public AgentHandle CreateAgent(AgentBuildProfile profile)
    {
        return AgentAssembler.Assemble(AgentAssemblyPlan.Resolve(profile));
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
    internal static List<CharacterData> ResolveMountedAgents(CharacterData owner)
    {
        return owner.MountAgents
            .Select(id => CharacterManager.Instance.GetCharacterData(id))
            .Where(x => x.Kind.IsAgent() && x.CharacterId != owner.CharacterId)
            .ToList();
    }

}
