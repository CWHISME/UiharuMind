using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Status;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 顶栏那条供应商读数：把 Core 的结构化结果翻成文案、配色与倒计时。
///
/// 分工：Core 只给「用量 / 余额 / 提示项」，中文句子在这里拼（<see cref="Loc"/>）——
/// Core 感知语言就没法在无头测试里断言数据了；<b>倒计时必须在这里算</b>，成品字符串没法逐秒重算。
///
/// 两个容易写错的地方（ADR 0094）：
/// ① <b>可见性只压网络，不压首次取数</b>（<see cref="ShouldPoll"/>）。若拿「控件自己可不可见」
///    当唯一闸门，首帧没有缓存就是死锁：隐藏 → 不取数 → 永远没有数据 → 永远隐藏。
/// ② 切模型时旧请求的结果要作废（<see cref="_sequence"/>），否则甲家 12 秒后才回来的数
///    会盖在乙家的读数上。
/// </summary>
public sealed partial class ProviderStatusViewData : ObservableObject, IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JustNowAfter = TimeSpan.FromSeconds(3);

    private readonly Func<ProviderStatusContext?> _contextProvider; //当前模型 → 取数输入；无状态可查时给 null
    private readonly ProviderStatusService _status;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Action> _post; //回界面线程；测试注入同步执行
    private readonly CancellationTokenSource _cts = new();

    private ProviderStatusContext? _context; //当前目标（Tick 每次都刷）
    private ProviderStatusSnapshot? _snapshot; //账户级数据的最后一次成功结果
    private bool _visible; //控件报上来的可见性
    private bool _fetching;
    private bool _everSucceeded;
    private bool _disposed;
    private int _sequence; //取数序号：切目标后旧结果作废

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _readout = "";
    [ObservableProperty] private string _dotTag = "Ready";
    [ObservableProperty] private bool _hasDot;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasBadge;
    [ObservableProperty] private string _badgeText = "";

    [ObservableProperty] private string _panelTitle = "";
    [ObservableProperty] private bool _hasMeters;
    [ObservableProperty] private IReadOnlyList<ProviderStatusMeterRow> _meterRows = [];
    [ObservableProperty] private bool _hasBalance;
    [ObservableProperty] private string _balanceText = "";
    [ObservableProperty] private bool _hasCredentialWarning;
    [ObservableProperty] private string _credentialHint = "";
    [ObservableProperty] private string _dataAgeText = "";

    /// <summary>
    /// 造一份读数
    /// </summary>
    /// <param name="contextProvider">当前模型 → 取数输入</param>
    /// <param name="status">取数服务</param>
    /// <param name="now">时钟（测试用）</param>
    /// <param name="post">回界面线程的方式（测试用：直接执行）</param>
    public ProviderStatusViewData(Func<ProviderStatusContext?> contextProvider, ProviderStatusService status,
        Func<DateTimeOffset>? now = null, Action<Action>? post = null)
    {
        _contextProvider = contextProvider;
        _status = status;
        _now = now ?? (() => DateTimeOffset.Now);
        _post = post ?? (action =>
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        });
        // 定时拍归公共调度器管：多个界面挂着同一家供应商时，不会各问一遍
        ProviderStatusHub.Shared.Subscribe(this);
    }

    /// <summary>
    /// 控件告诉这边「我现在看不看得见」：只影响要不要联网，不影响首次取数
    /// </summary>
    /// <param name="visible">是否可见</param>
    public void SetVisible(bool visible) => _visible = visible;

    /// <summary>
    /// 一拍（30 秒，由 <see cref="ProviderStatusHub"/> 统一驱动）：重算倒计时与「是否仍处高峰」，
    /// 必要时按 TTL 去取一次。同一把 key 的多份读数共用服务层的缓存，HTTP 只发生一次
    /// </summary>
    public void Pump()
    {
        if (_disposed) return;
        ProviderStatusContext? context = _contextProvider();
        if (context == null)
        {
            Clear();
            return;
        }

        bool switched = _context == null || !SameTarget(_context, context);
        _context = context;

        if (ProviderStatusRegistry.For(context.ConfigType)?.Source == null)
        {
            //这家没有状态可查：整块不显示，也不该留着上一家的数字
            Clear();
            return;
        }

        if (switched) _snapshot = _status.Peek(context); //切回来先顶上缓存，不用空一秒
        Apply(context);

        if (ShouldPoll) _ = RefreshAsync();
    }

    /// <summary>
    /// 取一次（已缓存且未过期时直接命中）
    /// </summary>
    /// <param name="force">跳过 TTL</param>
    public async Task RefreshAsync(bool force = false)
    {
        if (_disposed) return;
        ProviderStatusContext? context = _contextProvider();
        if (context == null)
        {
            Clear();
            return;
        }

        _context = context;
        if (_fetching) return; //一拍撞一次手动刷新时不叠加请求

        _fetching = true;
        if (force) BeginRefresh(); //点开明细：先说「更新中」，别把上一轮的秒数闪给用户看
        int sequence = ++_sequence;
        try
        {
            ProviderStatusSnapshot? snapshot = await _status.GetAsync(context, force, _cts.Token)
                .ConfigureAwait(false);
            if (snapshot == null)
            {
                // 什么都没有就别停在「更新中…」
                _post(() => IsRefreshing = false);
                return;
            }

            _post(() =>
            {
                if (sequence != _sequence) return; //期间切了模型，旧结果作废
                _snapshot = snapshot;
                _everSucceeded = true;
                IsRefreshing = false;
                Apply(context);
            });
        }
        catch (OperationCanceledException)
        {
            //退出时取消，不是错误
        }
        finally
        {
            _fetching = false;
        }
    }

    /// <summary>一轮对话结束：额度可能变了，按 TTL 补刷一次（不会连击）</summary>
    public void NotifyTurnEnded() => _ = RefreshAsync();

    /// <summary>停下在途请求并从公共调度器摘掉（窗口关了、页面没了）</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ProviderStatusHub.Shared.Unsubscribe(this);
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>拍一次就联网的条件：可见，或者还从没成功过（否则首帧没数据就永远不出）</summary>
    private bool ShouldPoll => _visible || !_everSucceeded;

    private void Apply(ProviderStatusContext context)
    {
        ProviderStatusRegistration? registration = ProviderStatusRegistry.For(context.ConfigType);
        ProviderStatusSnapshot? snapshot = _snapshot;
        if (registration?.Source == null || snapshot == null)
        {
            Clear();
            return;
        }

        DateTimeOffset now = _now();
        bool credentialInvalid = snapshot.AccountNotices.Any(n => n.Kind == EProviderNoticeKind.CredentialInvalid);
        ProviderBalance? balance = snapshot.Balance;

        List<ProviderStatusNotice> notices = [.. snapshot.AccountNotices];
        if (registration.Rule?.Describe(context, now) is { Count: > 0 } modelNotices) notices.AddRange(modelNotices);
        notices.AddRange(ProviderStatusRules.MeterNotices(snapshot.Meters));

        IsEmpty = false;
        HasDot = false;
        DotTag = "Ready";
        if (credentialInvalid || notices.Any(n => n.Kind is EProviderNoticeKind.WindowExhausted))
        {
            HasDot = true;
            DotTag = "Error";
        }
        else if (notices.Any(n => n.Kind is EProviderNoticeKind.WindowNearLimit))
        {
            HasDot = true;
            DotTag = "Warning";
        }

        HasBalance = balance?.Remaining != null;
        BalanceText = HasBalance
            ? Loc.Text(LangKey.ProviderStatusBalance, balance!.Unit, FormatAmount(balance.Remaining!.Value))
            : "";

        // 没有滚动窗口的账户（免费档，接口给 windowLimits.fiveHour=null）读数退到余额：
        // 否则顶栏一片空白，而用户其实是「余额 0，发不出去」，正好缺的就是这句
        string metersReadout = credentialInvalid
            ? Loc.Text(LangKey.ProviderStatusCredentialInvalid)
            : ComposeReadout(snapshot.Meters, now);
        Readout = metersReadout.Length == 0 ? BalanceText : metersReadout;

        //徽章一次只挂一个：高峰 > 促销 > 免费（前两个都在说「小心」，免费是中性信息）
        ProviderStatusNotice? badge =
            notices.FirstOrDefault(n => n.Kind == EProviderNoticeKind.PeakPricing)
            ?? notices.FirstOrDefault(n => n.Kind == EProviderNoticeKind.PromoMultiplier)
            ?? notices.FirstOrDefault(n => n.Kind == EProviderNoticeKind.FreeModel);
        HasBadge = badge != null;
        BadgeText = badge == null ? "" : ComposeBadge(badge, now);

        PanelTitle = string.IsNullOrWhiteSpace(balance?.PlanName)
            ? snapshot.ProviderName
            : $"{snapshot.ProviderName} · {balance!.PlanName}";

        IReadOnlyList<ProviderStatusMeterRow> rows = BuildMeterRows(snapshot.Meters, now);
        if (!MeterRows.SequenceEqual(rows)) MeterRows = rows;
        HasMeters = rows.Count > 0;

        HasCredentialWarning = credentialInvalid;
        CredentialHint = credentialInvalid ? Loc.Text(LangKey.ProviderStatusCredentialHint) : "";
        DataAgeText = ComposeDataAge(now - snapshot.FetchedAt);
    }

    private void Clear()
    {
        _snapshot = null;
        _everSucceeded = false;
        IsRefreshing = false;
        IsEmpty = true;
        HasDot = false;
        HasBadge = false;
        Readout = "";
        BadgeText = "";
        PanelTitle = "";
        HasMeters = false;
        HasBalance = false;
        BalanceText = "";
        HasCredentialWarning = false;
        CredentialHint = "";
        DataAgeText = "";
        if (MeterRows.Count > 0) MeterRows = [];
    }

    private string ComposeReadout(IReadOnlyList<ProviderUsageMeter> meters, DateTimeOffset now)
    {
        if (meters.Count == 0) return "";

        //打满时不说「还剩多少」，直接说什么时候能再用——那才是用户此刻唯一关心的
        if (ProviderStatusRules.Tightest(meters) is { IsExhausted: true } tight)
        {
            string head = $"{SlotLabel(tight.Slot)} 100%";
            return tight.ResetAt is { } resetAt
                ? $"{head} · {Loc.Text(LangKey.ProviderStatusResetsIn, ShortSpan(resetAt - now))}"
                : head;
        }

        return string.Join(" · ", meters.Select(meter => $"{SlotLabel(meter.Slot)} {PercentText(meter.Percent)}"));
    }

    private IReadOnlyList<ProviderStatusMeterRow> BuildMeterRows(IReadOnlyList<ProviderUsageMeter> meters,
        DateTimeOffset now)
    {
        if (meters.Count == 0) return [];

        List<ProviderStatusMeterRow> rows = [];
        foreach (ProviderUsageMeter meter in meters)
        {
            string resetText = meter.ResetAt is { } resetAt
                ? Loc.Text(LangKey.ProviderStatusResetsIn, ShortSpan(resetAt - now))
                : "";
            rows.Add(new ProviderStatusMeterRow(SlotLabel(meter.Slot), PercentText(meter.Percent), meter.Percent,
                resetText, meter.IsExhausted));
        }

        return rows;
    }

    private string ComposeBadge(ProviderStatusNotice notice, DateTimeOffset now) => notice.Kind switch
    {
        EProviderNoticeKind.PeakPricing => Loc.Text(LangKey.ProviderStatusPeakBadge,
            FormatMultiplier(notice.Multiplier),
            notice.Until is { } until ? ShortSpan(until - now) : ""),
        EProviderNoticeKind.PromoMultiplier => Loc.Text(LangKey.ProviderStatusPromoBadge,
            FormatMultiplier(notice.Multiplier)),
        EProviderNoticeKind.FreeModel => Loc.Text(LangKey.ProviderStatusFreeModel),
        _ => ""
    };

    /// <summary>
    /// 数据年龄的文案。<b>按秒走</b>：面板开着的时候每秒重算一次，
    /// 「47 秒前」得自己往上爬，而不是 30 秒跳一格。
    /// </summary>
    private string ComposeDataAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        // 刚取到的那几秒说「刚刚」而不是「0秒前」——数字从 0 开始爬看着像倒计时
        if (age < JustNowAfter) return Loc.Text(LangKey.ProviderStatusDataJustNow);
        string span = age.TotalMinutes < 1
            ? Loc.Text(LangKey.ProviderStatusAgeSeconds, (int)age.TotalSeconds)
            : age.TotalHours < 1
                ? Loc.Text(LangKey.ProviderStatusAgeMinutes, (int)age.TotalMinutes, age.Seconds)
                : Loc.Text(LangKey.ProviderStatusAgeHours, (int)age.TotalHours, age.Minutes);
        return age > StaleAfter
            ? Loc.Text(LangKey.ProviderStatusDataStale, span)
            : Loc.Text(LangKey.ProviderStatusDataAge, span);
    }

    /// <summary>
    /// 进入「更新中」状态：面板里的数据年龄先说「更新中…」，等新数据到了再显示新的年龄。
    /// <b>不做这一步就会闪一下旧秒数</b>（47 秒前 → 立刻变成 0 秒前，看着像倒计时在倒着跑）。
    /// </summary>
    public void BeginRefresh()
    {
        if (_disposed) return;
        IsRefreshing = true;
        DataAgeText = Loc.Text(LangKey.ProviderStatusRefreshing);
    }

    /// <summary>
    /// 只重算「随时间走」的那几处文案（数据年龄、面板里的重置倒计时），不碰网络。
    /// 面板开着时由控件按秒调它——公共调度器那 30 秒一拍是给取数用的，撑不起秒级跳动
    /// </summary>
    public void RefreshAge()
    {
        if (_disposed || _snapshot == null || IsRefreshing) return; //刷新中别把「更新中」顶掉
        DateTimeOffset now = _now();
        DataAgeText = ComposeDataAge(now - _snapshot.FetchedAt);
        IReadOnlyList<ProviderStatusMeterRow> rows = BuildMeterRows(_snapshot.Meters, now);
        if (!MeterRows.SequenceEqual(rows)) MeterRows = rows;
    }

    private static string SlotLabel(EProviderMeterSlot slot) => slot switch
    {
        EProviderMeterSlot.FiveHour => Loc.Text(LangKey.ProviderStatusSlotFiveHour),
        EProviderMeterSlot.Weekly => Loc.Text(LangKey.ProviderStatusSlotWeekly),
        _ => Loc.Text(LangKey.ProviderStatusSlotMonthly)
    };

    //百分比自己拼：走 ":P0" 会跟着文化变成 "32 %"（带空格）或换符号，读数要的是稳定的 "32%"
    private static string PercentText(double ratio) => $"{(int)Math.Round(ratio * 100)}%";

    private static string FormatMultiplier(double multiplier) =>
        Math.Abs(multiplier - Math.Round(multiplier)) < 0.01
            ? ((int)Math.Round(multiplier)).ToString()
            : multiplier.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatAmount(double amount) => amount.ToString("0.##",
        System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>倒计时用的短跨度（"12分" / "2时13分" / "2天4时"），单位词在资源里</summary>
    private static string ShortSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return Loc.Text(LangKey.ProviderStatusSpanSeconds, (int)span.TotalSeconds);
        if (span.TotalHours < 1) return Loc.Text(LangKey.ProviderStatusSpanMinutes, (int)span.TotalMinutes);
        if (span.TotalDays < 1) return Loc.Text(LangKey.ProviderStatusSpanHours, (int)span.TotalHours, span.Minutes);
        return Loc.Text(LangKey.ProviderStatusSpanDays, (int)span.TotalDays, span.Hours);
    }

    private static bool SameTarget(ProviderStatusContext left, ProviderStatusContext right) =>
        left.ConfigType == right.ConfigType &&
        left.ApiBaseUrl == right.ApiBaseUrl &&
        string.Equals(left.ModelId, right.ModelId, StringComparison.Ordinal);
}

/// <summary>悬停面板里的一行窗口读数</summary>
/// <param name="Label">窗口名</param>
/// <param name="PercentText">"32%"</param>
/// <param name="Ratio">0..1，画进度条用</param>
/// <param name="ResetText">多久后重置；不知道时为空</param>
/// <param name="IsExhausted">是否已打满</param>
public sealed record ProviderStatusMeterRow(string Label, string PercentText, double Ratio, string ResetText,
    bool IsExhausted)
{
    /// <summary>有没有重置时刻可显示</summary>
    public bool HasReset => ResetText.Length > 0;
}

/// <summary>
/// 当前模型 → 取数输入。取不到（本地模型、没填 key、端点不像 URL）就返回 null，
/// 上层据此整块不显示。
/// </summary>
internal static class ProviderStatusContextFactory
{
    /// <summary>
    /// 按当前模型造一份取数输入
    /// </summary>
    /// <param name="model">当前模型</param>
    /// <returns>取数输入；不是远程模型或缺 key 时为 null</returns>
    public static ProviderStatusContext? TryCreate(ModelRunningData? model)
    {
        if (model?.ModelInfo is not RemoteModelInfo remote) return null;

        BaseRemoteModelConfig config = remote.Config;
        string apiKey = remote.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        string baseUrl = ProviderStatusContext.DeriveBaseUrl(config.ModelPath);
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        return new ProviderStatusContext(EffectiveConfigType(config), baseUrl, apiKey, config.ModelId,
            config.ModelName);
    }

    /// <summary>
    /// 存档读回来的 <c>Config</c> 可能落在 <see cref="BaseRemoteModelConfig"/> 本身
    /// （实测：反序列化只看属性声明类型）。<c>RemoteModelManager</c> 启动时会按 <c>ConfigType</c>
    /// 再修一次，但不是所有路径都过那里；注册表按基类名查不到就会整块不显示，所以这里再兜一层。
    /// </summary>
    private static Type EffectiveConfigType(BaseRemoteModelConfig config)
    {
        Type runtime = config.GetType();
        if (runtime != typeof(BaseRemoteModelConfig) && runtime != typeof(RemoteModelConfig)) return runtime;
        return RemoteModelConfigRegistry.ResolveConfigType(config.ConfigType) ?? runtime;
    }
}
