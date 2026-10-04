using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Group;

namespace UiharuMind.App.Tests.Group;

/// <summary>从单聊开群时，建群弹窗预先勾上原单聊的角色、填好群名；从群开群时带上原群的成员、模型与调度</summary>
[Collection(CharacterLibraryCollection.Name)]
public class GroupCreateWindowModelTests
{
    /// <summary>原单聊的角色是屏蔽卡时也照样预选：用户正在跟他聊，本来就看得见</summary>
    [Fact]
    public void Preselected_IsPickedFirst_AndNameIsFilled_EvenWhenShielded()
    {
        CharacterData source = CharacterManager.Instance.CharacterDataDictionary.Values
            .First(x => x.IsShielded && !x.IsInternal && GroupChatSessions.CanJoin(x));
        Assert.False(CharacterVisibility.ShowShielded); //测试数据目录没有解锁标记

        GroupCreateWindowModel model = new(true, null, [source], "和她的单聊");

        Assert.Equal([source], model.Picked);
        Assert.Equal("和她的单聊", model.Name);
        Assert.False(model.CanCreate); //还差至少一位
    }

    /// <summary>从群开群：成员、各自的模型、调度与主持人都在弹窗里预填好，确认时原样带出</summary>
    [Fact]
    public void CopyParameters_FillMembersModelsAndSchedule()
    {
        CharacterData[] candidates = CharacterManager.Instance.CharacterDataDictionary.Values
            .Where(x => !x.IsInternal && GroupChatSessions.CanJoin(x))
            .Take(3).ToArray();
        CharacterData a = candidates[0];
        CharacterData b = candidates[1];
        CharacterData c = candidates[2];

        // 并行、激进停止、主持人第二位；模型名给的在测试环境模型清单里都没有，应安全回退跟随全局
        GroupSchedule schedule = new(EGroupScheduleMode.Parallel, EGroupStopPolicy.Aggressive, 1);
        GroupCreateWindowModel model = new(true, null, [a, b, c], "复盘群", ["model-x", null, "model-y"], schedule);

        Assert.Equal([a, b, c], model.Picked);
        Assert.Equal("复盘群", model.Name);
        Assert.Same(b, model.SelectedHost?.Data); //主持人预选第二位
        Assert.True(model.Schedule.IsParallel);
        Assert.True(model.Schedule.IsAggressive);
        Assert.Equal([null, null, null], model.Picker.PickedModelNames); //清单里没有 → 跟随全局（不炸）
        Assert.True(model.CanCreate);

        // 确认请求原样带出：调度与主持人按下标记
        GroupCreateRequest request = new(model.Name, [..model.Picked], [..model.Picker.PickedModelNames], model.PickedSchedule);
        Assert.Equal(1, request.Schedule.HostIndex);
        Assert.Equal(EGroupScheduleMode.Parallel, request.Schedule.Mode);
        Assert.Equal(EGroupStopPolicy.Aggressive, request.Schedule.StopPolicy);
    }
}
