using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Shared.Shell;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 聊天页右栏「会话详情」的主壳数据：持有三块面板的子模型，并把会话切换分发给它们。
///
/// 这里曾是一套插件框架（基类 + 泛型视图工厂 + Type 键缓存 + 事件 + 手工拼 Children），
/// 而注册表始终是硬编码的三项、三个生命周期钩子里有一个零覆写——间接层没有买到任何扩展性，
/// 故改回直白的「三个字段 + 直接调用」，面板由 ChatInfoView.axaml 静态摆放。
///
/// ⚠️ 类名保留：它语义上是 *ViewData，但属术语表点名的历史遗留三例之一，不改也不新增。
/// </summary>
public partial class ChatInfoModel : ViewModelBase
{
    /// <summary>用户卡片。与会话无关，只有显不显示跟会话有关</summary>
    public UserCardViewData UserCard { get; } = new();

    /// <summary>翻译语言</summary>
    public TranslationViewData Translation { get; } = new();

    /// <summary>对话参数</summary>
    public ChatParamsViewData ChatParams { get; } = new();

    /// <summary>是否有会话可展示（无会话时整栏内容为空）</summary>
    [ObservableProperty] private bool _hasSession;

    /// <summary>
    /// 切换详情栏对应的会话（由聊天页面壳在会话选择变化时调用）
    /// </summary>
    /// <param name="session">会话视图数据，为空清空面板</param>
    public void SetSession(SessionListItem? session)
    {
        HasSession = session != null;
        if (session == null)
        {
            UserCard.IsShown = false;
            return;
        }

        //这块面板编辑的就是用户卡,因此按"本角色是否注入用户卡"决定要不要它
        UserCard.IsShown = session.Session.CharacterData.InjectUserCard;
        Translation.SetSession(session);
        ChatParams.SetSession(session);
    }

    /// <summary>
    /// 通知一轮生成开始。只有对话参数面板关心它：把用户刚改过的参数落盘
    /// </summary>
    public void NotifyChatBegin()
    {
        ChatParams.NotifyChatBegin();
    }
}
