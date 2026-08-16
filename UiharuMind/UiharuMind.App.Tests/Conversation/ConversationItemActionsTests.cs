using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 气泡上那一行操作按钮的显隐。
///
/// 它们绑的是 <c>CanEdit</c> 等四个计算属性，而属性取值由回调是否挂上决定。
/// 时序上气泡<b>先上屏、一轮结束后</b>才由 <c>WireItemActions</c> 接上回调，
/// 所以赋值必须抛通知——不抛的话悬停菜单一旦在接线之前实例化过，
/// 就会一直停在"只有复制按钮"，要切走会话重建条目才恢复。
/// </summary>
public class ConversationItemActionsTests
{
    private static (TextConversationItem Item, List<string> Changed) NewItem()
    {
        TextConversationItem item = new(true);
        List<string> changed = new();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        return (item, changed);
    }

    [Fact]
    public void WiringTheEditCallback_AnnouncesCanEdit()
    {
        (TextConversationItem item, List<string> changed) = NewItem();
        Assert.False(item.CanEdit);

        item.EditedCallback = _ => { };

        Assert.True(item.CanEdit);
        Assert.Contains(nameof(item.CanEdit), changed);
    }

    [Fact]
    public void WiringTheDeleteCallback_AnnouncesCanDelete()
    {
        (TextConversationItem item, List<string> changed) = NewItem();
        Assert.False(item.CanDelete);

        item.DeleteCallback = _ => { };

        Assert.True(item.CanDelete);
        Assert.Contains(nameof(item.CanDelete), changed);
    }

    [Fact]
    public void WiringTheRetryCallback_AnnouncesCanRetry()
    {
        (TextConversationItem item, List<string> changed) = NewItem();
        Assert.False(item.CanRetry);

        item.RetryCallback = _ => { };

        Assert.True(item.CanRetry);
        Assert.Contains(nameof(item.CanRetry), changed);
    }

    [Fact]
    public void WiringTheBranchCallback_AnnouncesCanBranch()
    {
        (TextConversationItem item, List<string> changed) = NewItem();
        Assert.False(item.CanBranch);

        item.BranchCallback = _ => { };

        Assert.True(item.CanBranch);
        Assert.Contains(nameof(item.CanBranch), changed);
    }
}
