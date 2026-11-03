using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 「此刻真正发这一发的模型」及其派生事实（看不看得见图、上下文窗口多大）的<b>唯一判据</b>。
///
/// 装配读到的模型（<see cref="Assembly.AgentBuildProfile.ResolveCurrentModel"/>：钉选 ?? 全局有效）
/// 不等于服务器上真正接这一发的那个：钉选的本地模型没在跑时请求会回落全局（见
/// <see cref="LazyChatClient.ServingModel"/>）。两边一旦岔开，凡是照钉选名算出来的东西都会各说一套，
/// 而<b>同一个会话里只该有一个窗口、一种能力</b>。这条已经踩过两次，都是真金白银：
/// <list type="bullet">
/// <item>发送时的附图与降级：视觉→盲切换后图片照旧重发，严格的服务端整轮 400。</item>
/// <item>压缩预算：按钉选名的窗口算额度，而请求发给另一个窗口大得多的模型——轻会话每轮被过度折叠、
/// 前缀缓存每轮断一次；工具重的会话额度算成 0，窗口机制整个关掉，历史涨到真的超窗。</item>
/// </list>
///
/// 因此这些都<b>按每次使用现读</b>，不许固化在装配期：
/// <list type="bullet">
/// <item>普通对话形态的装配快照里根本不含视觉一项（<see cref="Assembly.AgentAssemblyFacts"/> 对非 agent 一律归零），
/// 换模型也不触发重建——钉死它就会两头错：该降的不降、不该降的降了。</item>
/// <item>钉选回落本身随时会变（模型被卸载、全局换人），装配期读到的答案一分钟后就过期。</item>
/// <item>本地模型的 <c>ContextLength</c> 取运行期实际加载值，没加载时落到 8192——按钉选名算预算，
/// 等于按一个根本不发请求的窗口在算。</item>
/// </list>
///
/// 全局那一半取 <see cref="LlmManager.EffectiveGlobalModel"/>（与装配侧同口径，含"还没选过时
/// 首个请求会自动挑的那个"）。发送侧 <see cref="LazyChatClient"/> 取的是
/// <see cref="LlmManager.CurrentRunningModel"/>，两者只在<b>一个都没在跑</b>时不同，
/// 而那时请求本来就发不出去——所以这两处不能互相替换。
/// </summary>
public static class ServingModelFacts
{
    /// <summary>
    /// 会发这一发的模型自带视觉（全局那一半现读单例）
    /// </summary>
    /// <param name="pinned">会话钉选解析出的模型；null 表示跟随全局</param>
    /// <returns>自带视觉则为 true（一个模型都指望不上时为 false）</returns>
    public static bool SeesImages(ModelRunningData? pinned) =>
        SeesImages(pinned, LlmManager.Instance.EffectiveGlobalModel);

    /// <summary>
    /// 会发这一发的模型自带视觉
    /// </summary>
    /// <param name="pinned">会话钉选解析出的模型</param>
    /// <param name="global">全局当前模型；显式传是为了可单测</param>
    /// <returns>自带视觉则为 true</returns>
    public static bool SeesImages(ModelRunningData? pinned, ModelRunningData? global) =>
        LazyChatClient.ServingModel(pinned, global)?.IsVisionModel == true;

    /// <summary>
    /// 会发这一发的模型的上下文窗口（全局那一半现读单例）
    /// </summary>
    /// <param name="pinned">会话钉选解析出的模型；null 表示跟随全局</param>
    /// <returns>窗口大小 token 数；一个模型都指望不上时为 0（表示未知，压缩据此不压）</returns>
    public static int ContextLength(ModelRunningData? pinned) =>
        ContextLength(pinned, LlmManager.Instance.EffectiveGlobalModel);

    /// <summary>
    /// 会发这一发的模型的上下文窗口
    /// </summary>
    /// <param name="pinned">会话钉选解析出的模型</param>
    /// <param name="global">全局当前模型；显式传是为了可单测</param>
    /// <returns>窗口大小 token 数；一个都指望不上时为 0</returns>
    public static int ContextLength(ModelRunningData? pinned, ModelRunningData? global) =>
        LazyChatClient.ServingModel(pinned, global)?.ContextLength ?? 0;
}
