using System;
using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>群的右栏成员列表。数据上下文是 <see cref="GroupMembersViewData"/></summary>
public partial class GroupMembersPanel : UserControl
{
    public GroupMembersPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 主持人下拉只在这里提交：用户亲手关下拉，才算他选定了。
    /// 下拉的选中是单向显示（<see cref="GroupMembersViewData.SelectedHost"/>），
    /// 切会话/模板重建时控件自己会把选中项清掉又推回来——那一律不是用户意图，
    /// 以前经双向绑定直接灌进视图模型，切一切换就无故弹窗“换成无吗”。
    /// </summary>
    internal void OnHostDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (DataContext is not GroupMembersViewData members) return;
        if (combo.SelectedItem is not GroupHostChoice choice) return;
        members.SelectedHost = choice;
    }
}
