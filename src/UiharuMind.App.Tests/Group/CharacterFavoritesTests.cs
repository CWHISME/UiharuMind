using System.Linq;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Features.Characters;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 角色收藏：数据在设置里、变化有广播，三处角色列表（工作台左栏 / 内联选择器 / 建群挑人）共享同一套排序口径——
/// 收藏恒置顶，然后才是各自的档位轴。
/// </summary>
[Collection(CharacterLibraryCollection.Name)]
public class CharacterFavoritesTests
{
    private const string TempAgentId = "test-fav-agent";
    private const string TempChatId = "test-fav-chat";

    private static CharacterData AddTemp(string id, bool isAgent, string name)
    {
        CharacterData card = new()
        {
            CharacterId = id,
            IsAgent = isAgent,
            CharacterName = name,
        };
        card.NormalizeParams();
        Assert.True(CharacterManager.Instance.TryAddNewCharacterData(card));
        return card;
    }

    private static void Cleanup()
    {
        foreach (string id in SettingConfig.Current.FavoriteCharacterIds.ToList())
            SettingConfig.Current.FavoriteCharacterIds.Remove(id);
        SettingConfig.Current.Save();
        CharacterManager.Instance.DeleteCharacterData(TempAgentId);
        CharacterManager.Instance.DeleteCharacterData(TempChatId);
    }

    [Fact]
    public void Toggle_AddsAndRemoves_AndBroadcasts()
    {
        int changes = 0;
        void OnChanged() => changes++;
        CharacterFavorites.Changed += OnChanged;
        try
        {
            Assert.False(CharacterFavorites.IsFavorite(TempAgentId));
            CharacterFavorites.Toggle(TempAgentId);
            Assert.True(CharacterFavorites.IsFavorite(TempAgentId));
            CharacterFavorites.Toggle(TempAgentId);
            Assert.False(CharacterFavorites.IsFavorite(TempAgentId));
            Assert.Equal(2, changes);
        }
        finally
        {
            CharacterFavorites.Changed -= OnChanged;
            Cleanup();
        }
    }

    /// <summary>
    /// 左栏：收藏顶到最前（哪怕它是普通卡、翻过了整个智能体分区）；
    /// 「只看收藏」筛出来的一定全是收藏
    /// </summary>
    [Fact]
    public void Library_FavoritesPinToTheFront_AndFilterShowsOnlyFavorites()
    {
        AddTemp(TempAgentId, true, "试卡智能体");
        AddTemp(TempChatId, false, "试卡普通");
        try
        {
            CharacterFavorites.Toggle(TempChatId);
            CharacterListViewData list = new(new RecordingMessageService());

            Assert.Equal(TempChatId, list.Characters[0].CharacterId);

            list.FavoriteFilter = true;
            Assert.NotEmpty(list.Characters);
            Assert.All(list.Characters, c => Assert.True(c.IsFavorite));

            // 取消收藏：立刻掉出「只看收藏」，也不再置顶
            list.FavoriteFilter = false;
            CharacterFavorites.Toggle(TempChatId);
            Assert.NotEqual(TempChatId, list.Characters[0].CharacterId);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void Library_StarCommand_TogglesViaBroadcast()
    {
        AddTemp(TempChatId, false, "试卡普通");
        try
        {
            CharacterListViewData list = new(new RecordingMessageService());
            CharacterInfoViewData row = list.Characters.First(c => c.CharacterId == TempChatId);

            row.ToggleFavoriteCommand.Execute(null);
            Assert.True(row.IsFavorite);
            // 广播重建过列表，收藏项顶到最前
            Assert.Equal(TempChatId, list.Characters[0].CharacterId);

            row = list.Characters.First();
            row.ToggleFavoriteCommand.Execute(null);
            Assert.False(row.IsFavorite);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>建群挑人的档位轴按群类型定序：智能体群智能体在前、普通群普通在前</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Group_KindAxisFollowsGroupType(bool agentsFirst)
    {
        AddTemp(TempAgentId, true, "试卡智能体");
        AddTemp(TempChatId, false, "试卡普通");
        try
        {
            GroupCreateWindowModel model = new(agentsFirst, null);
            AssertAllAgentsBeforeChats(model.Picker.Candidates, agentsFirst);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>收藏跨档置顶：智能体群里收藏一张普通卡，它顶到所有智能体前面</summary>
    [Fact]
    public void Group_FavoritePinsAboveKind()
    {
        AddTemp(TempAgentId, true, "试卡智能体");
        AddTemp(TempChatId, false, "试卡普通");
        try
        {
            CharacterFavorites.Toggle(TempChatId);
            GroupCreateWindowModel model = new(true, null);
            Assert.Equal(TempChatId, model.Picker.Candidates[0].Data.CharacterId);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>候选行的星标：点完立刻收藏并重排到最前，不用等下次开窗</summary>
    [Fact]
    public void Group_CandidateStar_TogglesAndReordersImmediately()
    {
        AddTemp(TempChatId, false, "试卡普通");
        try
        {
            GroupCreateWindowModel model = new(true, null);
            GroupCandidate candidate = model.Picker.Candidates.First(c => c.Data.CharacterId == TempChatId);
            Assert.NotEqual(TempChatId, model.Picker.Candidates[0].Data.CharacterId);

            candidate.ToggleFavoriteCommand.Execute(null);

            Assert.True(CharacterFavorites.IsFavorite(TempChatId));
            Assert.Equal(TempChatId, model.Picker.Candidates[0].Data.CharacterId);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>角色选择器（内联挑人）也按收藏置顶，与左栏同一口径</summary>
    [Fact]
    public void Picker_FavoritesPinToTheFront()
    {
        AddTemp(TempAgentId, true, "ZetaAgent");
        AddTemp(TempChatId, false, "AlphaChat");
        try
        {
            CharacterFavorites.Toggle(TempAgentId); //收藏名字靠后的那张，置顶后必须在前面
            CharacterPickerViewData picker = new(_ => { });

            Assert.Equal(TempAgentId, picker.Items[0].Character.CharacterId);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>跳过开头全是档位轴第一段后，剩下必须全是另一档——两档不该交错</summary>
    /// <param name="candidates">候选</param>
    /// <param name="agentsFirst">智能体群：智能体是第一段</param>
    private static void AssertAllAgentsBeforeChats(IEnumerable<GroupCandidate> candidates, bool agentsFirst)
    {
        List<bool> kinds = candidates.Select(c => c.Data.IsAgent).ToList();
        Assert.NotEmpty(kinds);
        int firstOther = kinds.FindIndex(k => k != agentsFirst);
        if (firstOther >= 0)
            Assert.All(kinds.Skip(firstOther), k => Assert.Equal(!agentsFirst, k));
    }
}