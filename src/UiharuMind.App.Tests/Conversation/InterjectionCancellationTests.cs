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
        vm.Interjections.Items.Add(new PendingInterjectionViewData(message, "插话"));
        Assert.Single(vm.Interjections.Items);

        await vm.Interjections.RemoveCommand.ExecuteAsync(message);

        Assert.Empty(vm.Interjections.Items);
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
        vm.Interjections.Items.Add(new PendingInterjectionViewData(message, "插话", [attachment]));
        Assert.Single(vm.Interjections.Items);

        await vm.Interjections.RemoveCommand.ExecuteAsync(message);

        Assert.Empty(vm.Interjections.Items);
        Assert.Equal("插话", vm.InputText);
        Assert.Single(vm.Tray.Attachments);
        Assert.Same(attachment, vm.Tray.Attachments[0]);
    }

    [Fact]
    public void RemoveInterjection_WithNullParameter_DoesNothing()
    {
        ConversationViewModel vm = new();
        vm.Interjections.RemoveCommand.Execute(null);
        Assert.Empty(vm.Interjections.Items);
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
        vm.Interjections.Items.Add(new PendingInterjectionViewData(new ChatMessage(ChatRole.User, "第一句"), "第一句", [attachment]));
        vm.Interjections.Items.Add(new PendingInterjectionViewData(new ChatMessage(ChatRole.User, "第二句"), "第二句"));
        Assert.Equal(2, vm.Interjections.Items.Count);

        vm.StopSendingCommand.Execute(null);

        Assert.Empty(vm.Interjections.Items);
        Assert.Equal("第一句\n第二句", vm.InputText);
        Assert.Single(vm.Tray.Attachments);
        Assert.Same(attachment, vm.Tray.Attachments[0]);
    }

    [Fact]
    public void StopSending_AppendsToExistingComposerText()
    {
        ConversationViewModel vm = new();
        vm.InputText = "草稿";
        vm.Interjections.Items.Add(new PendingInterjectionViewData(new ChatMessage(ChatRole.User, "插话"), "插话"));

        vm.StopSendingCommand.Execute(null);

        Assert.Equal("草稿\n插话", vm.InputText);
    }
}