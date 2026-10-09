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
/// 生图分类左上的筛选项：全部 / 可改图 / 仅文生图
/// </summary>
/// <param name="Editing">要不要能改图；null 不筛</param>
/// <param name="Label">显示文字</param>
public sealed record ImageCapabilityFilterOption(bool? Editing, string Label);

/// <summary>
/// 「获取模型」的生图分类：可下载的本地生图模型来自内置列表（照 sd.cpp 文档转写），推荐的排前面，
/// 没实际出过图的标「未测试」。没装引擎时引导先装
/// </summary>
public partial class ImageMarketData : ObservableObject, IModelMarket
{
    private readonly ModelDownloadContext _context;
    private readonly ImageMarketServices _image;
    private readonly List<ImageFamilyListItem> _all;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private ImageFamilyDetailData? _detail;

    [ObservableProperty] private ImageFamilyListItem? _selectedFamily;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ImageCapabilityFilterOption _selectedFilter;
    [ObservableProperty] private string _sourceLabel = ModelDownloadSourceInfo.Label(true);

    /// <param name="context">各分类共用的依赖</param>
    /// <param name="image">生图分类的依赖</param>
    public ImageMarketData(ModelDownloadContext context, ImageMarketServices image)
    {
        _context = context;
        _image = image;
        FilterOptions =
        [
            new ImageCapabilityFilterOption(null, Loc.Text(LangKey.ImageMarketFilterAll)),
            new ImageCapabilityFilterOption(true, Loc.Text(LangKey.ImageMarketFilterEditing)),
            new ImageCapabilityFilterOption(false, Loc.Text(LangKey.ImageMarketFilterTextOnly))
        ];
        _selectedFilter = FilterOptions[0];
        string language = ImageMarketServices.Language;
        _all = image.Catalog.Families
            .OrderByDescending(x => x.Recommended)
            .ThenByDescending(x => x.Tested)
            .Select(x => new ImageFamilyListItem(x, language))
            .ToList();
        EnginePrompt = new EngineInstallPromptData(image.Engine, context.Messages,
            new EngineInstallPromptTexts(LangKey.ImageMarketNeedsEngine, LangKey.ImageInstallEngine,
                LangKey.ImageMarketEngineInstalling,
                new EngineEnsureTexts(LangKey.ImageEngineNoPackage, LangKey.ImageEngineQueued,
                    LangKey.ImageEngineCheckFailed)));
        ApplyFilter();
    }

    /// <summary>
    /// 没装引擎时的安装引导（列表照常可看可下，顶上提示）
    /// </summary>
    public EngineInstallPromptData EnginePrompt { get; }

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
    /// 筛选项（与语音分类的「合成 / 识别」同一个位置）
    /// </summary>
    public ImageCapabilityFilterOption[] FilterOptions { get; }

    /// <summary>
    /// 搜不到
    /// </summary>
    public bool IsResultEmpty => Families.Count == 0;

    public void Activate()
    {
        EnginePrompt.Watch();
        SourceLabel = ModelDownloadSourceInfo.Label(true);
        _ = EnginePrompt.RefreshAsync();
        RefreshInstalled();
    }

    public void CloseDetail()
    {
        Detail?.Dispose();
        Detail = null;
        SelectedFamily = null;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFilterChanged(ImageCapabilityFilterOption value) => ApplyFilter();

    partial void OnSelectedFamilyChanged(ImageFamilyListItem? value)
    {
        if (value == null || value.Family == Detail?.Family) return;
        Detail?.Dispose();
        ImageFamilyDetailData detail = new(_context, _image, value.Family, EnginePrompt.EnsureQueuedAsync, RefreshInstalled);
        Detail = detail;
        _ = detail.LoadAsync();
    }

    [RelayCommand]
    private void OpenSourceSettings() => _context.OpenSourceSettings();

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
        foreach (ImageFamilyListItem item in _all.Where(x =>
                     (SelectedFilter.Editing == null || x.Family.SupportsEditing == SelectedFilter.Editing) &&
                     x.Matches(query)))
            Families.Add(item);
        OnPropertyChanged(nameof(IsResultEmpty));
    }
}
