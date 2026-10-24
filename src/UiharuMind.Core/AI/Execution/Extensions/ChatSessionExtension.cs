/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// chatsession 扩展：查看与本会话关联的会话状态——本会话派出过的子代理还在不在聊、
/// 卡没卡审批、报告交没交，以及群聊的成员与化身状态。全部只读。
///
/// 群聊的命令位是留给「本会话发起的群聊」的口子：群的创建至今没有记录发起会话，
/// <c>groups</c> 的 mine 参数已就位，等数据有了按发起会话过滤即可接上，不用改扩展
/// </summary>
public sealed class ChatSessionExtension : BuiltinExtension
{
    /// <summary>规格单例</summary>
    public static readonly ChatSessionExtension Spec = new();

    public override string Name => BuiltinExtensions.ChatSession;

    public override string Guide => Summary;

    public override string Summary =>
        "查子代理状态、群聊、远程模型与角色库，也可用于停止子代理";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
    [
        (BuiltinExtensions.ChatSessionList, ChatSessionTools.ListToolName),
        (BuiltinExtensions.ChatSessionGroups, ChatSessionTools.GroupsToolName),
        (BuiltinExtensions.ChatSessionModels, ChatSessionTools.ListModelsToolName),
        (BuiltinExtensions.ChatSessionCharacters, ChatSessionTools.ListCharactersToolName),
        (BuiltinExtensions.ChatSessionStop, ChatSessionTools.StopToolName),
    ];

    /// <summary>查询命令按只读放行；stop 会取消一轮并写收尾历史，按一般级问一次</summary>
    public override EExtensionRisk Classify(FunctionCallContent call)
    {
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;
        return invocation.Command == BuiltinExtensions.ChatSessionStop
            ? EExtensionRisk.Standard
            : EExtensionRisk.ReadOnly;
    }

    /// <summary>只挂能派出子代理、会话是持久的场合：主代理、群成员、化身。子代理/定时/脑没自己的委派可查</summary>
    public override bool MountsIn(EAgentToolScope scope) => scope is
        EAgentToolScope.Main or EAgentToolScope.GroupMember or EAgentToolScope.GroupAvatar;

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        if (string.IsNullOrEmpty(resources.SessionId))
        {
            ChatSessionExtension unavailable = new();
            unavailable.SetUnavailable("当前没有可用的会话，无从查起。");
            return unavailable;
        }

        ChatSessionExtension assembled = new();
        assembled.SetCommands(Bind(ChatSessionTools.Create(resources.SessionId)));
        return assembled;
    }
}