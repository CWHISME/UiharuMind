using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 用量与消费统计页的状态宿主。
///
/// <para>
/// <b>页面实例常驻</b>（<c>MainViewModel.GetPage</c> 带缓存），所以筛选状态就挂在实例上：
/// 用户切去别的页再回来，时间范围 / 模型 / 会话原样在。重数据（明细、排行、图表 series）
/// 在 <see cref="OnDisable"/> 释放、<see cref="OnEnable"/> 重查——既压住内存，也保证回来时是新鲜的。
/// </para>
/// <para>
/// 查询全部下沉到 <see cref="IUsageStore"/>（SQL），这里只做「取 → 格式化 → 交给绑定」；
/// 每次重查带 <c>_generation</c>，晚到的旧结果直接丢。
/// </para>
/// </summary>
public partial class UsagePageData : PageDataBase
{
    private const int PageSize = 50;

    /// <summary>清理按钮保留的天数（跟着按钮走；将来要做设置项就把它挪进配置）</summary>
    private const int RetentionDays = 180;

    /// <summary>排行取前几名，其余并成「其他」</summary>
    private const int RankTopCount = 8;

    /// <summary>占比饼图取前几块，碎成一片读不了</summary>
    private const int ShareTopCount = 5;

    private readonly IUsageStore _store;
    private readonly FilesService? _files;
    private readonly IMessageService? _messages;

    private int _generation; //每次重来都 +1，用来丢弃晚到的旧结果
    private int _pageIndex;
    private int _totalRows;
    private bool _suppressReload; //程序改选项时为真（避免重建选项时触发连锁重查）
    private bool _hasAnyKnown; //全表查过「至少有一条记录」：单调事实，查到 true 后不再查
    private UsageTotals _totals = UsageTotals.Empty;
    private IReadOnlyList<UsageTrendPoint> _trendPoints = [];
    private IReadOnlyList<UsageTimelinePoint> _timelinePoints = [];
    private EUsageDetailSortColumn _sortColumn = EUsageDetailSortColumn.Time;
    private EUsageSortDirection _sortDirection = EUsageSortDirection.Descending;

    /// <summary>KPI 六卡</summary>
    public ObservableCollection<UsageKpiCardViewData> Kpis { get; } = new();

    /// <summary>
    /// 趋势图/占比图的数据换过一轮（换范围、换指标、刷新都会走到）。
    /// 视图收到后要补一次「等画布出帧再测量」，否则 LiveCharts 会把这次重建吞掉——
    /// 它只在画布出过新帧之后才允许测量，而静止的图表不会再出帧。见 <see cref="UsagePage"/>
    /// </summary>
    public event Action? ChartsRebuilt;

    /// <summary>排行（Top N + 其他）</summary>
    public ObservableCollection<UsageRankItemViewData> RankItems { get; } = new();

    /// <summary>失败原因分布</summary>
    public ObservableCollection<UsageFailureItemViewData> Failures { get; } = new();

    /// <summary>明细表当前页</summary>
    public ObservableCollection<UsageDetailItemViewData> Details { get; } = new();

    /// <summary>模型下拉</summary>
    public ObservableCollection<UsageOptionViewData> ModelOptions { get; } = new();

    /// <summary>会话下拉</summary>
    public ObservableCollection<UsageOptionViewData> SessionOptions { get; } = new();

    /// <summary>粒度下拉</summary>
    public ObservableCollection<UsageOptionViewData> GranularityOptions { get; } = new();

    /// <summary>趋势指标下拉</summary>
    public ObservableCollection<UsageOptionViewData> TrendMetricOptions { get; } = new();

    /// <summary>时间范围下拉</summary>
    public ObservableCollection<UsageOptionViewData> RangeOptions { get; } = new();

    /// <summary>排行维度下拉</summary>
    public ObservableCollection<UsageOptionViewData> RankDimensionOptions { get; } = new();

    /// <summary>排行指标下拉</summary>
    public ObservableCollection<UsageOptionViewData> RankMetricOptions { get; } = new();

    /// <summary>趋势图 series（code-only 构造，XAML 只绑定）</summary>
    [ObservableProperty] private ISeries[] _trendSeries = [];

    /// <summary>趋势图 X 轴</summary>
    [ObservableProperty] private Axis[] _trendXAxes = [];

    /// <summary>趋势图 Y 轴（默认双轴）</summary>
    [ObservableProperty] private Axis[] _trendYAxes = [];

    /// <summary>占比图 series</summary>
    [ObservableProperty] private ISeries[] _shareSeries = [];

    /// <summary>时间范围预设</summary>
    [ObservableProperty] private UsageOptionViewData? _rangeOption;

    /// <summary>自定义范围的起点（含当天）</summary>
    [ObservableProperty] private DateTime? _customFrom;

    /// <summary>自定义范围的终点（含当天）</summary>
    [ObservableProperty] private DateTime? _customTo;

    /// <summary>粒度</summary>
    [ObservableProperty] private UsageOptionViewData? _granularityOption;

    /// <summary>模型筛选</summary>
    [ObservableProperty] private UsageOptionViewData? _modelOption;

    /// <summary>会话筛选；选中后趋势图切命中率时间线</summary>
    [ObservableProperty] private UsageOptionViewData? _sessionOption;

    /// <summary>趋势指标</summary>
    [ObservableProperty] private UsageOptionViewData? _trendMetricOption;

    /// <summary>排行维度</summary>
    [ObservableProperty] private UsageOptionViewData? _rankDimensionOption;

    /// <summary>排行指标</summary>
    [ObservableProperty] private UsageOptionViewData? _rankMetricOption;

    /// <summary>只看失败</summary>
    [ObservableProperty] private bool _onlyFailed;

    /// <summary>整页重查中（首次加载 / 筛选变化）</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>只翻页/换排序中（不闪整页）</summary>
    [ObservableProperty] private bool _isLoadingDetails;

    /// <summary>库里从来没有任何记录（决定空态文案与「去对话」按钮）</summary>
    [ObservableProperty] private bool _hasAnyRecords;

    /// <summary>当前筛选下没有数据</summary>
    [ObservableProperty] private bool _isEmpty;

    /// <summary>空态标题</summary>
    [ObservableProperty] private string _emptyTitle = "";

    /// <summary>空态说明</summary>
    [ObservableProperty] private string _emptyHint = "";

    /// <summary>空态是否给「去对话」按钮</summary>
    [ObservableProperty] private bool _showGoToChat;

    /// <summary>趋势图的空态</summary>
    [ObservableProperty] private bool _isEmptyTrend;

    /// <summary>趋势图上方的一行提示（降粒度、骤降点提示等）</summary>
    [ObservableProperty] private string _trendHint = "";

    /// <summary>有没有提示要显示（绑 IsVisible 用，避免字符串转 bool）</summary>
    [ObservableProperty] private bool _hasTrendHint;

    /// <summary>当前是会话视图（趋势图切命中率时间线）</summary>
    [ObservableProperty] private bool _isSessionView;

    /// <summary>会话视图的摘要</summary>
    [ObservableProperty] private UsageSessionSummaryViewData? _sessionSummary;

    /// <summary>当前筛选下所有请求都没上报缓存写入量（界面降级成说明条）</summary>
    [ObservableProperty] private bool _showCacheWriteNote;

    /// <summary>分页信息（「第 3 / 12 页 · 共 586 行」）</summary>
    [ObservableProperty] private string _pageInfo = "";

    /// <summary>还能上一页</summary>
    [ObservableProperty] private bool _canGoPrev;

    /// <summary>还能下一页</summary>
    [ObservableProperty] private bool _canGoNext;

    /// <summary>时间列表头（带排序箭头）</summary>
    [ObservableProperty] private string _timeHeader = "";

    /// <summary>模型列表头</summary>
    [ObservableProperty] private string _modelHeader = "";

    /// <summary>输入列表头</summary>
    [ObservableProperty] private string _inputHeader = "";

    /// <summary>输出列表头</summary>
    [ObservableProperty] private string _outputHeader = "";

    /// <summary>缓存列表头</summary>
    [ObservableProperty] private string _cacheHeader = "";

    /// <summary>延迟列表头</summary>
    [ObservableProperty] private string _latencyHeader = "";

    /// <summary>费用列表头</summary>
    [ObservableProperty] private string _costHeader = "";

    /// <summary>框架按类型建页面时走这里（无参构造转调可注入的那个）</summary>
    public UsagePageData() : this(App.Services.GetRequiredService<IMessageService>(), UsageJournal.Instance,
        App.FilesService)
    {
    }

    /// <summary>
    /// 建一份用量页状态
    /// </summary>
    /// <param name="messages">消息服务（导出结果通知）；null 表示不提示</param>
    /// <param name="store">用量库；测试可换成临时库</param>
    /// <param name="files">保存文件对话框；null 表示导出不可用（设计器/测试）</param>
    public UsagePageData(IMessageService? messages, IUsageStore store, FilesService? files)
    {
        _messages = messages;
        _store = store;
        _files = files;
        InitializeOptions();
        UpdateSortHeaders();
    }

    /// <inheritdoc />
    public override void OnEnable()
    {
        base.OnEnable();
        if (Application.Current is { } app) app.ActualThemeVariantChanged += OnThemeVariantChanged;
        //停留期间不自动重查：整页刷新会重建图表、重置下拉，打断正在翻页/看图的人；
        //要看最新数据手动点刷新，或切走再切回来（这里会查一次）
        _ = RefreshAsync();
    }

    /// <inheritdoc />
    public override void OnDisable()
    {
        base.OnDisable();
        if (Application.Current is { } app) app.ActualThemeVariantChanged -= OnThemeVariantChanged;
        ReleaseData();
    }

    /// <summary>
    /// 主题切换：图表颜色是构造时写进 series/axis 的（不用 LiveCharts 自带主题），
    /// 不重建就会停在上一个主题
    /// </summary>
    private void OnThemeVariantChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RebuildTrend();
        RebuildRank();
    });

    /// <summary>
    /// 释放重数据。<b>筛选状态留在实例上</b>——这是「切走再回来筛选还在」的实现方式
    /// </summary>
    private void ReleaseData()
    {
        _generation++; //在途查询的结果回来时直接丢
        Details.Clear();
        RankItems.Clear();
        Failures.Clear();
        Kpis.Clear();
        _trendPoints = [];
        _timelinePoints = [];
        _totals = UsageTotals.Empty;
        TrendSeries = [];
        ShareSeries = [];
        TrendXAxes = [];
        TrendYAxes = [];
        IsLoading = false;
        IsLoadingDetails = false;
        PageInfo = "";
        CanGoPrev = false;
        CanGoNext = false;
        SessionSummary = null;
    }

    /// <summary>
    /// 手工刷新（工具栏按钮）
    /// </summary>
    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    /// <summary>
    /// 清理旧记录：只由用户点，<b>不做自动保留策略</b>——用量数据不可再生，删不删由人决定。
    /// 范围筛选只管查得到什么，不减少磁盘：行数越多文件越大，这里才是真的减
    /// </summary>
    [RelayCommand]
    private async Task CleanupOldRecords()
    {
        DateTime cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
        if (_messages != null &&
            !await _messages.ConfirmAsync(
                Loc.Text(LangKey.UsageCleanupConfirm, cutoff.ToString("yyyy-MM-dd"), RetentionDays),
                Loc.Text(LangKey.UsageCleanup)))
            return;

        try
        {
            long cutoffTs = new DateTimeOffset(cutoff).ToUnixTimeMilliseconds();
            int rows = await Task.Run(() => _store.DeleteOlderThan(cutoffTs));
            _messages?.ShowNotification(Loc.Text(LangKey.UsageCleanupDone, rows),
                severity: MessageSeverity.Success);
            //删完重查：KPI 与会话下拉都是按当前范围算的，旧行没了就不该再出现在汇总里
            await RefreshAsync();
        }
        catch (Exception e)
        {
            _messages?.ShowNotification(Loc.Text(LangKey.UsageCleanupFailed, e.Message),
                severity: MessageSeverity.Error);
        }
    }

    /// <summary>上一页</summary>
    [RelayCommand]
    private async Task PrevPage()
    {
        if (_pageIndex <= 0) return;
        _pageIndex--;
        await RefreshDetailsAsync();
    }

    /// <summary>下一页</summary>
    [RelayCommand]
    private async Task NextPage()
    {
        if (_pageIndex + 1 >= PageCount) return;
        _pageIndex++;
        await RefreshDetailsAsync();
    }

    /// <summary>按表头排序（点同一列切方向）</summary>
    /// <param name="column">列名（与 <see cref="EUsageDetailSortColumn"/> 同名）</param>
    [RelayCommand]
    private async Task SortBy(string? column)
    {
        if (!Enum.TryParse(column, true, out EUsageDetailSortColumn parsed)) return;
        if (_sortColumn == parsed)
        {
            _sortDirection = _sortDirection == EUsageSortDirection.Descending
                ? EUsageSortDirection.Ascending
                : EUsageSortDirection.Descending;
        }
        else
        {
            _sortColumn = parsed;
            //时间默认倒序（新的在前），数值默认正序（小的在前）——两种排序的直觉方向不同
            _sortDirection = parsed == EUsageDetailSortColumn.Time
                ? EUsageSortDirection.Descending
                : EUsageSortDirection.Ascending;
        }

        _pageIndex = 0;
        UpdateSortHeaders();
        await RefreshDetailsAsync();
    }

    /// <summary>清掉会话筛选，回到全局视图</summary>
    [RelayCommand]
    private async Task ClearSession()
    {
        SelectOption(SessionOptions, null, option => SessionOption = option);
        await RefreshAsync();
    }

    /// <summary>空态里的「去对话」</summary>
    [RelayCommand]
    private void GoToChat() => App.JumpToPage(MenuPages.MenuConversationKey);

    /// <summary>
    /// 明细行点会话：钻取到该会话的命中率时间线（本页内完成，不进对话页）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    [RelayCommand]
    private async Task DrillSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        UsageOptionViewData? option = SessionOptions.FirstOrDefault(x =>
            string.Equals(x.Value, sessionId, StringComparison.Ordinal));
        if (option == null)
        {
            option = new UsageOptionViewData(sessionId, UsageFormat.ShortSession(sessionId));
            SessionOptions.Insert(1, option);
        }

        SessionOption = option;
        await RefreshAsync();
    }

    /// <summary>
    /// 导出当前筛选的完整结果集（不是当前页）
    /// </summary>
    [RelayCommand]
    private async Task Export()
    {
        if (_files == null)
        {
            NotifyWarning(Loc.Text(LangKey.UsageDetailExportNoStore));
            return;
        }

        try
        {
            string name = $"usage-{DateTime.Now:yyyy-MM-dd}.csv";
            Uri? target = await _files.SaveFileAsync(UIManager.GetFocusWindow(), name);
            if (target == null) return;

            UsageDetailQuery query = BuildDetailQuery(int.MaxValue, 0);
            long rows = await Task.Run(() => _store.ExportCsv(query, target.LocalPath));
            _messages?.ShowNotification(Loc.Text(LangKey.UsageDetailExportDone, rows, target.LocalPath),
                severity: MessageSeverity.Success);
        }
        catch (Exception e)
        {
            NotifyWarning(Loc.Text(LangKey.UsageDetailExportFailed, e.Message));
        }
    }

    private void NotifyWarning(string message) =>
        _messages?.ShowNotification(message, severity: MessageSeverity.Warning);

    private int PageCount => Math.Max(1, (_totalRows + PageSize - 1) / PageSize);

    partial void OnOnlyFailedChanged(bool value)
    {
        _pageIndex = 0;
        if (!_suppressReload) _ = RefreshAsync();
    }

    partial void OnModelOptionChanged(UsageOptionViewData? value)
    {
        _pageIndex = 0;
        if (!_suppressReload) _ = RefreshAsync();
    }

    partial void OnSessionOptionChanged(UsageOptionViewData? value)
    {
        _pageIndex = 0;
        if (!_suppressReload) _ = RefreshAsync();
    }

    partial void OnRangeOptionChanged(UsageOptionViewData? value)
    {
        _pageIndex = 0;
        OnPropertyChanged(nameof(IsCustomRange));
        if (!_suppressReload) _ = RefreshAsync();
    }

    partial void OnGranularityOptionChanged(UsageOptionViewData? value)
    {
        if (!_suppressReload) _ = RefreshAsync();
    }

    partial void OnTrendMetricOptionChanged(UsageOptionViewData? value) => RebuildTrend();

    partial void OnRankDimensionOptionChanged(UsageOptionViewData? value) => RebuildRank();

    partial void OnRankMetricOptionChanged(UsageOptionViewData? value) => RebuildRank();

    partial void OnCustomFromChanged(DateTime? value)
    {
        if (!_suppressReload) _ = RefreshAsync();
    }

    partial void OnCustomToChanged(DateTime? value)
    {
        if (!_suppressReload) _ = RefreshAsync();
    }

    /// <summary>时间范围是否「自定义」（决定日期选择器显不显示）</summary>
    public bool IsCustomRange => (RangeKindOf(RangeOption)) == EUsageRangeKind.Custom;

    /// <inheritdoc />
    protected override Control CreateView => new UsagePage();

    partial void OnTrendHintChanged(string value) => HasTrendHint = value.Length > 0;
}
