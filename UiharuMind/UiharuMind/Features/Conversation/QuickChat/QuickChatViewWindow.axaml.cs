using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.QuickChat;

/// <summary>
/// 承载一个会话的独立窗口：转临时对话与打开子会话都用它。
///
/// <b>文档型窗口</b>——系统标题栏、进任务栏、可缩放。它不是浮窗：
/// 你会在里面来回对话、可能开着一阵子再切回来，而 <see cref="QuickWindowBase"/> 默认那套
/// （无标题栏、不进任务栏）是为转瞬即逝的快捷浮窗设的，用在这里两条都错：
/// alt-tab 回不来、窗口列表里认不出是哪一个。
///
/// 置顶是<b>例外</b>：开着它多半是为了盯子代理跑到哪了，一边在别处干活一边瞄一眼，
/// 被别的窗口压住就白开了。
///
/// 两种用途<b>刻意不做区分</b>：它们是同一种东西。临时对话从前顶着浮窗外观，
/// 只是因为它出生在快捷工具那一族。
/// </summary>
public partial class QuickChatViewWindow : QuickWindowBase
{
    private const double DefaultWidth = 500; //会话里有工具卡片与代码块,窄了每张都折行
    private const double DefaultHeight = 666;

    /// <summary>
    /// 文档型窗口要参与 macOS 的常规模式，否则应用停在附属态，
    /// 这个窗口进不了应用切换器——与 <see cref="ShowInTaskbar"/> 是同一件事的两面
    /// </summary>
    public override bool ContributesToMacRegularMode => true;
    
    public override bool IsAuxiliaryWindow => true;

    /// <summary>本窗口此刻装载的会话；已关闭（视图模型已弃用）时为 null</summary>
    public string? SessionId { get; private set; }

    /// <summary>
    /// 打开一个承载给定会话的临时对话窗口。<b>一个会话只对应一扇窗</b>——工具卡片的
    /// 「查看过程」、右栏「子代理」面板、会话列表点到的可能是同一个子会话，再开一扇
    /// 就是同一段对话的两份视图：两个视图模型各自挂着全局事件、各自观察同一轮内容流。
    /// </summary>
    /// <param name="chatSession">会话本体(转临时对话前已持久化)</param>
    public static void Show(ChatSession chatSession)
    {
        // 开窗那条路自己 marshal(UIManager.ShowWindow),回到旧窗这条路得自己来
        Dispatcher.UIThread.Invoke(() =>
        {
            if (FindOpened(chatSession.SessionId) is { } opened)
            {
                opened.WindowState = WindowState.Normal; //可能被最小化了
                opened.RequestFocus();
                // 与 UIManager 开窗那条路同一口径:文档型窗口取焦点要连整应用一起激活
                MacApplicationActivationService.ActivateIgnoringOtherApps();
                //不重新 SetSession:那会弃掉正看着的视图模型再整段重载,滚动位置与展开状态全丢
                return;
            }

            UIManager.ShowWindow<QuickChatViewWindow>(x => x.SetSession(chatSession), isMulti: true);
        });
    }

    /// <summary>找出正显示着这个会话的窗口。只认可见的:关掉的那些视图模型已经弃用，复用它们要走装载那条路</summary>
    private static QuickChatViewWindow? FindOpened(string sessionId)
    {
        foreach (UiharuWindowBase window in UIManager.GetWindows<QuickChatViewWindow>())
        {
            if (window is QuickChatViewWindow { IsVisible: true } chat && chat.SessionId == sessionId) return chat;
        }

        return null;
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

        SessionId = chatSession.SessionId;
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
        SessionId = null;
    }

    public override void Awake()
    {
        base.Awake();
        // <b>必须在 base.Awake 之后</b>:QuickWindowBase.Awake 调 SetSimpledecorationWindow,
        // 把装饰/置顶/任务栏一并重置成浮窗那套,这里要覆盖回来
        this.SetDocumentWindow();
        Topmost = true; //盯子代理跑到哪了要一直看得见,SetDocumentWindow 把它关了,这里覆盖回来
        Width = DefaultWidth;
        Height = DefaultHeight;
    }
}