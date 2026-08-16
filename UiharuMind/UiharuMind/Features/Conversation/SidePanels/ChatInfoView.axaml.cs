using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>聊天页右栏「会话详情」。面板固定三块，摆放在 axaml 里，本类只负责取到主壳数据</summary>
public partial class ChatInfoView : UserControl
{
    public ChatInfoView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<ChatInfoModel>();
    }
}
