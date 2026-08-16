using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 钉死 <see cref="CharacterData.CopyFrom"/> 的完整性。
///
/// 编辑页是「草稿改完往活实例上盖」，这个方法就是那一盖。它是逐字段写的，
/// 因此每加一个可持久化字段都得跟着加一行——漏了不会有任何编译错误，
/// 只会表现成「某一类改动点了保存也不生效」，最难查的那种。
/// 这里的判据是<b>整份序列化结果相等</b>，所以新字段自动纳入。
/// </summary>
public class CharacterDataCopyFromTests
{
    /// <summary>造一份所有字段都偏离默认值的角色，任何漏抄都会显形</summary>
    private static CharacterData FullyPopulated() => new()
    {
        CharacterId = "copy-from-source",
        Kind = ECharacterKind.Agent,
        MemoryName = "memory-a",
        IsDefaultCharacter = true,
        IsInternal = true,
        InjectUserCard = true,
        RequiresVisionModel = true,
        CharacterIcon = "aWNvbg==",
        FirstGreeting = "开场白",
        MountAgents = ["sub-a", "sub-b"],
        CharacterName = "源角色",
        Description = "源描述",
        Template = "源提示词",
        Tools = { EnableShellExecution = true, EnableWebSearch = true },
    };

    [Fact]
    public void CopyFrom_CarriesEveryPersistedField()
    {
        CharacterData source = FullyPopulated();
        CharacterData target = new();

        target.CopyFrom(source);

        Assert.Equal(SaveUtility.SaveToString(source), SaveUtility.SaveToString(target));
    }

    [Fact]
    public void CopyFrom_DoesNotShareSubObjectsWithSource()
    {
        CharacterData source = FullyPopulated();
        CharacterData target = new();
        target.CopyFrom(source);

        // 盖完之后两边必须各走各的:共享子对象会让"取消"变成假的
        // ——草稿上再改一个字，活实例会跟着变
        source.CharacterName = "改过的名字";
        source.Tools.EnableShellExecution = false;
        source.MountAgents.Add("sub-c");

        Assert.Equal("源角色", target.CharacterName);
        Assert.True(target.Tools.EnableShellExecution);
        Assert.Equal(2, target.MountAgents.Count);
    }

    [Fact]
    public void CopyFrom_KeepsTargetInstanceIdentity()
    {
        // 就地覆盖而非换实例:会话缓存着角色引用,换实例会让正在对话的一方继续用旧对象
        CharacterData target = new();
        CharacterConfig staleConfig = target.Config;

        target.CopyFrom(FullyPopulated());

        Assert.NotSame(staleConfig, target.Config);
        Assert.Equal("源角色", target.CharacterName);
    }
}
