/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 一条文本消息的气泡。DataContext 即 <see cref="TextConversationItem"/>，由宿主的 DataTemplate 传入。
///
/// 抽成组件的理由与 <see cref="ToolCallCardView"/> 一致，只是晚了一步：另外三种条目
/// （工具调用 / 审批 / 思考）早就各有其组件，唯独文本气泡一直内联在 ConversationView.axaml 里，
/// 130 行模板 + 三条只有它用的样式，把那个文件顶到了 700 多行。
/// </summary>
public partial class TextMessageCardView : UserControl
{
    public TextMessageCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击气泡里的图 → 原尺寸浮窗预览（可滚轮缩放、拖动）。
    /// 显示的正是**发给模型的那一份**（缩放重编码之后的结果），
    /// 「压缩之后文字还认得出吗」只能在这里对答案。
    /// </summary>
    private void OnMessageImagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Image { Source: Bitmap bitmap }) return;

        // 传副本:气泡这张图还挂在条目上,预览窗关闭时会释放它自己那份
        UIManager.ShowPreviewImageCopyWindowAtMousePosition(bitmap);
        e.Handled = true;
    }
}
