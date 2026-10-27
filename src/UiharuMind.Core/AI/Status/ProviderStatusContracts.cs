using System.Security.Cryptography;
using System.Text;

namespace UiharuMind.Core.AI.Status;

/// <summary>账户级用量窗口的档位</summary>
public enum EProviderMeterSlot
{
    FiveHour,
    Weekly,
    Monthly
}

/// <summary>提示项的语义（文案由界面层按此拼，Core 不出中文）</summary>
public enum EProviderNoticeKind
{
    /// <summary>当前处于高峰计价</summary>
    PeakPricing,

    /// <summary>当前模型不计费</summary>
    FreeModel,

    /// <summary>促销倍率（含加倍额度、折扣）</summary>
    PromoMultiplier,

    /// <summary>某窗口接近上限</summary>
    WindowNearLimit,

    /// <summary>某窗口已打满</summary>
    WindowExhausted,

    /// <summary>凭据失效（key 过期 / 被吊销 / 无权限）</summary>
    CredentialInvalid
}

/// <summary>
/// 一次取数需要的全部输入。
///
/// <b>是 class 且重写 <see cref="ToString"/>，不是 record</b>：record 自动生成的
/// <c>ToString()</c> 会把明文 <see cref="ApiKey"/> 打进任何字符串插值、异常消息与日志里，
/// 而「密钥不进日志」正是这个类唯一要守的东西。
/// </summary>
public sealed class ProviderStatusContext
{
    /// <summary>供应商配置类型（注册表键）</summary>
    public Type ConfigType { get; }

    /// <summary>状态接口基址（默认取模型端点 authority，由调用方推导）</summary>
    public string ApiBaseUrl { get; }

    /// <summary>明文密钥，仅在本进程内传递；不进日志、不进缓存键</summary>
    public string ApiKey { get; }

    /// <summary>发给接口的模型标识（模型级规则用）</summary>
    public string ModelId { get; }

    /// <summary>界面显示名</summary>
    public string ModelName { get; }

    /// <summary>
    /// 建一份取数输入
    /// </summary>
    /// <param name="configType">供应商配置类型</param>
    /// <param name="apiBaseUrl">状态接口基址</param>
    /// <param name="apiKey">明文密钥</param>
    /// <param name="modelId">模型标识</param>
    /// <param name="modelName">界面显示名</param>
    public ProviderStatusContext(Type configType, string apiBaseUrl, string apiKey, string modelId, string modelName)
    {
        ConfigType = configType;
        ApiBaseUrl = apiBaseUrl;
        ApiKey = apiKey;
        ModelId = modelId;
        ModelName = modelName;
    }

    /// <summary>不含密钥的描述（日志、异常、调试器里都只用它）</summary>
    public override string ToString() => $"{ConfigType.Name} @ {ApiBaseUrl} · {ModelId}";

    /// <summary>
    /// 从模型端点地址推出状态接口基址（协议 + 主机 + 端口），失败返回空串
    /// </summary>
    /// <param name="modelPath">模型端点地址</param>
    /// <returns>基址；解析不出来时为空串</returns>
    public static string DeriveBaseUrl(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath)) return string.Empty;
        return Uri.TryCreate(modelPath, UriKind.Absolute, out Uri? uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : string.Empty;
    }
}

/// <summary>一个账户级用量窗口</summary>
/// <param name="Slot">窗口档位</param>
/// <param name="Used">已用</param>
/// <param name="Cap">上限</param>
/// <param name="Unit">单位（"$"、"次" 等）</param>
/// <param name="ResetAt">窗口重置时刻；接口没给则为 null</param>
/// <param name="Exceeded">接口明说已打满</param>
public sealed record ProviderUsageMeter(
    EProviderMeterSlot Slot,
    double Used,
    double Cap,
    string Unit,
    DateTimeOffset? ResetAt,
    bool Exceeded)
{
    /// <summary>已用占上限的比例（0..1，上限未知时算 0）</summary>
    public double Percent => Cap > 0 ? Math.Clamp(Used / Cap, 0, 1) : 0;

    /// <summary>是否算打满：接口明说，或数值已到顶</summary>
    public bool IsExhausted => Exceeded || (Cap > 0 && Used >= Cap);
}

/// <summary>余额与计划（都可能为空：免费档没有计划名、预付费没有余额概念）</summary>
/// <param name="Remaining">剩余额度</param>
/// <param name="Unit">单位</param>
/// <param name="PlanName">计划名</param>
public sealed record ProviderBalance(double? Remaining, string Unit, string? PlanName);

/// <summary>
/// 提示项：只带语义与数值，文案由界面层组装。
/// 例：<c>Kind=PeakPricing, Multiplier=2, Until=本段结束时刻</c>
/// </summary>
/// <param name="Kind">语义</param>
/// <param name="Multiplier">倍率（计价/额度类提示用，其余为 1）</param>
/// <param name="Until">该状态持续到何时（倒计时用）</param>
/// <param name="Detail">补充说明（原始错误码之类的短串，不承载文案）</param>
public sealed record ProviderStatusNotice(
    EProviderNoticeKind Kind,
    double Multiplier = 1,
    DateTimeOffset? Until = null,
    string? Detail = null);

/// <summary>
/// 一次取数的结果：<b>只含账户级</b>信息。模型级结论（高峰、免费、促销）由
/// <see cref="IModelBillingRule"/> 按当前时刻现算，不进这份快照——
/// 否则按账户缓存的快照会把上一个模型的高峰徽章带给下一个模型。
/// </summary>
/// <param name="ProviderName">供应商显示名</param>
/// <param name="Meters">各窗口用量</param>
/// <param name="Balance">余额与计划</param>
/// <param name="AccountNotices">账户级提示（凭据失效等）</param>
/// <param name="FetchedAt">取到这份数据的时刻（本地时钟）</param>
public sealed record ProviderStatusSnapshot(
    string ProviderName,
    IReadOnlyList<ProviderUsageMeter> Meters,
    ProviderBalance? Balance,
    IReadOnlyList<ProviderStatusNotice> AccountNotices,
    DateTimeOffset FetchedAt);

/// <summary>
/// 缓存键：额度是账户级的，同一把 key 下切模型共用一份。
/// <see cref="KeyHash"/> 是密钥的 SHA256，明文不进字典键。
/// </summary>
/// <param name="ConfigType">供应商配置类型</param>
/// <param name="ApiBaseUrl">状态接口基址</param>
/// <param name="KeyHash">密钥散列</param>
internal readonly record struct ProviderStatusCacheKey(Type ConfigType, string ApiBaseUrl, string KeyHash)
{
    /// <summary>
    /// 算密钥散列（缓存键用；明文不参与比较）
    /// </summary>
    /// <param name="apiKey">明文密钥</param>
    /// <returns>十六进制散列；空密钥返回空串</returns>
    public static string HashKey(string? apiKey) =>
        string.IsNullOrEmpty(apiKey)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
