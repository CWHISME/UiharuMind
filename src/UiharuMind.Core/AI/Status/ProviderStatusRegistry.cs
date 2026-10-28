using UiharuMind.Core.AI.Status.Providers.CommandCode;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.AI.Status;

/// <summary>
/// 账户级取数（联网）。失败抛异常，由 <see cref="ProviderStatusService"/> 统一兜底；
/// 凭据失效这类「有结论的失败」不要抛，返回带
/// <see cref="EProviderNoticeKind.CredentialInvalid"/> 的降级快照。
/// </summary>
public interface IProviderStatusSource
{
    /// <summary>
    /// 取一次账户级状态
    /// </summary>
    /// <param name="context">取数输入</param>
    /// <param name="ct">取消</param>
    /// <returns>快照；整份读不出结构时返回 null</returns>
    Task<ProviderStatusSnapshot?> FetchAsync(ProviderStatusContext context, CancellationToken ct);

    /// <summary>
    /// 取一次本周期用量统计。<b>与 <see cref="FetchAsync"/> 分开</b>：它只给「本月花了多少、重置于哪天」，
    /// 变化慢、只有明细面板要用，所以由界面在「点开面板」时按需调，不进 30 秒的公共拍子。
    /// 不提供统计的供应商默认返回 null。
    /// </summary>
    /// <param name="context">取数输入</param>
    /// <param name="ct">取消</param>
    /// <returns>统计；这家没有这个接口时 null</returns>
    Task<ProviderUsageSummary?> FetchSummaryAsync(ProviderStatusContext context, CancellationToken ct) =>
        Task.FromResult<ProviderUsageSummary?>(null);
}

/// <summary>模型级计费规则（纯本地，按当前时刻算，零网络）</summary>
public interface IModelBillingRule
{
    /// <summary>
    /// 描述此刻这个模型的计费状态
    /// </summary>
    /// <param name="context">取数输入（只用模型标识）</param>
    /// <param name="now">当前时刻</param>
    /// <returns>提示项；没有可说的就返回空列表</returns>
    IReadOnlyList<ProviderStatusNotice> Describe(ProviderStatusContext context, DateTimeOffset now);
}

/// <summary>一家的注册项：两个能力都可选（有的只给价目表，有的只给接口）</summary>
/// <param name="Source">账户级取数；null 表示这家没有状态接口</param>
/// <param name="Rule">模型级计费规则；null 表示这家没有时段/促销概念</param>
public sealed record ProviderStatusRegistration(IProviderStatusSource? Source, IModelBillingRule? Rule);

/// <summary>
/// 注册表查表面。抽成接口只为让 <see cref="ProviderStatusService"/> 在单测里能换一份假实现——
/// 静态类没法替身，而「并发 5 次只发 1 次请求」这类用例必须能注入。
/// </summary>
public interface IProviderStatusRegistry
{
    /// <summary>
    /// 按供应商配置类型查注册项
    /// </summary>
    /// <param name="configType">配置类型</param>
    /// <returns>注册项；未登记返回 null</returns>
    ProviderStatusRegistration? For(Type configType);
}

/// <summary>
/// 供应商状态的显式登记表。<b>与 <c>RemoteModelConfigRegistry</c> 同构</b>：显式登记、
/// 不扫程序集、不反射。新增一家 = 新增实现类 + 这里一行，界面层零改动。
///
/// 注意方向：供应商差异只许出现在「该供应商自己的实现类」里；取数、缓存、退避、格式化
/// 这些共享链路一律与供应商无关，否则第二个供应商进来就得改共享链路，复用是假的。
/// </summary>
public static class ProviderStatusRegistry
{
    private static readonly Dictionary<Type, ProviderStatusRegistration> Entries = new()
    {
        [typeof(RemoteCommandCodeModelConfig)] =
            new(new CommandCodeStatusSource(), new CommandCodeBillingCalendar()),
    };

    /// <summary>查表用的那份注册表（进程内唯一）</summary>
    public static IProviderStatusRegistry Instance { get; } = new DictionaryRegistry(Entries);

    /// <summary>
    /// 按供应商配置类型查注册项
    /// </summary>
    /// <param name="configType">配置类型</param>
    /// <returns>注册项；未登记返回 null（顶栏据此整块不显示）</returns>
    public static ProviderStatusRegistration? For(Type configType) => Instance.For(configType);

    private sealed class DictionaryRegistry(Dictionary<Type, ProviderStatusRegistration> entries)
        : IProviderStatusRegistry
    {
        public ProviderStatusRegistration? For(Type configType) =>
            entries.TryGetValue(configType, out ProviderStatusRegistration? registration) ? registration : null;
    }
}
