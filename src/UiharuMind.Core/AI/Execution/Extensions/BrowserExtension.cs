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
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 浏览器扩展：一个会话一个标签页
/// </summary>
public sealed class BrowserExtension : BuiltinExtension
{
    /// <summary>规格单例（无资源依赖，审批/名单/界面都经它回答）</summary>
    public static readonly BrowserExtension Spec = new();

    public override string Name => BuiltinExtensions.Browser;

    public override string Guide =>
        "带独立配置文件的真实 Chrome 窗口，一个会话一个标签页；用户在那里登录过的站点保持登录。" +
        "先 navigate（或 snapshot 重读已打开的页面），再按最新 outline 里的 ref 对元素 act。" +
        "snapshot 从不问用户；navigate 和 act 除非用户已为本会话放行浏览器，否则会询问；" +
        "handoff 和上传总是等用户。";

    public override string Summary => "操作一个真实的 Chrome 窗口：打开网页、点按钮、填表、读正文、截图";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
    [
        (BuiltinExtensions.BrowserSnapshot, BrowserTools.SnapshotToolName),
        (BuiltinExtensions.BrowserNavigate, BrowserTools.NavigateToolName),
        (BuiltinExtensions.BrowserAct, BrowserTools.ActToolName),
        (BuiltinExtensions.BrowserHandoff, BrowserTools.HandoffToolName),
    ];

    /// <summary>快照只读；去本机/内网地址硬问；其余一般级（本会话放行过浏览器就不问）</summary>
    public override EExtensionRisk Classify(FunctionCallContent call)
    {
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;
        // 没有这条命令：执行只会报错，不为它弹卡（与基类同一口径）
        if (CanonicalCommand(invocation.Command) == null) return EExtensionRisk.ReadOnly;
        if (invocation.Command == BuiltinExtensions.BrowserSnapshot) return EExtensionRisk.ReadOnly;
        return BrowserRisk.RequiresApprovalInAllModes(ExtensionCalls.Unwrap(call))
            ? EExtensionRisk.AlwaysAsk
            : EExtensionRisk.Standard;
    }

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        if (resources.Browser == null)
        {
            // 名单照写（与系统提示同一判据），调用时如实说用不了，免得名单与帮助各说各的
            BrowserExtension unavailable = new();
            unavailable.SetUnavailable("No browser is available here.");
            return unavailable;
        }

        string spillDirectory = Path.Combine(
            resources.OutputRoomDirectory.Length > 0 ? resources.OutputRoomDirectory : Path.GetTempPath(), "browser");
        EReadImageMode screenshots = imageMode == EReadImageMode.Attach && !mountFiles ? EReadImageMode.None : imageMode;
        BrowserExtension assembled = new();
        assembled.SetCommands(Bind(BrowserTools.Create(resources.Browser, resources.SessionId, spillDirectory,
            screenshots, resources.Paths)));
        return assembled;
    }
}
