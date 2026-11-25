/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>派活时的模型决定</summary>
/// <param name="Name">钉在子会话上的模型名；跟随派活者/全局时为 null</param>
/// <param name="Notice">要随回执交代给派活方的那句话；无需交代时为空串</param>
internal readonly record struct ModelChoice(string? Name, string Notice);

/// <summary>
/// 一个模型名能不能钉到子会话上。生产实现是「模型库里查得到 + 跑得起来」，
/// 抽出来是为了让<b>挑选规则本身</b>可测——真去钉一个模型要拉起运行时。
/// </summary>
internal interface ISubAgentModelPinProbe
{
    /// <summary>模型库里有这个名字吗。只用于把「没在运行」与「没这个模型」分开说</summary>
    bool Knows(string name);

    /// <summary>钉得住就返回规范化后的名字，否则 null</summary>
    string? Pin(string? name);
}

/// <summary>
/// 生产用的判据：查得到、且它<b>跑得起来</b>才算。
///
/// 判据是「远程」而不是「此刻已就绪」：远程模型可以在子会话第一次请求时被拉起并等就绪
/// （<c>LazyChatClient</c> 的 <c>ResolveAsync</c> 会先 <c>EnsureModelStarted</c> 再等），
/// 所以冷着也钉得住。这里曾经要求 <c>ChatClient != null</c>，于是配置了探索模型却没提前跑过的
/// 场合一律被静默丢掉、回落到全局模型——表现就是「只读子代理用了通用子代理的模型」
/// （两者恰好同值时更看不出来）。本地模型没起来才是真的钉不得：一次只能跑一个，
/// <c>EnsureModelStarted</c> 对它直接 return，钉上去就是惰性客户端死等再报 "Model is not running"。
/// </summary>
internal sealed class LlmModelPinProbe : ISubAgentModelPinProbe
{
    /// <summary>单例：无状态，判据全在读 LlmManager</summary>
    public static LlmModelPinProbe Instance { get; } = new();

    public bool Knows(string name) => LlmManager.Instance.CacheModelDictionary.ContainsKey(name);

    public string? Pin(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!LlmManager.Instance.CacheModelDictionary.TryGetValue(name, out ModelRunningData? found)) return null;

        ModelRunningData? candidate = found;
        // 顺带把远程模型拉起来：钉完到第一轮请求之间多半就能就绪，省掉惰性客户端那段等待
        if (!LlmManager.Instance.TryCheckModelRunning(false, ref candidate)) return null;
        return candidate?.ModelName;
    }
}

/// <summary>
/// 派活时挑哪个模型：<b>点名 &gt; 设置页那一档（多选时依次取）&gt; 派活者/全局</b>。
///
/// 优先级由 ADR 0031 定的：档位配置的语义是「默认用哪个」，派活时点名是「这一趟用哪个」，
/// 更具体的覆盖更笼统的。多选后多出来的只有「默认那一档怎么挑一条」——依次取，见
/// <see cref="SubAgentModelRotation"/>（ADR 0111）。
///
/// 点名但钉不住时<b>回落到档位默认</b>（不是直接跟随主代理），并把实情写进回执：
/// 那是派活方唯一能知道「我指定的模型没生效」的渠道——模型名解析从不抛异常，
/// 不说的话它会一直以为跑的是它点的那个。
///
/// 从 <c>SubAgentTool</c> 里搬出来是为了可测：挑选规则要能被单测直接驱动，
/// 而那里只能通过读全局配置与真模型库来间接触发。
/// </summary>
internal static class SubAgentModelChooser
{
    /// <summary>
    /// 这一趟该用哪个模型。
    /// </summary>
    /// <param name="profile">子代理档</param>
    /// <param name="requested">派活方这一趟点名的模型；空白视为没点名</param>
    /// <param name="config">全局 agent 设置</param>
    /// <param name="probe">钉得住的判据</param>
    /// <returns>钉在子会话上的模型名，以及要随回执交代的那句话</returns>
    public static ModelChoice Pick(SubAgentProfile profile, string? requested, AgentSettingConfig config,
        ISubAgentModelPinProbe probe)
    {
        string? asked = Normalize(requested);
        if (asked == null) return new ModelChoice(ConfiguredOrNext(profile, config, probe), string.Empty);

        // 点名的压过设置页那一档：那两项的语义是"默认用哪个"，而这一趟点名是"这一趟用哪个"。
        // 钉得住就不碰游标——点名的这一趟不该把下一趟的轮次带跑
        if (probe.Pin(asked) is { } pinned) return new ModelChoice(pinned, string.Empty);

        string reason = probe.Knows(asked) ? $"模型 '{asked}' 没有在运行" : $"没有名为 '{asked}' 的模型";
        string? fallback = ConfiguredOrNext(profile, config, probe);
        // 落到哪儿就说到哪儿:默认模型也没得用时要讲明白这次跟随的是主代理的模型，
        // 否则「改用默认模型」这句话是假的，而界面只有一个模型名，看不出区别
        string target = fallback != null
            ? $"所以这次运行改用设置里这一档的默认模型 '{fallback}'。"
            : "设置里这一档也没有可用的模型，所以这次运行跟随你的模型。";
        return new ModelChoice(fallback, $"注：{reason}，{target}");
    }

    /// <summary>
    /// 设置页配的那一档，<b>多选时依次取一条</b>；一条都钉不住时留日志再回落。
    ///
    /// 回落本身是必要的（本地模型没起来就是钉不得），但<b>必须留痕</b>：配置了探索模型却跑在
    /// 主代理模型上，界面上只有一个模型名，翻会话元数据才看得出差别，没有日志就等于没发生。
    /// 被跳过的名字按 <c>Debug</c> 逐条记——Debug 是因为「配了三个死名字」会每趟刷一遍。
    /// </summary>
    /// <param name="profile">子代理档</param>
    /// <param name="config">全局 agent 设置</param>
    /// <param name="probe">钉得住的判据</param>
    /// <returns>钉得住的模型名；未配置或一条都钉不住时为 null（跟随派活者/全局）</returns>
    private static string? ConfiguredOrNext(SubAgentProfile profile, AgentSettingConfig config,
        ISubAgentModelPinProbe probe)
    {
        IReadOnlyList<string> configured = profile.ResolveModelNames(config);
        string? pinned = SubAgentModelRotation.Next(profile.Type, configured, name =>
        {
            string? usable = probe.Pin(name);
            if (usable == null) Log.Debug($"Sub-agent model '{name}' ({profile.Type}) cannot be pinned; skipped.");
            return usable;
        });

        if (pinned == null && configured.Count > 0)
        {
            Log.Warning($"Sub-agent models [{string.Join(", ", configured)}] ({profile.Type}) cannot be pinned; "
                        + "the sub-session follows the parent/global model.");
        }

        return pinned;
    }

    /// <summary>模型名按 <c>ModelName</c> 全局唯一 key 认，只去掉空白与换行；空白视为未指定</summary>
    private static string? Normalize(string? model)
    {
        string? trimmed = model?.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
