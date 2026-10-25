using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// chatsession.list-characters 的可见性口径：内部角色（工具卡）不列、屏蔽角色按
/// <see cref="CharacterVisibility.ShowShielded"/> 解锁闸门走——与 UI 选择器/建群共用
/// <see cref="CharacterVisibility.IsVisibleCandidate"/>，工具侧不得自己另写一套。
/// 曾经在没解锁时硬滤 IsShielded，解锁后屏蔽的内置智能体仍不出现，这条钉住修复。
/// </summary>
public sealed class ChatSessionCharactersTests : IDisposable
{
    private static readonly string[] CardIds = ["cc-test-visible", "cc-test-internal", "cc-test-shielded"];

    public ChatSessionCharactersTests()
    {
        CharacterManager.Instance.CharacterDataDictionary[CardIds[0]] = new CharacterData
        {
            CharacterId = CardIds[0], CharacterName = "可见角色", Description = "普通智能体", IsAgent = true,
        };
        CharacterManager.Instance.CharacterDataDictionary[CardIds[1]] = new CharacterData
        {
            CharacterId = CardIds[1], CharacterName = "工具卡", Description = "内部技能角色", IsInternal = true,
        };
        CharacterManager.Instance.CharacterDataDictionary[CardIds[2]] = new CharacterData
        {
            CharacterId = CardIds[2], CharacterName = "屏蔽角色", Description = "解锁后才可见", IsShielded = true, IsAgent = true,
        };
    }

    public void Dispose()
    {
        foreach (string card in CardIds) CharacterManager.Instance.CharacterDataDictionary.Remove(card);
        CharacterVisibility.SetShowShielded(false); //锁回：别把解锁状态泄漏给进程内其它测试
    }

    private static async Task<string> ListCharactersAsync()
    {
        AIFunction tool = (AIFunction)ChatSessionTools.Create("cc-parent")
            .First(x => x.Name == ChatSessionTools.ListCharactersToolName);
        return (await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken))?.ToString() ?? string.Empty;
    }

    [Fact]
    public async Task ListsVisibleCharacters_ButNotInternalOrShielded_ByDefault()
    {
        string text = await ListCharactersAsync();

        Assert.Contains("可见角色", text);
        Assert.DoesNotContain("工具卡", text);
        Assert.DoesNotContain("屏蔽角色", text);
    }

    [Fact]
    public async Task IncludesShieldedCharacters_AfterUnlock()
    {
        CharacterVisibility.SetShowShielded(true);

        string text = await ListCharactersAsync();

        Assert.Contains("可见角色", text);
        Assert.DoesNotContain("工具卡", text); //解锁只放屏蔽卡，不放内部工具卡
        Assert.Contains("屏蔽角色", text);
    }
}
