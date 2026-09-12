using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.QuickChat;

/// <summary>
/// 承载一个会话的独立窗口：转临时对话与打开子会话都用它。
///
/// <b>文档型窗口</b>——系统标题栏、进任务栏、不置顶、可缩放。它不是浮窗：
/// 你会在里面来回对话、可能开着一阵子再切回来，而 <see cref="QuickWindowBase"/> 默认那套
/// （无标题栏、置顶、不进任务栏）是为转瞬即逝的快捷浮窗设的，用在这里三条全错：
/// 置顶压着别的活、alt-tab 回不来、窗口列表里认不出是哪一个。
///
/// 两种用途<b>刻意不做区分</b>：它们是同一种东西。临时对话从前顶着浮窗外观，
/// 只是因为它出生在快捷工具那一族。
/// </summary>
public partial class QuickChatViewWindow : QuickWindowBase
{
    private const double DefaultWidth = 500; //会话里有工具卡片与代码块,窄了每张都折行
    private const double DefaultHeight = 640;

    /// <summary>
    /// 文档型窗口要参与 macOS 的常规模式，否则应用停在附属态，
    /// 这个窗口进不了应用切换器——与 <see cref="ShowInTaskbar"/> 是同一件事的两面
    /// </summary>
    public override bool ContributesToMacRegularMode => true;

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
        // 本窗口是缓存复用的,再次打开会重新装载:先弃用上一个视图模型,
        // 否则它连着全局事件与可能还在跑的那一轮一起留在后台
        DisposeConversation();

        Title = chatSession.Title;

        ConversationViewModel conversation = new();
        // <b>必须在 LoadSessionAsync 之前</b>:装载途中会检查这一项,false 就把活推迟到
        // 「切回来再说」(那是为页面壳的会话列表设计的闸门)。浮窗没有页面壳替它维护,
        // 不自己声明就永远推迟——症状是窗口一片空白
        conversation.IsDisplayed = true;
        DataContext = conversation;
        _ = conversation.LoadSessionAsync(chatSession.ToMeta());
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        DisposeConversation();
    }

    private void DisposeConversation()
    {
        if (DataContext is ConversationViewModel previous) previous.Dispose();
    }

    public override void Awake()
    {
        base.Awake();
        // <b>必须在 base.Awake 之后</b>:QuickWindowBase.Awake 调 SetSimpledecorationWindow,
        // 把装饰/置顶/任务栏一并重置成浮窗那套,这里要覆盖回来
        this.SetDocumentWindow();
        Width = DefaultWidth;
        Height = DefaultHeight;
    }
}