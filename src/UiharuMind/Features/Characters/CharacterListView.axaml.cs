using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色工作台左栏。两条筛选轴的子菜单在<b>每次展开时</b>现填：选项是枚举派生的
/// （<see cref="CharacterFilterPresentation"/>），勾选状态现读视图模型。
/// 填一次就固定住的写法会在改过筛选之后显示过期状态。
/// </summary>
public partial class CharacterListView : UserControl
{
    public CharacterListView()
    {
        InitializeComponent();
    }

    private void OnFilterMenuOpening(object? sender, EventArgs e)
    {
        if (DataContext is not CharacterListViewData data) return;

        FillRadioSubMenu(KindFilterMenuItem, CharacterFilterPresentation.KindOptions(), data.KindFilter,
            value => data.KindFilter = value);
        FillRadioSubMenu(OriginFilterMenuItem, CharacterFilterPresentation.OriginOptions(), data.OriginFilter,
            value => data.OriginFilter = value);
    }

    /// <summary>
    /// 把一条筛选轴铺成一组互斥项。两个轴同构，所以抽一个泛型方法，
    /// 免得加第三条轴时再抄一遍建项与勾选那几行。
    /// </summary>
    /// <typeparam name="T">筛选轴的枚举</typeparam>
    /// <param name="menu">承载子菜单的菜单项</param>
    /// <param name="options">选项与显示名，按枚举值升序</param>
    /// <param name="current">当前选中的那一档</param>
    /// <param name="pick">选中后写回视图模型的回调</param>
    private static void FillRadioSubMenu<T>(MenuItem menu, IReadOnlyList<(T Value, string Label)> options,
        T current, Action<T> pick) where T : struct, Enum
    {
        menu.Items.Clear();
        foreach ((T value, string label) in options)
        {
            MenuItem item = new()
            {
                Header = label,
                ToggleType = MenuItemToggleType.Radio,
                // 组名按轴分开，否则两条轴的互斥组会互相取消勾选
                GroupName = typeof(T).Name,
                IsChecked = EqualityComparer<T>.Default.Equals(value, current),
            };
            item.Click += (_, _) => pick(value);
            menu.Items.Add(item);
        }
    }
}
