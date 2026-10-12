using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>合并后的对话页：壳与三处内容槽。交互与数据都在 ConversationPageData</summary>
public partial class ConversationPage : UserControl
{
    public ConversationPage()
    {
        long ctorBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
        InitializeComponent();
        global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End("page/conversation-page-ctor", ctorBegin);
    }
}