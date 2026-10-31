using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ProviderPicker;

/// <summary>
/// models.dev 供应商选择器的数据。<b>单选、选完即回调</b>，与 <c>CharacterPickerViewData</c> 同款形状。
///
/// <para>三态由这里统一决定，视图只管绑：<b>加载中</b>（本地为空且正在拉）、<b>就绪</b>（列表 + 底部数据年龄）、
/// <b>失败</b>（无数据时给重试；有旧数据时继续用旧的，只在底部说明）。</para>
///
/// <para>数据是进程级单例（<see cref="ModelsDevCatalog.Instance"/>），打开弹层时若本地为空会自动拉一次——
/// 这是「按需刷新」的两个触发点之一（ADR 0103），另一个是这里的「刷新」按钮。</para>
/// </summary>
public partial class ProviderPickerViewData : ObservableObject
{
    private readonly ModelsDevCatalog _catalog;
    private readonly Func<RemoteProviderInfo, Task> _onPicked;
    private bool _started; //首次打开已经触发过 EnsureAsync

    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>正在拉取（首次或手动刷新）</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>底部那行：数据年龄 / 失败说明 / 更新中</summary>
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>底部那行是否是坏消息（拉取失败或数据太旧），视图据此换色</summary>
    [ObservableProperty] private bool _isStatusWarning;

    /// <summary>没数据也没在拉：此时列表区显示空态，底部给重试</summary>
    [ObservableProperty] private bool _hasError;

    /// <summary>当前候选（已按搜索词筛过、已内置的置顶）</summary>
    public ObservableCollection<ProviderPickerItem> Items { get; } = new();

    /// <summary>列表为空：显示空态文案</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// 构造选择器
    /// </summary>
    /// <param name="onPicked">选中回调（点击即触发；可能弹确认，所以是异步的）</param>
    /// <param name="catalog">数据源；测试注入，省略用全局单例</param>
    public ProviderPickerViewData(Func<RemoteProviderInfo, Task> onPicked, ModelsDevCatalog? catalog = null)
    {
        _onPicked = onPicked;
        _catalog = catalog ?? ModelsDevCatalog.Instance;
        // 刻意不订阅 Changed：选择器是一次性的（每次开窗新建），订阅一个进程级单例的事件会把它永久钉住。
        // 别的窗口刷新了数据也不影响这里——下次打开重新 BeginAsync 就重建了
    }

    /// <summary>
    /// 打开弹层时调一次：本地有数据直接出列表，没有才联网拉。
    /// 幂等——同一个选择器实例反复打开不会重复拉。
    /// </summary>
    public async Task BeginAsync()
    {
        if (_started)
        {
            Rebuild();
            return;
        }

        _started = true;
        if (_catalog.IsLoaded)
        {
            Rebuild();
            return;
        }

        IsBusy = true;
        UpdateStatus();
        try
        {
            HasError = !await _catalog.EnsureAsync();
        }
        finally
        {
            IsBusy = false;
            Rebuild();
        }
    }

    /// <summary>手动刷新：带 ETag 的条件请求，没变就只更新时间戳</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        HasError = false;
        UpdateStatus();
        try
        {
            HasError = !await _catalog.RefreshAsync();
        }
        finally
        {
            IsBusy = false;
            Rebuild();
        }
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Items.Clear();
        foreach (ProviderPickerItem item in _catalog.Providers
                     .Where(provider => Matches(provider, SearchText))
                     // 已内置的置顶：用户大概率要找的就是那几家，其余按名称
                     .OrderByDescending(provider => RemoteModelConfigRegistry.TryGetConfigType(provider.Id, out _))
                     .ThenBy(provider => provider.Name, StringComparer.CurrentCultureIgnoreCase)
                     .Select(provider => new ProviderPickerItem(
                         provider,
                         RemoteModelConfigRegistry.TryGetConfigType(provider.Id, out _),
                         new RelayCommand(() => _ = _onPicked(provider)))))
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        UpdateStatus();
    }

    private static bool Matches(RemoteProviderInfo provider, string searchText)
    {
        string keyword = searchText.Trim();
        return keyword.Length == 0 ||
               provider.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
               provider.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateStatus()
    {
        if (IsBusy)
        {
            StatusText = Loc.Text(LangKey.ProviderPickerLoading);
            IsStatusWarning = false;
            return;
        }

        if (HasError && !_catalog.IsLoaded)
        {
            StatusText = Loc.Text(LangKey.ProviderPickerFailed);
            IsStatusWarning = true;
            return;
        }

        if (_catalog.FetchedAt is not { } fetchedAt || _catalog.Providers.Count == 0)
        {
            StatusText = Loc.Text(LangKey.ProviderDataUnknown);
            IsStatusWarning = false;
            return;
        }

        int count = _catalog.Providers.Count;
        bool stale = DateTimeOffset.Now - fetchedAt > TimeSpan.FromDays(30);
        StatusText = string.Format(
            Loc.Text(stale ? LangKey.ProviderDataStale : LangKey.ProviderDataStatus),
            count,
            fetchedAt.ToLocalTime().ToString("yyyy-MM-dd"));
        // 有旧数据时刷新失败也只是「数据旧」，不算坏消息，但要说清楚
        IsStatusWarning = stale || HasError;
    }
}
