using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>从单聊开群时，建群弹窗预先勾上原单聊的角色、填好群名</summary>
[Collection(CharacterLibraryCollection.Name)]
public class GroupCreateWindowModelTests
{
    /// <summary>原单聊的角色是屏蔽卡时也照样预选：用户正在跟他聊，本来就看得见</summary>
    [Fact]
    public void Preselected_IsPickedFirst_AndNameIsFilled_EvenWhenShielded()
    {
        CharacterManager.Instance.OnInitialize();
        CharacterData source = CharacterManager.Instance.CharacterDataDictionary.Values
            .First(x => x.IsShielded && !x.IsInternal && GroupChatSessions.CanJoin(x));
        Assert.False(CharacterVisibility.ShowShielded); //测试数据目录没有解锁标记

        GroupCreateWindowModel model = new(true, null, [source], "和她的单聊");

        Assert.Equal([source], model.Picked);
        Assert.Equal("和她的单聊", model.Name);
        Assert.False(model.CanCreate); //还差至少一位
    }
}
