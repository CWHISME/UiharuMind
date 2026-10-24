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
using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 桌面扩展：窗口编号全应用一份，快照按会话记
/// </summary>
public sealed class DesktopExtension : BuiltinExtension
{
    /// <summary>规格单例</summary>
    public static readonly DesktopExtension Spec = new();

    public override string Name => BuiltinExtensions.Desktop;

    public override string Guide =>
        "用户电脑上别的应用的窗口。先 windows 列出窗口（编号如 w3），再 snapshot 读它的界面（ref 如 e5），" +
        "然后按 ref 对它 act。windows 与 snapshot 从不问用户；act 与 launch 除非用户已为本会话放行这个应用，否则会询问；" +
        "input \"real\"（真实鼠标键盘）要用户为这个应用单独放行，handoff 总是等用户。密码管理、系统设置、终端与本应用自身的窗口不能碰。";

    public override string Summary => "看、操作电脑上别的应用的窗口：读界面、点按钮、填字、按快捷键、启动应用";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
    [
        (BuiltinExtensions.DesktopWindows, DesktopTools.WindowsToolName),
        (BuiltinExtensions.DesktopSnapshot, DesktopTools.SnapshotToolName),
        (BuiltinExtensions.DesktopAct, DesktopTools.ActToolName),
        (BuiltinExtensions.DesktopLaunch, DesktopTools.LaunchToolName),
        (BuiltinExtensions.DesktopHandoff, DesktopTools.HandoffToolName),
    ];

    /// <summary>看窗口只读；真实键鼠按应用逐项；接管、真实键鼠之外的硬问按应用判</summary>
    public override EExtensionRisk Classify(FunctionCallContent call)
    {
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;
        if (CanonicalCommand(invocation.Command) == null) return EExtensionRisk.ReadOnly;
        FunctionCallContent inner = ExtensionCalls.Unwrap(call);
        if (DesktopRisk.IsReadOnly(inner)) return EExtensionRisk.ReadOnly;
        if (DesktopRisk.IsRealInput(inner)) return EExtensionRisk.PerScope;
        return DesktopRisk.RequiresApprovalInAllModes(inner) ? EExtensionRisk.AlwaysAsk : EExtensionRisk.Standard;
    }

    /// <summary>「本会话允许」按应用记（<c>desktop:com.apple.TextEdit</c>），放行记事本不连带放行微信；真实键鼠另记一格</summary>
    public override string ApprovalKey(ExtensionInvocation invocation, FunctionCallContent call,
        DesktopHost? desktop = null) =>
        $"{BuiltinExtensions.Desktop}:{DesktopRisk.ApprovalScope(ExtensionCalls.Unwrap(call), desktop ?? DesktopHost.Shared)}";

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        if (resources.Desktop == null)
        {
            DesktopExtension unavailable = new();
            unavailable.SetUnavailable("Desktop control is not available here.");
            return unavailable;
        }

        string spillDirectory = Path.Combine(
            resources.OutputRoomDirectory.Length > 0 ? resources.OutputRoomDirectory : Path.GetTempPath(), "desktop");
        EReadImageMode screenshots = imageMode == EReadImageMode.Attach && !mountFiles ? EReadImageMode.None : imageMode;
        DesktopExtension assembled = new();
        assembled.SetCommands(Bind(DesktopTools.Create(resources.Desktop, resources.SessionId, spillDirectory,
            resources.Paths, screenshots)));
        return assembled;
    }
}
