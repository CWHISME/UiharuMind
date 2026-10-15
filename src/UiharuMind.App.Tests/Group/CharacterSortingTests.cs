using System;
using System.Linq;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Features.Characters;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 角色排序的统一口径（<see cref="CharacterSorting"/>）：收藏恒置顶 → 分区轴 → 作品分组
/// （未填作品的排最前）→ 角色名。三处角色列表（工作台左栏 / 内联选择器 / 建群挑人）共用它，
/// 这里钉住轴序本身，再用三份列表各做一条端到端确认，防止以后有人改回各排各的。
/// </summary>
[Collection(CharacterLibraryCollection.Name)]
public class CharacterSortingTests
{
    private const string TempWorksAId = "test-sort-works-a";
    private const string TempWorksBId = "test-sort-works-b";
    private const string TempAgentId = "test-sort-agent";

    private static CharacterData AddTemp(string id, bool isAgent, string name, string? works)
    {
        CharacterData card = new()
        {
            CharacterId = id,
            IsAgent = isAgent,
            CharacterName = name,
            Works = works ?? "",
        };
        card.NormalizeParams();
        Assert.True(CharacterManager.Instance.TryAddNewCharacterData(card));
        return card;
    }

    private static void Cleanup()
    {
        CharacterManager.Instance.DeleteCharacterData(TempWorksAId);
        CharacterManager.Instance.DeleteCharacterData(TempWorksBId);
        CharacterManager.Instance.DeleteCharacterData(TempAgentId);
        foreach (string id in SettingConfig.Current.FavoriteCharacterIds.ToList())
            SettingConfig.Current.FavoriteCharacterIds.Remove(id);
        SettingConfig.Current.Save();
    }

    /// <summary>空作品在前：同分区里，没填作品的卡排在填了作品的前面</summary>
    [Fact]
    public void Compare_EmptyWorksGoBeforeFilled()
    {
        CharacterData empty = AddTemp(TempWorksAId, false, "张", null);
        CharacterData filled = AddTemp(TempWorksBId, false, "阿", "魔法禁书目录");
        try
        {
            Assert.True(CharacterSorting.Compare(empty, filled, _ => 0) < 0);
            Assert.True(CharacterSorting.Compare(filled, empty, _ => 0) > 0);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>作品组相邻、组按作品名排：作品 B/A/B 的卡应排成 A、B、B，而不是按卡名穿插</summary>
    [Fact]
    public void Compare_WorksGroupByName()
    {
        CharacterData worksA = AddTemp(TempWorksAId, false, "阿卡", "作品甲");
        CharacterData worksB = AddTemp(TempWorksBId, false, "丙卡", "作品乙");
        try
        {
            // 作品名小的在前：作品甲(jiǎ) &lt; 作品乙(yǐ)，名字虽阿(ā)&lt;丙(bǐng)也不抢作品轴
            Assert.True(CharacterSorting.Compare(worksA, worksB, _ => 0) < 0);
            // 同作品时退到角色名：阿卡(ā) &lt; 乙卡(yǐ)
            CharacterData sameWorksLater = AddTemp("test-sort-works-c", false, "乙卡", "作品甲");
            try
            {
                Assert.True(CharacterSorting.Compare(worksA, sameWorksLater, _ => 0) < 0);
            }
            finally
            {
                CharacterManager.Instance.DeleteCharacterData("test-sort-works-c");
            }
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>收藏置顶优先于一切：即使它是「填了作品」的那张，也排在「空作品」的前面</summary>
    [Fact]
    public void Compare_FavoritePinsAboveWorksAxis()
    {
        CharacterData empty = AddTemp(TempWorksAId, false, "张", null);
        CharacterData filled = AddTemp(TempWorksBId, false, "阿", "魔法禁书目录");
        try
        {
            CharacterFavorites.Toggle(filled.CharacterId);
            Assert.True(CharacterSorting.Compare(filled, empty, _ => 0) < 0);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>分区轴在作品轴之前：左栏口径普通在前，普通卡即使填了作品也排在空作品智能体前面</summary>
    [Fact]
    public void Compare_KindAxisBeforeWorksAxis()
    {
        CharacterData chatFilled = AddTemp(TempWorksAId, false, "普通卡", "魔法禁书目录");
        CharacterData agentEmpty = AddTemp(TempAgentId, true, "智能体卡", null);
        try
        {
            Assert.True(CharacterSorting.Compare(chatFilled, agentEmpty, c => c.IsAgent ? 1 : 0) < 0);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>端到端·左栏：空作品卡排在填了作品卡的前面</summary>
    [Fact]
    public void Library_EmptyWorksAppearBeforeFilled()
    {
        AddTemp(TempWorksAId, false, "试卡空作品", null);
        AddTemp(TempWorksBId, false, "试卡有作品", "测试作品甲");
        try
        {
            CharacterListViewData list = new(new RecordingMessageService());
            int emptyIndex = list.Characters.ToList().FindIndex(c => c.CharacterId == TempWorksAId);
            int filledIndex = list.Characters.ToList().FindIndex(c => c.CharacterId == TempWorksBId);
            Assert.True(emptyIndex >= 0 && filledIndex >= 0 && emptyIndex < filledIndex);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>端到端·内联选择器：空作品卡排在填了作品卡的前面</summary>
    [Fact]
    public void Picker_EmptyWorksAppearBeforeFilled()
    {
        AddTemp(TempWorksAId, false, "试卡空作品", null);
        AddTemp(TempWorksBId, false, "试卡有作品", "测试作品甲");
        try
        {
            CharacterPickerViewData picker = new(_ => { });
            int emptyIndex = picker.Items.ToList().FindIndex(i => i.Character.CharacterId == TempWorksAId);
            int filledIndex = picker.Items.ToList().FindIndex(i => i.Character.CharacterId == TempWorksBId);
            Assert.True(emptyIndex >= 0 && filledIndex >= 0 && emptyIndex < filledIndex);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>端到端·建群挑人：分区轴之外，空作品卡排在填了作品卡的前面</summary>
    [Fact]
    public void GroupPicker_EmptyWorksAppearBeforeFilled_WithinSameKind()
    {
        AddTemp(TempWorksAId, false, "试卡空作品", null);
        AddTemp(TempWorksBId, false, "试卡有作品", "测试作品甲");
        try
        {
            GroupCreateWindowModel model = new(true, null);
            int emptyIndex = model.Picker.Candidates.ToList().FindIndex(c => c.Data.CharacterId == TempWorksAId);
            int filledIndex = model.Picker.Candidates.ToList().FindIndex(c => c.Data.CharacterId == TempWorksBId);
            Assert.True(emptyIndex >= 0 && filledIndex >= 0 && emptyIndex < filledIndex);
        }
        finally
        {
            Cleanup();
        }
    }
}
