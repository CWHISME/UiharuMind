using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>
/// 建群弹窗的挑人筛选：一条档位轴 + 搜索。
///
/// 钉的是<b>搜索要比描述</b>——角色的定位只写在描述里，只比名字会让人搜「审核者」搜不到人。
/// 候选与胶囊的选项都是枚举派生的，这里顺带钉住「选项跟着枚举走」：档位一改，
/// 胶囊的选中项也要跟着换，否则界面会停在一个已经筛不出来的档上。
/// </summary>
public class GroupCreateFilterTests
{
    private const string TempAgentId = "test-group-search-agent";
    private const string TempAgentDescription = "试卡专属的定位词-zh";

    public GroupCreateFilterTests()
    {
        CharacterManager.Instance.OnInitialize();
    }

    /// <summary>造一张用户自己建的卡，用完删掉，不留在测试数据目录里</summary>
    private static CharacterData AddTempUserAgent()
    {
        CharacterData card = new()
        {
            CharacterId = TempAgentId,
            IsAgent = true,
            CharacterName = "试卡智能体",
            Description = TempAgentDescription,
        };
        card.NormalizeParams();
        Assert.True(CharacterManager.Instance.TryAddNewCharacterData(card));
        return card;
    }

    /// <summary>默认不过滤，候选就是全部能进群的卡</summary>
    [Fact]
    public void ByDefault_KindFilterPassesEverythingThrough()
    {
        GroupCreateWindowModel model = new(true, null);

        Assert.Equal(ECharacterKindFilter.All, model.Picker.KindFilter);
        Assert.NotEmpty(model.Picker.Candidates);
        Assert.NotEmpty(model.Picker.KindPills);
    }

    /// <summary>档位轴只管档位：筛出来的必须全都落在那一档，另一档一档都不许漏进来</summary>
    [Fact]
    public void KindFilter_LandsOnExactlyOneAxis()
    {
        GroupCreateWindowModel model = new(true, null);

        model.Picker.KindFilter = ECharacterKindFilter.Agent;
        Assert.NotEmpty(model.Picker.Candidates);
        Assert.All(model.Picker.Candidates, c => Assert.True(c.Data.IsAgent));

        model.Picker.KindFilter = ECharacterKindFilter.Chat;
        Assert.NotEmpty(model.Picker.Candidates);
        Assert.All(model.Picker.Candidates, c => Assert.False(c.Data.IsAgent));
    }

    /// <summary>胶囊的选中态是快照，跟着模型走：换档之后必须重高亮，否则界面停在一个筛不出东西的档上</summary>
    [Fact]
    public void Pills_TrackTheSelectedFilter()
    {
        (ECharacterKindFilter Value, string Label)[] options = [.. CharacterFilterPresentation.KindOptions()];
        GroupCreateWindowModel model = new(true, null);

        // 默认是「全部」那一档
        Assert.Equal(options[0].Label, model.Picker.KindPills.Single(p => p.IsSelected).Label);

        model.Picker.KindFilter = ECharacterKindFilter.Agent;

        Assert.Equal(options[2].Label, model.Picker.KindPills.Single(p => p.IsSelected).Label);
        // 选中项恒为一项：两个都亮或都不亮都是坏了
        Assert.Single(model.Picker.KindPills, p => p.IsSelected);
    }

    /// <summary>
    /// 搜索要能命中描述里的定位词。这条曾经不成立：建群是三处挑选界面里唯一只比名字的，
    /// 于是搜「审核者」在角色库搜得到、在建群一个人都搜不到。
    /// </summary>
    [Fact]
    public void Search_HitsThePositioningWordInDescription()
    {
        CharacterData mine = AddTempUserAgent();
        try
        {
            GroupCreateWindowModel model = new(true, null);

            model.Picker.SearchText = "试卡智能体"; //名字
            Assert.Contains(model.Picker.Candidates, c => c.Data.CharacterId == mine.CharacterId);

            model.Picker.SearchText = TempAgentDescription; //只有描述里有
            Assert.Contains(model.Picker.Candidates, c => c.Data.CharacterId == mine.CharacterId);

            model.Picker.SearchText = "查无此人";
            Assert.Empty(model.Picker.Candidates);
        }
        finally
        {
            CharacterManager.Instance.DeleteCharacterData(TempAgentId);
        }
    }
}
