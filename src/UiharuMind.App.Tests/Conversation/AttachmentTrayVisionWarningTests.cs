using System.ComponentModel;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 附件盘发图警示的<b>刷新接线</b>。判据本身是 Core 侧的纯函数（见 VisionFallbackTests），
/// 这里钉死的是「什么时候重算」——接线断了症状很难看：加了图不出警示，或者删了图警示不消失，
/// 而属性本身算得完全正确，所以不会有任何报错。
/// </summary>
public class AttachmentTrayVisionWarningTests
{
    private static AttachmentTrayViewData CreateTray() => new(() => null, () => null);

    /// 只记属性名,不去读属性值——读值会碰 LlmManager 单例,那不是本测试要验的东西
    private static List<string> TrackNotifications(AttachmentTrayViewData tray)
    {
        List<string> notified = new();
        ((INotifyPropertyChanged)tray).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) notified.Add(e.PropertyName);
        };
        return notified;
    }

    [Fact]
    public void AddingAttachment_RecomputesWarning()
    {
        AttachmentTrayViewData tray = CreateTray();
        List<string> notified = TrackNotifications(tray);

        tray.AddAttachmentBytes([1, 2, 3]);

        Assert.Contains(nameof(AttachmentTrayViewData.IsImageUnreadableWarningVisible), notified);
    }

    [Fact]
    public void RemovingAttachment_RecomputesWarning()
    {
        AttachmentTrayViewData tray = CreateTray();
        tray.AddAttachmentBytes([1, 2, 3]);
        List<string> notified = TrackNotifications(tray);

        tray.Attachments.RemoveAt(0);

        Assert.Contains(nameof(AttachmentTrayViewData.IsImageUnreadableWarningVisible), notified);
    }

    [Fact]
    public void TakePending_RecomputesWarning()
    {
        // 发送那一刻附件被取空,警示必须跟着收起来
        AttachmentTrayViewData tray = CreateTray();
        tray.AddAttachmentBytes([1, 2, 3]);
        List<string> notified = TrackNotifications(tray);

        Assert.NotNull(tray.TakePending());

        Assert.Contains(nameof(AttachmentTrayViewData.IsImageUnreadableWarningVisible), notified);
    }

    [Fact]
    public void NotifyVisionStateChanged_RecomputesWarning()
    {
        // 换模型/换角色时宿主视图模型调的就是它:附件盘自己订阅不到那两件事
        AttachmentTrayViewData tray = CreateTray();
        List<string> notified = TrackNotifications(tray);

        tray.NotifyVisionStateChanged();

        Assert.Contains(nameof(AttachmentTrayViewData.IsImageUnreadableWarningVisible), notified);
    }
}
