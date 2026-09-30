using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>建群之后加人的弹窗：已在群里的不列，退群的挂徽章、从「加回」进来时预先勾上</summary>
[Collection(CharacterLibraryCollection.Name)]
public class GroupAddMembersWindowModelTests
{
    [Fact]
    public void ListsFormerButNotPresent_AndRejoinIsPreselected()
    {
        List<CharacterData> cards = CharacterManager.Instance.CharacterDataDictionary.Values
            .Where(x => !x.IsInternal && GroupChatSessions.CanJoin(x) && CharacterVisibility.PassesShield(x))
            .Take(3)
            .ToList();
        Assert.Equal(3, cards.Count);
        ChatSession group = GroupChatSessions.Create("g-add", false, cards, null);
        try
        {
            ChatSession leaving = SessionManager.Instance.GetGroupMembers(group.SessionId)
                .Select(x => SessionManager.Instance.Load(x.SessionId)!)
                .First(x => x.CharacterId == cards[2].CharacterId);
            Assert.Equal(EGroupRosterEdit.Done, GroupMembership.Remove(group, leaving));

            GroupAddMembersWindowModel model = new(group, cards[2]);

            List<GroupCandidate> candidates = [..model.Picker.Candidates];
            Assert.DoesNotContain(candidates, x => x.Data.CharacterId == cards[0].CharacterId);
            Assert.DoesNotContain(candidates, x => x.Data.CharacterId == cards[1].CharacterId);
            GroupCandidate former = Assert.Single(candidates, x => x.Data.CharacterId == cards[2].CharacterId);
            Assert.True(former.WasMember);
            Assert.True(former.IsPicked);
            Assert.True(model.CanAdd);
            Assert.Equal(EGroupBackfill.None, model.Backfill); //默认不补
        }
        finally
        {
            SessionManager.Instance.Delete(group.SessionId);
        }
    }
}
