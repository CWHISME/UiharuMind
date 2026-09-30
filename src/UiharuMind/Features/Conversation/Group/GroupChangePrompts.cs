using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 建群之后改群的几件事（主持人、名单、工作区、群名）共用的两道关：空闲才许改、跑过才问。
/// 它们的代价一样——全员系统提示改写、各自下一轮前缀缓存失效（ADR 0046 修订「建群之后增删成员」）
/// </summary>
internal static class GroupChangePrompts
{
    private static IMessageService Messages => App.Services.GetRequiredService<IMessageService>();

    /// <summary>
    /// 群此刻能不能改名单或工作区；不能就提示一句
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>能改为 true</returns>
    public static bool EnsureIdle(ChatSession group)
    {
        if (GroupMembership.CanEdit(group)) return true;
        Messages.ShowNotification(Loc.Text(LangKey.GroupEditBusy), severity: MessageSeverity.Warning);
        return false;
    }

    /// <summary>
    /// 群跑过才弹确认：没跑过的群没有缓存可失效，直接改
    /// </summary>
    /// <param name="hasRun">群跑过（有累计花费）</param>
    /// <param name="message">确认正文</param>
    /// <returns>可以改为 true</returns>
    public static Task<bool> ConfirmIfRunAsync(bool hasRun, string message) =>
        hasRun ? Messages.ConfirmAsync(message) : Task.FromResult(true);

    /// <summary>
    /// 一定弹确认，跑过的群再补一句缓存失效的代价
    /// </summary>
    /// <param name="hasRun">群跑过（有累计花费）</param>
    /// <param name="message">确认正文</param>
    /// <returns>可以改为 true</returns>
    public static Task<bool> ConfirmAsync(bool hasRun, string message) =>
        Messages.ConfirmAsync(WithCacheNote(hasRun, message));

    /// <summary>
    /// 跑过的群在正文后补上缓存失效的代价
    /// </summary>
    /// <param name="hasRun">群跑过（有累计花费）</param>
    /// <param name="message">正文</param>
    /// <returns>补过的正文</returns>
    public static string WithCacheNote(bool hasRun, string message) =>
        hasRun ? message + "\n\n" + Loc.Text(LangKey.GroupRosterCacheNote) : message;

    /// <summary>
    /// 群改名：空闲才许，跑过的群先确认（群名写在各成员的场景段里）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="name">新群名</param>
    /// <returns>可以改为 true</returns>
    public static async Task<bool> ConfirmRenameAsync(ChatSession group, string name)
    {
        if (!EnsureIdle(group)) return false;
        if (!await ConfirmIfRunAsync(HasRun(group), Loc.Text(LangKey.GroupRenameConfirm, name))) return false;
        return EnsureIdle(group);
    }

    /// <summary>
    /// 弹一条提示
    /// </summary>
    /// <param name="message">正文</param>
    /// <param name="severity">严重度</param>
    public static void Notify(string message, MessageSeverity severity = MessageSeverity.Information) =>
        Messages.ShowNotification(message, severity: severity);

    // 与右栏全群累计同一口径：有成员花过 token 才算跑过
    private static bool HasRun(ChatSession group) =>
        GroupRoster.Of(group).Present.Any(x => SessionManager.Instance.Load(x.SessionId) is { } member
                                               && member.TotalInputTokens + member.TotalOutputTokens > 0);
}
