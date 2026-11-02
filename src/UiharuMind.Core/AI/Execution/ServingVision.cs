using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 「此刻发这一发的模型看不看得见图」的<b>唯一判据</b>。
///
/// 装配读到的模型（<see cref="Assembly.AgentBuildProfile.ResolveCurrentModel"/>：钉选 ?? 全局有效）
/// 不等于服务器上真正接这一发的那个：钉选的本地模型没在跑时请求会回落全局（见
/// <see cref="LazyChatClient.ServingModel"/>）。两边一旦岔开，发送时降级与界面提示就会同时说错
/// ——「界面说能看图、其实看不见」，400 照旧。
///
/// 因此下面这三处都<b>按每次请求现读</b>，不许固化在装配期（以往还漏过识图投影那一层：
/// 它在最里层，现造的字节不会再经过外层的降级）：
/// <list type="bullet">
/// <item>普通对话形态的装配快照里根本不含这项（<see cref="Assembly.AgentAssemblyFacts"/> 对非 agent 一律归零），
/// 换模型也不触发重建——钉死它就会两头错：该降的不降、不该降的降了。</item>
/// <item>钉选回落本身随时会变（模型被卸载、全局换人），装配期读到的答案一分钟后就过期。</item>
/// </list>
/// 用它的地方：发送前的内联图片降级（<see cref="History.InlineImageDegrading"/>）、
/// 识图投影的附图开关（<see cref="ImagePreviewProjectingChatClient"/>）、
/// 交接后的占用估算（<c>TurnDriver.EstimateOccupancyAfterCompaction</c>）。
///
/// 全局那一半取 <see cref="LlmManager.EffectiveGlobalModel"/>（与装配侧同口径，含"还没选过时
/// 首个请求会自动挑的那个"）。发送侧 <see cref="LazyChatClient"/> 取的是
/// <see cref="LlmManager.CurrentRunningModel"/>，两者只在<b>一个都没在跑</b>时不同，
/// 而那时请求本来就发不出去——所以这两处不能互相替换。
/// </summary>
public static class ServingVision
{
    /// <summary>
    /// 会发这一发的模型自带视觉
    /// </summary>
    /// <param name="pinned">会话钉选解析出的模型；null 表示跟随全局</param>
    /// <returns>自带视觉则为 true（一个模型都指望不上时为 false）</returns>
    public static bool ServesImages(ModelRunningData? pinned) =>
        LazyChatClient.ServingModel(pinned, LlmManager.Instance.EffectiveGlobalModel)?.IsVisionModel == true;
}
