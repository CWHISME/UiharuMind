using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 插话撤回：界面上的「等待插话…」提示条应在删除命令后消失。
/// 队列侧的撤回在 Core（<see cref="UiharuMind.Core.AI.Execution.ICharacterRunner.CancelInjectionsAsync"/>），
/// 本类只测界面命令的删行行为——无会话时 runner 为 null，命令应安全空走。
/// </summary>
public class InterjectionCancellationTests
{
    [Fact]
    public async Task RemoveInterjection_DropsThePendingRow()
    {
        ConversationViewModel vm = new();
        var message = new ChatMessage(ChatRole.User, "插话");
        vm.PendingInterjections.Add(new PendingInterjectionViewData(message, "插话"));
        Assert.Single(vm.PendingInterjections);

        await vm.RemoveInterjectionCommand.ExecuteAsync(message);

        Assert.Empty(vm.PendingInterjections);
    }

    [Fact]
    public async Task RemoveInterjection_RestoresTextAndAttachments()
    {
        ConversationViewModel vm = new();
        var attachment = new ConversationAttachment
        {
            Bytes = new byte[] { 1, 2, 3 },
            MediaType = "image/png",
        };
        var message = new ChatMessage(ChatRole.User, "插话");
        vm.PendingInterjections.Add(new PendingInterjectionViewData(message, "插话", [attachment]));
        Assert.Single(vm.PendingInterjections);

        await vm.RemoveInterjectionCommand.ExecuteAsync(message);

        Assert.Empty(vm.PendingInterjections);
        Assert.Equal("插话", vm.InputText);
        Assert.Single(vm.Tray.Attachments);
        Assert.Same(attachment, vm.Tray.Attachments[0]);
    }

    [Fact]
    public void RemoveInterjection_WithNullParameter_DoesNothing()
    {
        ConversationViewModel vm = new();
        vm.RemoveInterjectionCommand.Execute(null);
        Assert.Empty(vm.PendingInterjections);
    }

    [Fact]
    public void StopSending_RestoresPendingInterjectionsToComposer()
    {
        ConversationViewModel vm = new();
        var attachment = new ConversationAttachment
        {
            Bytes = new byte[] { 1, 2, 3 },
            MediaType = "image/png",
        };
        vm.PendingInterjections.Add(new PendingInterjectionViewData(new ChatMessage(ChatRole.User, "第一句"), "第一句", [attachment]));
        vm.PendingInterjections.Add(new PendingInterjectionViewData(new ChatMessage(ChatRole.User, "第二句"), "第二句"));
        Assert.Equal(2, vm.PendingInterjections.Count);

        vm.StopSendingCommand.Execute(null);

        Assert.Empty(vm.PendingInterjections);
        Assert.Equal("第一句\n第二句", vm.InputText);
        Assert.Single(vm.Tray.Attachments);
        Assert.Same(attachment, vm.Tray.Attachments[0]);
    }

    [Fact]
    public void StopSending_AppendsToExistingComposerText()
    {
        ConversationViewModel vm = new();
        vm.InputText = "草稿";
        vm.PendingInterjections.Add(new PendingInterjectionViewData(new ChatMessage(ChatRole.User, "插话"), "插话"));

        vm.StopSendingCommand.Execute(null);

        Assert.Equal("草稿\n插话", vm.InputText);
    }
}