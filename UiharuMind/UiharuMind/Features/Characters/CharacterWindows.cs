/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色相关窗口的打开入口。
///
/// 住在本 feature 而不是 <see cref="UIManager"/>：那里是通用的窗口栈与生命周期机制，
/// 不该知道 <see cref="CharacterEditWindow"/> 这种具体窗口。
/// 原先它一并管着 5 个 feature 专属的打开器，等于把每个 feature 的窗口清单都抄进了 Shared。
/// </summary>
public static class CharacterWindows
{
    /// <summary>
    /// 打开角色编辑窗。<b>只给对话侧那两个入口用</b>——角色工作台里的编辑是内联的，
    /// 不走这里（见 ADR 0014）。
    /// </summary>
    /// <param name="draft">待编辑的草稿；保存与否都由窗口自己了结，调用方不必再管</param>
    public static void ShowEditCharacterWindow(CharacterDraft draft)
    {
        UIManager.ShowWindow<CharacterEditWindow>(x => x.DataContext = draft);
    }
}
