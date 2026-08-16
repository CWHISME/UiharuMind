namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 「这个角色发图有没有退路」的<b>唯一判据</b>。
///
/// 装配侧据它决定挂不挂 <c>ask_vision</c>（模型自己看不了图时委托视觉模型看），
/// 界面侧据它决定要不要在附件盘上出警示。两边必须同源：一旦岔开，
/// 就会出现「气泡里显示着图片、模型其实只收到一行 <c>[Attached file: 路径]</c>」——界面在撒谎。
/// </summary>
public static class VisionFallback
{
    /// <summary>
    /// 角色能否在模型看不了图时兜住图片。<b>非 agent 档一律不装工具</b>（ADR 0003），
    /// 所以扮演、工具人、用户卡三档都没有退路，只有 agent 档且识图开关开着才有。
    /// </summary>
    /// <param name="kind">角色档位</param>
    /// <param name="tools">角色的能力配置</param>
    /// <returns>有退路则为 true</returns>
    public static bool HasFallback(ECharacterKind kind, AgentToolConfig tools) =>
        kind.IsAgent() && tools.EnableVisionTool;

    /// <summary>
    /// 角色能否兜住图片。角色为 null 时按<b>有退路</b>处理——此刻无从判断，
    /// 警示宁可不出，也不要在拿不准的空态上吓人。
    /// </summary>
    /// <param name="character">角色；为 null 时返回 true</param>
    /// <returns>有退路则为 true</returns>
    public static bool HasFallback(CharacterData? character) =>
        character == null || HasFallback(character.Kind, character.Tools);

    /// <summary>
    /// 本轮图片会不会白发：有图、生效模型自己看不了、角色又没有识图工具兜底。
    /// 三条同时成立时模型只会收到一行路径文本，图等于没发。
    /// </summary>
    /// <param name="hasImage">附件里有图片</param>
    /// <param name="modelSupportsVision">生效模型支持识图</param>
    /// <param name="hasFallback">角色有识图工具兜底</param>
    /// <returns>图片会被白发则为 true</returns>
    public static bool WillDropImages(bool hasImage, bool modelSupportsVision, bool hasFallback) =>
        hasImage && !modelSupportsVision && !hasFallback;
}
