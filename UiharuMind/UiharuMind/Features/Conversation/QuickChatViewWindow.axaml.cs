using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 临时对话
/// </summary>
public partial class QuickChatViewWindow : QuickWindowBase
{
    /// <summary>
    /// 打开一个承载给定会话的临时对话窗口
    /// </summary>
    /// <param name="chatSession">会话本体(转临时对话前已持久化)</param>
    public static void Show(ChatSession chatSession)
    {
        UIManager.ShowWindow<QuickChatViewWindow>(x => x.SetSession(chatSession), isMulti: true);
    }

    public QuickChatViewWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 装载会话:标题与模型由通用对话组件的头部展示
    /// </summary>
    /// <param name="chatSession">会话本体</param>
    public void SetSession(ChatSession chatSession)
    {
        ConversationViewModel conversation = new();
        DataContext = conversation;
        _ = conversation.LoadSessionAsync(chatSession.ToMeta());
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
        PointerUpdateKind pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed && e.ClickCount >= 2)
        {
            if (Math.Abs(Height - StartHeight) > 10)
            {
                Width = StartWidth;
                Height = StartHeight;
            }
            else WindowState = WindowState.Maximized;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }
}