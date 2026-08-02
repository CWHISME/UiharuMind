using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 钉死系统提示词的装配规则。此前 ChatSession.BuildRequestMessagesAsync 与
/// CharacterConfig.ToAgent 各有一套装配逻辑，同一角色在聊天页与在技能里看到的系统提示并不相同；
/// 现在两条路都走 CharacterPromptBuilder，规则只有一条。
/// </summary>
public class CharacterPromptBuilderTests
{
    public CharacterPromptBuilderTests()
    {
        // 内置角色由嵌入资源装载；CharacterManager 未初始化时，
        // GetCharacterData 会按 CharacterId(= 枚举名) 回退到这里，因此挂载引用可以解析
        DefaultCharacterManager.Instance.OnInitialize();
    }

    private static CharacterData Default(DefaultCharacter character)
    {
        return DefaultCharacterManager.Instance.GetCharacterData(character);
    }

    [Fact]
    public void PurePromptCharacter_GetsOnlyItsOwnTemplate()
    {
        CharacterData translator = Default(DefaultCharacter.Translator);

        Assert.True(translator.IsPurePromptCharacter);

        string prompt = CharacterPromptBuilder.Build(translator);

        // 关键回归点：旧实现给所有非工具角色无条件注入用户卡。
        // 现在注入与否由挂载列表决定，翻译类角色不该被灌进用户人格。
        Assert.DoesNotContain("的个人信息", prompt);
        Assert.Contains("资深跨文化翻译家", prompt);
    }

    [Fact]
    public void RoleplayCharacter_ComposesMountsThenOwnTemplate()
    {
        CharacterData uiharu = Default(DefaultCharacter.UiharuKazari);

        // 默认挂载值已改为空列表，扮演角色必须在自己的存档里显式声明挂载
        Assert.Equal(
            [nameof(DefaultCharacter.Roleplay_ThirdPerson), nameof(DefaultCharacter.UserCard)],
            uiharu.MountPrompts);
        Assert.False(uiharu.IsPurePromptCharacter);

        string prompt = CharacterPromptBuilder.Build(uiharu);

        int scaffold = prompt.IndexOf("第三人称角色扮演系统", StringComparison.Ordinal);
        int userCard = prompt.IndexOf("的个人信息", StringComparison.Ordinal);
        int ownTemplate = prompt.IndexOf("初春饰利是《魔法禁书目录》", StringComparison.Ordinal);

        Assert.True(scaffold >= 0, "缺少第三人称扮演脚手架");
        Assert.True(userCard >= 0, "缺少用户卡注入");
        Assert.True(ownTemplate >= 0, "缺少角色自身的 Template");

        // 顺序：挂载片段按声明顺序在前，角色自身 Template 在后
        Assert.True(scaffold < userCard, "挂载片段未按声明顺序拼接");
        Assert.True(userCard < ownTemplate, "角色自身 Template 应拼在挂载片段之后");
    }

    [Fact]
    public void UserCardMount_ResolvesUserNameNotHostName()
    {
        CharacterData uiharu = Default(DefaultCharacter.UiharuKazari);
        string userName = CharacterManager.Instance.UserCharacterName;

        string prompt = CharacterPromptBuilder.Build(uiharu);

        // 用户卡降级为普通挂载项后，其模板改用 {{$user}}；
        // 若误用 {{$char}} 会被替换成宿主角色名(初春)，那是错的
        Assert.Contains($"{userName}的个人信息", prompt);
    }

    [Fact]
    public void Build_DoesNotMutateCallerArguments()
    {
        CharacterData translator = Default(DefaultCharacter.Translator);
        Dictionary<string, object?> custom = new() { ["foo"] = "bar" };

        CharacterPromptBuilder.Build(translator, custom);

        // 旧实现把 lang/char/user 直接补进调用方字典，
        // 而调用方是 ChatSession.CustomParams —— 这些参数会随会话被持久化下来
        Assert.Single(custom);
        Assert.Equal("bar", custom["foo"]);
    }

    [Fact]
    public void UnrenderedPlaceholders_AreResolved()
    {
        CharacterData uiharu = Default(DefaultCharacter.UiharuKazari);

        string prompt = CharacterPromptBuilder.Build(uiharu);

        Assert.DoesNotContain("{{$char}}", prompt);
        Assert.DoesNotContain("{{$user}}", prompt);
        Assert.DoesNotContain("{{$lang}}", prompt);
    }
}
