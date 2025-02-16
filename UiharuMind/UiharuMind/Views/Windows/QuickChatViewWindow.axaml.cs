using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 临时对话
/// </summary>
public partial class QuickChatViewWindow : QuickWindowBase
{
    public static void Show(ChatViewModel model)
    {
        model.ChatSession.Active();
        UIManager.ShowWindow<QuickChatViewWindow>(x => x.ChatModel = model, isMulti: true);
    }

    public QuickChatViewWindow()
    {
        InitializeComponent();
    }

    public ChatViewModel? ChatModel
    {
        get => DataContext as ChatViewModel;
        set
        {
            DataContext = value;
            TitleTextBlock.Text =
                $"{ChatModel?.ChatSession.Name}";
            TitleSecondaryTextBlock.Text = $"({ChatModel?.ChatSession.ChatSession.ChatModelRunningData?.ModelName})";
        }
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
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