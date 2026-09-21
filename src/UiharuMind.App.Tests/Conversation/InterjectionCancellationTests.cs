using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;

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
    public void RemoveInterjection_WithNullParameter_DoesNothing()
    {
        ConversationViewModel vm = new();
        vm.RemoveInterjectionCommand.Execute(null);
        Assert.Empty(vm.PendingInterjections);
    }
}