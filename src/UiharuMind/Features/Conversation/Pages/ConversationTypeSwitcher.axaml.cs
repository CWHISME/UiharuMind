using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>左栏顶部的类型切换器 + 新建按钮。只负责摆控件，状态在 ConversationPageData</summary>
public partial class ConversationTypeSwitcher : UserControl
{
    public ConversationTypeSwitcher()
    {
        InitializeComponent();
    }
}