using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Data;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// 引擎版本卡：在用哪个版本、有没有新版本、可下的包与发布说明。具体是哪个引擎由 <see cref="ILocalEngine"/> 决定
/// </summary>
public partial class EngineVersionsViewData : ObservableObject
{
    private readonly ILocalEngine _source;
    private readonly IMessageService _messageService;
    private readonly Action<string> _openFolder;
    private readonly List<EnginePackage> _lastLocalVersions = []; //上一次本地版本，刷新时差分删除

    [ObservableProperty] private EnginePackage? _selectedVersion;
    [ObservableProperty] private SettingChoice<bool> _selectedChannel;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private string? _releaseInfo;

    /// <param name="source">哪个引擎</param>
    /// <param name="iconName">标题前的图标（Assets/Icons 下的名字）</param>
    /// <param name="versionDescription">版本下拉的说明：选中的版本用在哪</param>
    /// <param name="messageService">提示</param>
    /// <param name="openFolder">打开目录（界面外壳的系统调用）</param>
    public EngineVersionsViewData(ILocalEngine source, string iconName, string versionDescription,
        IMessageService messageService, Action<string> openFolder)
    {
        _source = source;
        _messageService = messageService;
        _openFolder = openFolder;
        IconName = iconName;
        VersionDescription = versionDescription;
        _selectedChannel = ChannelOptions.First(x => x.Value == (source.Channels?.UseStableChannel ?? true));
        Packages = new DownloadListViewData(messageService)
        {
            DownloadCompletedHandler = OnPackageDownloaded,
            DeleteConfirmMessageProvider = () => Loc.Text(LangKey.ConfirmDeleteRuntimeEngine)
        };
        Packages.OnDownloadFileChange += () => _ = RefreshLocalVersionsAsync();
        // 别处（获取模型、模型下载）自动装的引擎也要出现在版本下拉里
        source.Installed += () => Dispatcher.UIThread.Post(() => _ = RefreshLocalVersionsAsync());
        source.AvailableUpdateChanged += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableUpdate));
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateText));
        });
        _ = RefreshLocalVersionsAsync();
    }

    /// <summary>
    /// 引擎名
    /// </summary>
    public string EngineName => _source.Name;

    /// <summary>
    /// 标题前的图标
    /// </summary>
    public string IconName { get; }

    /// <summary>
    /// 版本下拉的说明
    /// </summary>
    public string VersionDescription { get; }

    /// <summary>
    /// 本机已装的版本
    /// </summary>
    public ObservableCollection<EnginePackage> LocalVersions { get; } = [];

    /// <summary>
    /// 可下的包（含已装的）
    /// </summary>
    public DownloadListViewData Packages { get; }

    /// <summary>
    /// 卡片上的版本号；没装时说明一句
    /// </summary>
    public string VersionText => SelectedVersion?.Name ?? Loc.Text(LangKey.EngineNotInstalled);

    /// <summary>
    /// 当前版本是本机推荐的变体
    /// </summary>
    public bool IsRecommended => SelectedVersion?.IsRecommended == true;

    /// <summary>
    /// 当前版本的发布页
    /// </summary>
    public string? ReleaseUrl => SelectedVersion == null ? null : _source.ReleaseUrl(SelectedVersion);

    /// <summary>
    /// 比本机新的推荐包；没有为 null
    /// </summary>
    public EnginePackage? AvailableUpdate => _source.AvailableUpdate;

    /// <summary>
    /// 有新版本可更新
    /// </summary>
    public bool HasUpdate => AvailableUpdate != null;

    /// <summary>
    /// 新版本提示
    /// </summary>
    public string UpdateText => AvailableUpdate == null ? "" : Loc.Text(LangKey.EngineUpdateAvailable, AvailableUpdate.Name);

    /// <summary>
    /// 有没有更新通道可选
    /// </summary>
    public bool HasChannels => _source.Channels != null;

    /// <summary>
    /// 更新通道：预览版几乎天天有新构建，正式版只跟正式版钉住的构建
    /// </summary>
    public IReadOnlyList<SettingChoice<bool>> ChannelOptions { get; } =
    [
        new(false, Loc.Text(LangKey.EngineChannelPreview)),
        new(true, Loc.Text(LangKey.EngineChannelStable))
    ];

    // 与模型下载自动装引擎同一条路：排进全局下载队列，装好后选中
    [RelayCommand]
    private void InstallUpdate()
    {
        if (AvailableUpdate is not { } version) return;
        DownloadJob job = _source.Enqueue(version);
        _messageService.ShowNotification(Loc.Text(LangKey.EngineUpdateQueued, version.Name));
        EngineInstallFeedback.Watch(job, version.Name, _messageService);
    }

    [RelayCommand]
    private async Task CheckUpdate()
    {
        await AsyncCommandScope.RunAsync(
            v => IsCheckingForUpdate = v,
            PullRemoteVersionsAsync,
            e => ReleaseInfo = e.Message, //拉取失败就把原因写在说明那一栏，界面本来就在显示它
            IsCheckingForUpdate);
    }

    [RelayCommand]
    private void OpenFolder() => _openFolder(_source.RootDirectory);

    /// <summary>
    /// 以磁盘为准刷新本机版本，并同步选中项
    /// </summary>
    public async Task RefreshLocalVersionsAsync()
    {
        IReadOnlyList<EnginePackage> versions = await _source.GetLocalVersionsAsync();
        List<EnginePackage> gone = _lastLocalVersions.Except(versions).ToList();
        foreach (EnginePackage version in versions)
        {
            // 列表里那行若是拉远端时建的（不知道已经装好，比如别处装的），换成本机这份
            if (Packages.Find(version.Name) is { IsDownloaded: false } stale && version.IsInstalled)
                Packages.RemoveItem(stale);
            Packages.AddItem(new DownloadableItemData(version, true));
            if (!LocalVersions.Contains(version)) LocalVersions.Add(version);
        }

        foreach (EnginePackage version in gone) LocalVersions.Remove(version);
        _lastLocalVersions.Clear();
        _lastLocalVersions.AddRange(versions);
        SelectedVersion = _source.CurrentVersion;
    }

    private async Task PullRemoteVersionsAsync()
    {
        Packages.ClearIfNotExists();
        EngineRemoteVersions remote = await _source.PullRemoteVersionsAsync();
        foreach (EnginePackage version in remote.Versions)
        {
            if (Packages.IsExists(version.Name)) continue;
            Packages.AddItem(new DownloadableItemData(version, true));
        }

        ReleaseInfo = remote.ReleaseInfo;
    }

    partial void OnSelectedChannelChanged(SettingChoice<bool> value)
    {
        if (_source.Channels != null) _source.Channels.UseStableChannel = value.Value;
    }

    partial void OnSelectedVersionChanged(EnginePackage? oldValue, EnginePackage? newValue)
    {
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(IsRecommended));
        OnPropertyChanged(nameof(ReleaseUrl));
        if (newValue == null)
        {
            // 下拉清空（如列表刷新）时退回在用的那个
            if (_source.CurrentVersion != null) SelectedVersion = _source.CurrentVersion;
            return;
        }

        if (oldValue != null && !Equals(newValue, _source.CurrentVersion)) _source.Select(newValue);
    }

    // 与模型下载自动装引擎走同一条安装：解压到临时目录、校验有服务端程序再换上
    private async Task OnPackageDownloaded(DownloadableItemData item)
    {
        var version = (EnginePackage)item.Target;
        item.IsDownloading = true;
        item.DownloadInfo = Loc.Text(LangKey.Decompressing) + item.DownloadInfo;
        try
        {
            await _source.InstallAsync(version);
        }
        catch (Exception e)
        {
            Log.Error($"Install {EngineName} failed: {version.Name}, {e.Message}");
            item.ErrorMessage = e.Message;
        }

        item.IsDownloading = false;
        item.InitFileSize();
    }
}
