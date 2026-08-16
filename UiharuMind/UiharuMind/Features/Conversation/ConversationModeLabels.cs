/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 输入区工具行那四组开关的文案：状态键（驱动图标配色的 Tag）与悬停提示。
///
/// 做成纯函数是有意的：它只把「当前是哪一档」翻译成给人看的字，不持有任何状态，
/// 因此四组开关的本地化键<b>集中在这一处</b>——从前它们散在视图模型的十个属性体里，
/// 加一档或改一句措辞要在四处之间来回找。
/// </summary>
public static class ConversationModeLabels
{
    /// <summary>模式显示标签</summary>
    /// <param name="mode">当前模式</param>
    /// <returns>本地化文案</returns>
    public static string ModeLabel(EAgentMode mode) => LocalizationManager.Instance.GetString(
        mode == EAgentMode.Plan ? "AgentPlanMode" : "AgentModeExecute");

    /// <summary>模式悬停提示</summary>
    /// <param name="mode">当前模式</param>
    /// <returns>本地化文案</returns>
    public static string ModeTooltip(EAgentMode mode) =>
        $"{ModeLabel(mode)}\n{LocalizationManager.Instance.GetString("ClickToSwitch")}";

    /// <summary>
    /// 权限档状态键(ReadOnly/AutoEdit/FullAuto)。
    /// 越界一律落到 AutoEdit——与视图模型构造期的 Clamp 同一兜底方向
    /// </summary>
    /// <param name="index">权限档序号</param>
    /// <returns>状态键</returns>
    public static string PermissionKey(int index) => index switch
    {
        0 => "ReadOnly",
        2 => "FullAuto",
        _ => "AutoEdit",
    };

    /// <summary>权限档悬停提示</summary>
    /// <param name="index">权限档序号</param>
    /// <returns>本地化文案</returns>
    public static string PermissionTooltip(int index) =>
        LocalizationManager.Instance.GetString(index switch
        {
            0 => "AgentPermissionReadOnly",
            2 => "AgentPermissionFullAuto",
            _ => "AgentPermissionAutoEdit",
        });

    /// <summary>思考力度状态键(EThinkingMode 名)</summary>
    /// <param name="index">思考力度序号,即 EThinkingMode</param>
    /// <returns>状态键</returns>
    public static string ThinkingKey(int index) => ((EThinkingMode)index).ToString();

    /// <summary>思考力度悬停提示</summary>
    /// <param name="index">思考力度序号,即 EThinkingMode</param>
    /// <returns>本地化文案</returns>
    public static string ThinkingTooltip(int index) =>
        LocalizationManager.Instance.GetString($"ThinkingMode{(EThinkingMode)index}") +
        $"\n{LocalizationManager.Instance.GetString("ThinkingModeTips")}";

    /// <summary>发送身份对应的图标名</summary>
    /// <param name="isUser">是否以用户身份发送</param>
    /// <returns>图标名</returns>
    public static string SenderIcon(bool isUser) => isUser ? "user" : "bot";

    /// <summary>发送身份悬停提示</summary>
    /// <param name="isUser">是否以用户身份发送</param>
    /// <returns>本地化文案</returns>
    public static string SenderTooltip(bool isUser) =>
        $"{(isUser ? "User" : "Assistant")}\n{LocalizationManager.Instance.GetString("SendUserDesc")}";
}
