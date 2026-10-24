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
/// 目标模式隐藏扩展：模型自判结束目标模式。广告与帮助不列（<see cref="IsHidden"/>），
/// 只在目标模式开启时经注入消息得知用法；只挂主代理场合
/// </summary>
public sealed class GoalModeExtension : BuiltinExtension
{
    /// <summary>规格单例</summary>
    public static readonly GoalModeExtension Spec = new();

    public override string Name => BuiltinExtensions.Goal;

    public override string Guide => string.Empty;

    public override string Summary => string.Empty;

    public override bool IsHidden => true;

    public override bool MountsIn(EAgentToolScope scope) => scope == EAgentToolScope.Main;

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
        [(BuiltinExtensions.GoalEnd, EndGoalModeTool.ToolName)];

    /// <summary>结束目标模式：不改任何盘，只请求收场——无人值守也得放行，否则模型结束不了目标模式</summary>
    public override EExtensionRisk Classify(FunctionCallContent call) => EExtensionRisk.ReadOnly;

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        GoalModeExtension assembled = new();
        assembled.SetCommands(Bind([EndGoalModeTool.Create()]));
        return assembled;
    }
}
