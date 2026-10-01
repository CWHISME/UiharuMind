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
    /// 卡所在子目录必须与它的身份一致：<c>Tools/</c> 是内部技能角色、<c>Characters/</c> 是普通角色，
    /// 各作品目录一律是智能体。目录只是分类提示，身份以卡上的 IsAgent / IsInternal 为准——
    /// 这条钉住两者不许打架。
    ///
    /// 「一个目录一种语义」是这条不变量的用意，所以作品目录<b>显式列出来</b>：
    /// 列错一个名字（打错字、改名）当场炸，好过它悄悄长出一张错位的卡。
    /// <b>新增作品目录要在这里加一行。</b>
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

            string relative = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length); // "Toaru.AcceleratorAgent"
            int dot = relative.IndexOf('.');
            string folder = relative.Substring(0, dot);
            string id = relative.Substring(dot + 1);
            CharacterData data = DefaultCharacterManager.Instance.All[id];

            switch (folder)
            {
                case "Agents": //非作品的自有角色与委派身份载体
                case "Toaru":
                case "DeathNote":
                case "DrStone":
                case "CodeGeass":
                case "SteinsGate":
                    Assert.True(data.IsAgent, $"{id} 在 {folder}/ 下却不是智能体");
                    break;
                case "Tools":
                    Assert.True(data.IsInternal && !data.IsAgent, $"{id} 在 Tools/ 下却不是内部技能角色");
                    break;
                case "Characters":
                    Assert.False(data.IsAgent || data.IsInternal, $"{id} 在 Characters/ 下却是智能体或内部角色");
                    break;
                default:
                    Assert.Fail($"未知的卡片目录:{folder}(新增作品目录要先加进这条测试的目录清单)");
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
    [InlineData("LawlietAgent")]
    [InlineData("HououinKyoumaAgent")]
    [InlineData("MisakaMikotoAgent")]
    [InlineData("KinuhataSaiaiAgent")]
    [InlineData("TsuchimikadoMotoharuAgent")]
    [InlineData("SatenRuikoAgent")]
    [InlineData("IndexAgent")]
    [InlineData("KamijouToumaAgent")]
    [InlineData("LastOrderAgent")]
    [InlineData("Misaka10032Agent")]
    [InlineData("MakiseKurisuAgent")]
    [InlineData("CcAgent")]
    [InlineData("RationalAgent")]
    public void CardWithAnchor_CodaEqualsAnchor(string id)
    {
        CharacterData data = DefaultCharacterManager.Instance.All[id];

        Assert.False(string.IsNullOrWhiteSpace(data.PersonaAnchor), $"{id} 的 anchor 丢了");
        Assert.Equal(data.PersonaAnchor.Trim(), data.GetPersonaCoda());
    }

    /// <summary>
    /// 生图默认只给 OP-01：每次出图都可能花钱，其余内置卡与新建角色一律关，要用的在编辑页自己开
    /// </summary>
    [Fact]
    public void ImageGeneration_IsOnlyOnForRationalAgentByDefault()
    {
        string[] enabled = DefaultCharacterManager.Instance.All.Values
            .Where(data => data.Tools.EnableImageGeneration)
            .Select(data => data.CharacterId)
            .ToArray();

        Assert.Equal(["RationalAgent"], enabled);
        Assert.False(new AgentToolConfig().EnableImageGeneration);
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

    /// <summary>
    /// 智能体卡的人格正文不许假设「有别人在场」（人格稿 v8 §7.1）：同一份人格单聊群聊共用，
    /// 单聊里压根没有「别人」。实测白井黑子单聊开场把「不替人核数字」「别人刚说过的结论」
    /// 这类分工话原样念给了用户。群聊专属的说法归场景段。
    /// </summary>
    [Fact]
    public void AgentCardPersona_DoesNotAssumeOthersPresent()
    {
        string[] groupPhrases =
        [
            "别人的活", "别人的事", "别人盯着", "另有人干", "那两位", "你们", "有人说", "一行人",
            "大家说", "大家查", "几个人的话", "资历最老", "别人讲", "别人给出", "别人刚说", "别人已经说", "别人从仓库",
        ];
        List<string> hits = [];
        foreach (CharacterData data in DefaultCharacterManager.Instance.All.Values)
        {
            if (!data.IsAgent || data.IsInternal) continue;
            string template = data.Config.PromptConfig.Template;
            hits.AddRange(groupPhrases.Where(template.Contains).Select(p => $"{data.CharacterId}:「{p}」"));
        }

        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }
}
