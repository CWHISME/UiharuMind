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

    private static CharacterData Default(DefaultCharacter character) =>
        DefaultCharacterManager.Instance.GetCharacterData(character);

    /// <summary>带用户卡注入的普通角色。测装配规则，不依赖某张具体内置卡</summary>
    private static CharacterData ChatCharacterWithUserCard() => new()
    {
        IsAgent = false,
        InjectUserCard = true,
        CharacterName = "测试角色",
        Template = "【角色自身】开头的一段设定",
    };

    [Fact]
    public void ToolCharacter_GetsOnlyItsOwnTemplate()
    {
        CharacterData translator = Default(DefaultCharacter.TranslationPrompt);

        Assert.False(translator.IsAgent); //ADR 0043 之后它是普通角色，不再单列「工具人」档
        Assert.False(translator.InjectUserCard);

        string prompt = CharacterPromptBuilder.Build(translator);

        // 关键回归点：旧实现给所有非工具角色无条件注入用户卡。
        // 现在注入与否由 InjectUserCard 决定，工具人不该被灌进用户人格。
        Assert.DoesNotContain("的个人信息", prompt);
        Assert.Contains("资深跨文化翻译家", prompt);
    }

    [Fact]
    public void ChatCharacter_ComposesOwnTemplateThenUserCard()
    {
        CharacterData character = ChatCharacterWithUserCard();

        Assert.True(character.IsChat());
        Assert.True(character.InjectUserCard);

        string prompt = CharacterPromptBuilder.Build(character);

        int ownTemplate = prompt.IndexOf("【角色自身】", StringComparison.Ordinal);
        int userCard = prompt.IndexOf("的个人信息", StringComparison.Ordinal);

        Assert.True(ownTemplate >= 0, "缺少角色自身 Template");
        Assert.True(userCard >= 0, "缺少用户卡注入");
        Assert.True(ownTemplate < userCard, "用户卡应拼在角色自身 Template 之后");
    }

    [Fact]
    public void UserCardInjection_ResolvesUserNameNotHostName()
    {
        CharacterData character = ChatCharacterWithUserCard();
        string userName = CharacterManager.Instance.UserCharacterName;

        string prompt = CharacterPromptBuilder.Build(character);

        // 用户卡模板用的是 {{$user}}；若误用 {{$char}} 会被替换成宿主角色名，那是错的
        Assert.Contains($"{userName}的个人信息", prompt);
    }

    [Fact]
    public void Build_DoesNotMutateCallerArguments()
    {
        CharacterData translator = Default(DefaultCharacter.TranslationPrompt);
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
        CharacterData character = ChatCharacterWithUserCard();

        string prompt = CharacterPromptBuilder.Build(character);

        Assert.DoesNotContain("{{$char}}", prompt);
        Assert.DoesNotContain("{{$user}}", prompt);
        Assert.DoesNotContain("{{$lang}}", prompt);
    }
}
