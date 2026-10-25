/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色选择器（单选、内联）。DataContext 由使用方给一个 <see cref="CharacterPickerViewData"/>。
/// </summary>
public partial class CharacterPickerView : UserControl
{
    public CharacterPickerView()
    {
        InitializeComponent();

        // 行模板必须可复用：XAML 的 DataTemplate 虽然声明了 IRecyclingDataTemplate，
        // 但滚动时仍是每次新建一棵行控件树（实测 20 步滚动新建 50 次、复用 0 次）。
        // 换成 FuncDataTemplate + CharacterPickerRow 之后同一行实例换 DataContext 复用。
        CharacterList.ItemTemplate = new FuncDataTemplate<CharacterPickerItem>(
            static (_, _) => new CharacterPickerRow(), supportsRecycling: true);
    }
}
