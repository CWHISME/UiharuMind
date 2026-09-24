using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 不变量：<b>每一个内置角色卡都有一份能读出来的嵌入资源</b>。
///
/// 内置卡的 id 全集 = 枚举成员 ∪ 扫描 <c>Resources/Cards/*.json</c> 得到的文件名。
/// 漏放文件会在 <c>DefaultCharacterManager.LoadAll</c> 于<b>应用启动时</b>抛
/// <c>FileNotFoundException</c>——编译期看不见、单跑功能也测不到，只能靠这条钉住。
/// </summary>
public class DefaultCharacterResourceTests
{
    public DefaultCharacterResourceTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
    }

    [Fact]
    public void EveryDefaultCharacter_HasALoadedCard()
    {
        foreach (DefaultCharacter value in Enum.GetValues<DefaultCharacter>())
        {
            if (value == DefaultCharacter.Max) continue;
            Assert.True(DefaultCharacterManager.Instance.All.ContainsKey(value.ToString()),
                $"{value} 没有装载到卡");
        }
    }

    /// <summary>
    /// 卡所在子目录必须与它的身份一致：Agents/ = 智能体，Tools/ = 内部技能角色，Characters/ = 普通角色。
    /// 目录只是分类提示，身份以卡上的 IsAgent / IsInternal 为准——这条钉住两者不许打架。
    /// </summary>
    [Fact]
    public void CardFolder_MatchesItsIdentity()
    {
        string prefix = $"{typeof(DefaultCharacterManager).Assembly.GetName().Name}.Resources.Cards.";
        const string suffix = ".json";
        foreach (string name in typeof(DefaultCharacterManager).Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            string relative = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length); // "Agents.ChenXiAgent"
            int dot = relative.IndexOf('.');
            string folder = relative.Substring(0, dot);
            string id = relative.Substring(dot + 1);
            CharacterData data = DefaultCharacterManager.Instance.All[id];

            switch (folder)
            {
                case "Agents":
                    Assert.True(data.IsAgent, $"{id} 在 Agents/ 下却不是智能体");
                    break;
                case "Tools":
                    Assert.True(data.IsInternal && !data.IsAgent, $"{id} 在 Tools/ 下却不是内部技能角色");
                    break;
                case "Characters":
                    Assert.False(data.IsAgent || data.IsInternal, $"{id} 在 Characters/ 下却是智能体或内部角色");
                    break;
                default:
                    Assert.Fail($"未知的卡片目录:{folder}");
                    break;
            }
        }
    }

    /// <summary>
    /// 匿名委派会话的身份载体必须是<b>智能体档</b>且<b>内部角色</b>：
    /// 前者决定它走不走 harness 装配（走错就没有工具），
    /// 后者决定它不出现在角色库与选择器里（它不是给用户挑的）。
    /// </summary>
    [Theory]
    [InlineData(DefaultCharacter.AnonymousAgent)]
    [InlineData(DefaultCharacter.LegacyExploreAgent)]
    public void AnonymousAgentCard_IsInternalAgent(DefaultCharacter character)
    {
        CharacterData data = DefaultCharacterManager.Instance.GetCharacterData(character);

        Assert.True(data.IsAgent);
        Assert.True(data.IsInternal);
    }

    /// <summary>
    /// 配了手写锚点的内置卡，coda 必须原样返回那句锚点而不是自动拼：
    /// JSON 键名写错会静默回退到自动拼，不加这条看不出来。
    /// </summary>
    [Theory]
    [InlineData("AcceleratorAgent")]
    [InlineData("ShokuhouMisakiAgent")]
    [InlineData("KongoMitsukoAgent")]
    [InlineData("UiharuKazariAgent")]
    [InlineData("ChenXiAgent")]
    [InlineData("BaiLuAgent")]
    [InlineData("ShiraiKurokoAgent")]
    [InlineData("SenkuAgent")]
    [InlineData("LelouchAgent")]
    [InlineData("YagamiLightAgent")]
    [InlineData("HououinKyoumaAgent")]
    public void CardWithAnchor_CodaEqualsAnchor(string id)
    {
        CharacterData data = DefaultCharacterManager.Instance.All[id];

        Assert.False(string.IsNullOrWhiteSpace(data.PersonaAnchor), $"{id} 的 anchor 丢了");
        Assert.Equal(data.PersonaAnchor.Trim(), data.GetPersonaCoda());
    }

    /// <summary>
    /// 身份卡的提示词模板<b>必须保持为空</b>。
    ///
    /// 匿名委派会话的系统提示由 <c>SubAgentAssembly.BuildSubAgentInstructions</c> 现拼，
    /// 往这张卡里写模板是<b>无操作</b>——写了也不生效。这条测试让「填了没用」在改动那一刻就报出来。
    /// </summary>
    [Theory]
    [InlineData(DefaultCharacter.AnonymousAgent)]
    [InlineData(DefaultCharacter.LegacyExploreAgent)]
    public void AnonymousAgentCard_TemplateStaysEmpty(DefaultCharacter character)
    {
        CharacterData data = DefaultCharacterManager.Instance.GetCharacterData(character);

        Assert.True(string.IsNullOrEmpty(data.Config.PromptConfig.Template));
    }
}
