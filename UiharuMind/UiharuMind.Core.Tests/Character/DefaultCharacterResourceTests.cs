/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 不变量：<b>每一个 <see cref="DefaultCharacter"/> 都有一份能读出来的内置角色卡</b>。
///
/// 加枚举项容易，往 csproj 里补 <c>EmbeddedResource</c> 容易忘——而漏了的表现是
/// <c>DefaultCharacterManager.LoadDefaultCharacters</c> 在<b>应用启动时</b>抛
/// <c>FileNotFoundException</c>。那是编译期看不见、单跑功能也测不到的一类崩溃，
/// 只能靠这条钉住。
/// </summary>
public class DefaultCharacterResourceTests
{
    public static TheoryData<DefaultCharacter> AllCharacters()
    {
        TheoryData<DefaultCharacter> data = new();
        for (int i = 0; i < (int)DefaultCharacter.Max; i++) data.Add((DefaultCharacter)i);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllCharacters))]
    public void EveryDefaultCharacter_HasAnEmbeddedCard(DefaultCharacter character)
    {
        CharacterData? data = EmbeddedResourcesUtils.ReadFromJson<CharacterData>(character + ".json");

        Assert.NotNull(data);
    }

    /// <summary>
    /// 匿名子代理的身份角色必须是<b>智能体档</b>且<b>内部角色</b>：
    /// 前者决定它走不走 harness 装配（走错就没有工具），
    /// 后者决定它不出现在角色库与选择器里（它不是给用户挑的）。
    ///
    /// 两档各一张，刻意不共用：探索档恒定只读、另配轻量模型，
    /// 顶同一个名字用户分不清这次委派能不能改东西。
    /// </summary>
    [Theory]
    [InlineData(DefaultCharacter.GeneralSubAgent)]
    [InlineData(DefaultCharacter.ExploreSubAgent)]
    public void SubAgentCard_IsInternalAgent(DefaultCharacter character)
    {
        CharacterData? data = EmbeddedResourcesUtils.ReadFromJson<CharacterData>(character + ".json");

        Assert.NotNull(data);
        Assert.Equal(ECharacterKind.Agent, data!.Kind);
        Assert.True(data.IsInternal);
    }

    /// <summary>
    /// 身份卡的提示词模板<b>必须保持为空</b>。
    ///
    /// 匿名子代理的系统提示由 <c>SubAgentAssembly.BuildSubAgentInstructions</c> 现拼，
    /// 装配时人格一律置空（通用子代理不是派活者的分身）——所以往这张卡里写模板是<b>无操作</b>，
    /// 写了也不会生效。那是最难查的一类缺陷：改了、存了、看起来该变了，而模型那边一个字没动。
    /// 这条测试的作用是让"填了没用"在改动那一刻就报出来。
    /// </summary>
    [Theory]
    [InlineData(DefaultCharacter.GeneralSubAgent)]
    [InlineData(DefaultCharacter.ExploreSubAgent)]
    public void SubAgentCard_TemplateStaysEmpty(DefaultCharacter character)
    {
        CharacterData? data = EmbeddedResourcesUtils.ReadFromJson<CharacterData>(character + ".json");

        Assert.NotNull(data);
        Assert.True(string.IsNullOrEmpty(data!.Config.PromptConfig.Template));
    }
}
