/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Input;
using Avalonia.Interactivity;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.UIHolder;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 子代理过程窗口：只读地展示一次 <c>run_subagent</c> 调用内部的全部活动。
///
/// 只读是按构造成立的——窗口里根本没有输入框，也没有任何回写路径，
/// 不需要给 ConversationViewModel 加「只读模式」。
///
/// 绑的是工具卡片上那个 <see cref="ToolCallItem.NestedItems"/> 本体（同一个集合实例），
/// 所以子代理还在跑时打开窗口能实时看到它推进；关掉再打开也不丢，因为集合活在卡片上。
/// 反过来说它只存在于内存：切会话或重启后卡片没了，过程也就没了（报告仍在工具结果里）。
/// </summary>
public partial class SubAgentActivityWindow : QuickWindowBase
{
    private readonly ScrollViewerAutoScrollHolder _autoScrollHolder;

    /// <summary>
    /// 打开一次工具调用的过程窗口
    /// </summary>
    /// <param name="item">发起子代理的那张工具卡片</param>
    public static void Show(ToolCallItem item)
    {
        UIManager.ShowWindow<SubAgentActivityWindow>(x => x.SetSource(item), isMulti: true);
    }

    public SubAgentActivityWindow()
    {
        InitializeComponent();
        // 与会话流同一个跟底器:用户往上滚就停止自动跟底,滚回底部再恢复。
        // 子代理跑起来一秒好几条,少了这个"停止"语义,想回看前面几步根本按不住
        _autoScrollHolder = new ScrollViewerAutoScrollHolder(ActivityScroll);
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
    }

    /// <summary>
    /// 装载来源卡片。窗口可被复用（<see cref="UiharuWindowBase.IsCacheWindow"/>），
    /// 换源等于内容整体重建，跟底器会把 Offset 回零误读为用户上滚，故显式恢复。
    /// </summary>
    /// <param name="item">发起子代理的那张工具卡片</param>
    public void SetSource(ToolCallItem item)
    {
        TaskTextBlock.Text = item.ArgumentSummary;
        ActivityList.ItemsSource = item.NestedItems;
        _autoScrollHolder.Resume();
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }
}
