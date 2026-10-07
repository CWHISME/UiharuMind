using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.WorldSettings;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Features.Characters;

namespace UiharuMind.App.Tests.Features.Characters;

/// <summary>
/// 钉住 CharacterDraft 新字段（世界书挂载 / 备选开场白 / 深度注入）与草稿本体的同步：
/// 挂载名必须即时反映到 Subject（提交时那份），空深度注入不落盘。
/// </summary>
public class CharacterDraftSyncTests
{
    private static CharacterDraft NewDraft() =>
        CharacterDraft.ForNew(new CharacterData { CharacterName = "测试角色" }, new RecordingMessageService());

    [Fact]
    public void WorldSettingName_SyncsToSubject()
    {
        CharacterDraft draft = NewDraft();
        Assert.False(draft.HasWorldSettingName);
        Assert.Equal("", draft.WorldSettingSummary);

        draft.WorldSettingName = "某世界书";

        Assert.Equal("某世界书", draft.Subject.WorldSettingName);
        Assert.True(draft.HasWorldSettingName);

        draft.WorldSettingName = "";
        Assert.False(draft.HasWorldSettingName);
    }

    [Fact]
    public void AlternateGreetings_SyncToSubject()
    {
        CharacterDraft draft = NewDraft();

        draft.AddAlternateGreetingCommand.Execute(null);
        draft.AlternateGreetings[0].Text = "备选一";

        Assert.Equal(["备选一"], draft.Subject.AlternateGreetings);

        draft.RemoveAlternateGreetingCommand.Execute(draft.AlternateGreetings[0]);

        Assert.Empty(draft.Subject.AlternateGreetings);
    }

    [Fact]
    public void EmptyDepthPrompt_NotPersisted()
    {
        CharacterDraft draft = NewDraft();

        // 只调角色不写字：不应凭空生成空对象
        draft.DepthPromptRoleIndex = 1;
        Assert.Null(draft.Subject.DepthPrompt);
        Assert.False(draft.HasDepthPrompt);

        draft.DepthPromptText = "记住：你在学园都市。";
        Assert.NotNull(draft.Subject.DepthPrompt);
        Assert.True(draft.HasDepthPrompt);

        // 清掉正文：回到「没有深度注入」
        draft.DepthPromptText = "";
        Assert.Null(draft.Subject.DepthPrompt);
        Assert.False(draft.HasDepthPrompt);
    }
}