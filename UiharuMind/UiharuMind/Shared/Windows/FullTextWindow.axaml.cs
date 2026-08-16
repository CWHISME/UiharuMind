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

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 全文窗：把一段可能很长的文本从卡片里请出去单独看。
///
/// 非模态且刻意允许多开（<c>isMulti: true</c>）——参数与结果、两次调用的结果，
/// 都得能并排对着看，模态或单例都做不到这件事。
/// 只读由构造成立：窗口里只有一个 <see cref="Controls.LongTextView"/>，没有任何回写路径。
/// </summary>
public partial class FullTextWindow : QuickWindowBase
{
    /// <summary>
    /// 打开一段长文本的只读全文窗
    /// </summary>
    /// <param name="title">标题栏文案</param>
    /// <param name="text">正文全文</param>
    public static void Show(string title, string text)
    {
        UIManager.ShowWindow<FullTextWindow>(x => x.SetSource(title, text), isMulti: true);
    }

    public FullTextWindow()
    {
        InitializeComponent();
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
    }

    /// <summary>
    /// 装载正文。窗口可被复用（<see cref="UiharuWindowBase.IsCacheWindow"/>），
    /// 换源时必须把滚动位置显式复位，否则新内容停在上一份的位置上
    /// </summary>
    /// <param name="title">标题栏文案</param>
    /// <param name="text">正文全文</param>
    public void SetSource(string title, string text)
    {
        Title = title;
        TitleTextBlock.Text = title;
        TextView.Text = text;
        TextView.ScrollToTop();
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    private void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        TextView.CopyAll();
    }

    private void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        TextView.OpenSearch();
    }
}
