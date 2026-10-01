/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using System.Linq;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 会话列表。DataContext 即 <see cref="SessionListModel"/>，聊天页与智能体页共用。
/// </summary>
public partial class SessionListView : UserControl
{
    /// <summary>
    /// 是否显示角色头像。
    ///
    /// 这是两页<b>有意</b>保留的唯一差异，而它同时决定「这一页怎么说明这是哪个角色」——
    /// 二选一：聊天页显示头像（角色扮演靠脸认角色，且标题本就是角色名），
    /// 智能体页不显示头像、改在副行给出角色名（标题是用户第一句，看不出用的是哪个智能体）。
    ///
    /// ⚠️ 角色名的显隐<b>不要</b>改成从标题推（「标题里没有角色名就显示」）。
    /// 试过一版，会话一复制（<c>SessionManager.Copy</c> 给标题加 <c>_Copy</c> 后缀）
    /// 判据就失配，聊天页的副行会凭空多出角色名。同理描述那边也踩过一次。
    /// 这是页面级选择，就让它是个页面级参数。
    /// </summary>
    public static readonly StyledProperty<bool> ShowAvatarProperty =
        AvaloniaProperty.Register<SessionListView, bool>(nameof(ShowAvatar), true);

    /// <summary>是否显示角色头像</summary>
    public bool ShowAvatar
    {
        get => GetValue(ShowAvatarProperty);
        set => SetValue(ShowAvatarProperty, value);
    }

    public SessionListView()
    {
        InitializeComponent();
    }

    /// 右键哪一行就在哪一行弹那份共用菜单,数据上下文换成那一行的条目
    private void OnListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control source) return;

        Control? row = source.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(x => x.DataContext is SessionListItem);
        if (row == null || Resources["SessionRowMenu"] is not ContextMenu menu) return;

        menu.DataContext = row.DataContext;
        menu.Open(row);
        e.Handled = true;
    }
}
