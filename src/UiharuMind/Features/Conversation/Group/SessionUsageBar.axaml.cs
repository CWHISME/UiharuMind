using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>会话模型用量的统计行 + 占用进度条。数据上下文是 <see cref="SessionUsageStats"/></summary>
public partial class SessionUsageBar : UserControl
{
    /// <summary>加载布局</summary>
    public SessionUsageBar()
    {
        InitializeComponent();
    }
}
