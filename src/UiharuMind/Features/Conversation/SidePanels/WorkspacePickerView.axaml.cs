using System;
using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>工作目录的展示与切换（单聊工作区卡与群卡共用），DataContext 是 <see cref="WorkspacePickerViewData"/></summary>
public partial class WorkspacePickerView : UserControl
{
    public WorkspacePickerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 目录行展开前重建最近工作区列表：这份列表也会被设置页(默认工作目录)写入，
    /// 而那条路径不经过会话，光靠 WorkspacePath 变化刷新会漏掉。
    /// </summary>
    private void OnWorkspaceFlyoutOpening(object? sender, EventArgs e)
    {
        (DataContext as WorkspacePickerViewData)?.RefreshRecent();
    }
}
