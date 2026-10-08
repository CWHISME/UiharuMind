using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.Downloads.Image;

/// <summary>
/// 「获取模型」的生图分类：可下载的本地生图模型来自内置列表（照 sd.cpp 文档转写），推荐的排前面，
/// 没实际出过图的标「未测试」。没装引擎时引导先装
/// </summary>
public partial class ImageMarketData : ObservableObject, IModelMarket
{
    private readonly ModelDownloadContext _context;
    private readonly ImageMarketServices _image;
    private readonly List<ImageFamilyListItem> _all;
    private bool _isWatchingEngine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private ImageFamilyDetailData? _detail;

    [ObservableProperty] private ImageFamilyListItem? _selectedFamily;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _showEnginePrompt;
    [ObservableProperty] private bool _isEngineQueued;
    [ObservableProperty] private string _sourceLabel = ModelDownloadSourceInfo.Label(true);

    /// <param name="context">各分类共用的依赖</param>
    /// <param name="image">生图分类的依赖</param>
    public ImageMarketData(ModelDownloadContext context, ImageMarketServices image)
    {
        _context = context;
        _image = image;
        string language = ImageMarketServices.Language;
        _all = image.Catalog.Families
            .OrderByDescending(x => x.Recommended)
            .ThenByDescending(x => x.Tested)
            .Select(x => new ImageFamilyListItem(x, language))
            .ToList();
        ApplyFilter();
    }

    public string Title => Loc.Text(LangKey.ImageModelsTitle);

    public string IconName => "image";

    public string EmptyHint => Loc.Text(LangKey.ImageMarketPickFamily);

    public bool HasDetail => Detail != null;

    object? IModelMarket.Detail => Detail;

    /// <summary>
    /// 按搜索过滤后的家族
    /// </summary>
    public ObservableCollection<ImageFamilyListItem> Families { get; } = [];

    /// <summary>
    /// 搜不到
    /// </summary>
    public bool IsResultEmpty => Families.Count == 0;

    public void Activate()
    {
        if (!_isWatchingEngine)
        {
            _isWatchingEngine = true;
            _image.Engine.Distribution.VersionChanged += () => UiDispatcher.FireAndForget(() => _ = RefreshEngineAsync());
        }

        SourceLabel = ModelDownloadSourceInfo.Label(true);
        _ = RefreshEngineAsync();
        RefreshInstalled();
    }

    public void CloseDetail()
    {
        Detail?.Dispose();
        Detail = null;
        SelectedFamily = null;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFamilyChanged(ImageFamilyListItem? value)
    {
        if (value == null || value.Family == Detail?.Family) return;
        Detail?.Dispose();
        ImageFamilyDetailData detail = new(_context, _image, value.Family, RefreshInstalled);
        Detail = detail;
        _ = detail.LoadAsync();
    }

    // 与下载对话模型时自动装 llama.cpp 同一条路：排进全局队列，装好后选中
    [RelayCommand]
    private async Task InstallEngine()
    {
        IsEngineQueued = true;
        ILocalEngine engine = _image.Engine.Distribution;
        EEngineEnsureState? state = await EngineInstallFeedback.EnsureAsync(() => engine.EnsureQueuedAsync(),
            _context.Messages,
            new EngineEnsureTexts(LangKey.ImageEngineNoPackage, LangKey.ImageEngineQueued, LangKey.ImageEngineCheckFailed),
            () => IsEngineQueued = false);
        if (state == EEngineEnsureState.AlreadyInstalled) await RefreshEngineAsync();
        else if (state != EEngineEnsureState.Queued) IsEngineQueued = false;
    }

    [RelayCommand]
    private void OpenSourceSettings() => _context.OpenSourceSettings();

    private async Task RefreshEngineAsync()
    {
        SdCppVersion? current = await Task.Run(() => _image.Engine.Distribution.EnsureCurrentAsync());
        ShowEnginePrompt = current == null;
        if (current != null) IsEngineQueued = false;
    }

    private void RefreshInstalled()
    {
        foreach (ImageFamilyListItem item in _all)
            item.IsInstalled = item.Family.Tiers.Any(_image.Store.IsInstalled);
        Detail?.RefreshStatuses();
    }

    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        Families.Clear();
        foreach (ImageFamilyListItem item in _all.Where(x => x.Matches(query))) Families.Add(item);
        OnPropertyChanged(nameof(IsResultEmpty));
    }
}
