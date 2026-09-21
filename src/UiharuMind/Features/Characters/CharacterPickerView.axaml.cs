/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色选择器（单选、内联）。DataContext 由使用方给一个 <see cref="CharacterPickerViewData"/>。
/// </summary>
public partial class CharacterPickerView : UserControl
{
    public CharacterPickerView()
    {
        InitializeComponent();
    }
}
