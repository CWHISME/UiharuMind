/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
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
    /// 打开角色编辑窗
    /// </summary>
    /// <param name="characterInfo">待编辑的角色；传 null 即新建</param>
    /// <param name="onSureCallback">点确定后的回调</param>
    public static void ShowEditCharacterWindow(CharacterInfoViewData? characterInfo,
        Action<CharacterInfoViewData>? onSureCallback)
    {
        // 传 null 即新建:档位由调用方在数据上定好(见 CharacterListViewData.AddCharacter)
        characterInfo ??= new CharacterInfoViewData();
        UIManager.ShowWindow<CharacterEditWindow>(x =>
        {
            x.DataContext = characterInfo;
            x.OnSureCallback = onSureCallback;
        });
    }
}
