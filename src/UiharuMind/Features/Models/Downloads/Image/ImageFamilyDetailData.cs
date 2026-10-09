using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Models.ImageModels;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.Downloads.Image;

/// <summary>
/// 生图分类右栏：一个家族的简介与各档位。下一档就是把它的组件（共用的只下一次）排进队列，到齐后加进生图模型列表
/// </summary>
public partial class ImageFamilyDetailData : ObservableObject, IDisposable
{
    private readonly ModelDownloadContext _context;
    private readonly ImageMarketServices _image;
    private readonly Func<Task> _ensureEngine;
    private readonly Action _onInstalledChanged;
    private readonly CancellationTokenSource _cancellation = new();

    [ObservableProperty] private bool _isLoading;

    /// <param name="context">各分类共用的依赖</param>
    /// <param name="image">生图分类的依赖</param>
    /// <param name="family">家族</param>
    /// <param name="ensureEngine">没装引擎就排上（与分类顶上的安装引导同一份状态）</param>
    /// <param name="onInstalledChanged">装好一档后（左栏的已装标记跟着刷）</param>
    public ImageFamilyDetailData(ModelDownloadContext context, ImageMarketServices image, ImageCatalogFamily family,
        Func<Task> ensureEngine, Action onInstalledChanged)
    {
        _context = context;
        _image = image;
        _ensureEngine = ensureEngine;
        _onInstalledChanged = onInstalledChanged;
        Family = family;
        Description = family.DescriptionFor(ImageMarketServices.Language);
        Tags =
        [
            Loc.Text(family.SupportsEditing ? LangKey.ImageFamilyEditing : LangKey.ImageFamilyTextToImage),
            Loc.Text(LangKey.ImageFamilyBaseSize, family.BaseResolution)
        ];
        Rows = new ObservableCollection<ImageTierRowData>(family.Tiers.Select(x => new ImageTierRowData(x)));
        _context.QueueView.Changed += RefreshStatuses;
        RefreshStatuses();
    }

    /// <summary>
    /// 家族
    /// </summary>
    public ImageCatalogFamily Family { get; }

    public string Title => Family.Name;

    /// <summary>
    /// 简介（按界面语言）
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 标签：能做什么、基准边长
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// 没实际出过图
    /// </summary>
    public bool IsUntested => !Family.Tested;

    /// <summary>
    /// 档位
    /// </summary>
    public ObservableCollection<ImageTierRowData> Rows { get; }

    /// <summary>
    /// 算各档在这台机器上的内存风险
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            RuntimeDeviceInfo device = await Task.Run(_context.DeviceInfo, _cancellation.Token);
            foreach (ImageTierRowData row in Rows)
                row.ApplyRisk(RuntimeLoadRiskEvaluator.EstimateBeforeDownload(row.Tier.TotalSize, 0, device),
                    RuntimeLoadRiskEvaluator.EstimateBytesBeforeDownload(row.Tier.TotalSize, 0), device);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 以磁盘与下载队列为准刷新每档状态
    /// </summary>
    public void RefreshStatuses()
    {
        FileDownloadStates downloads = _context.SnapshotDownloads();
        foreach (ImageTierRowData row in Rows)
        {
            List<QuantFileSnapshot> files = row.Tier.Components
                .Select(x => downloads.Of(_image.Store.PathOf(x), x.Size)).ToList();
            // 已在本机的组件（多是几个模型共用的）不算这一档的进度，只看还缺的
            List<QuantFileSnapshot> missing = files.Where(x => !x.Exists).ToList();
            row.ApplyStatus(QuantDownloadStatus.Resolve(missing.Count == 0 ? files : missing, false));
        }
    }

    // 没装引擎就把推荐包一起排进去；拉不到包也不拦着下模型
    [RelayCommand]
    private async Task Download(ImageTierRowData row)
    {
        if (!row.CanDownload) return;
        await _ensureEngine();
        try
        {
            IReadOnlyList<DownloadJob> jobs = _image.Store.Enqueue(row.Tier, () =>
            {
                UiDispatcher.FireAndForget(() => OnInstalled(row));
                return Task.CompletedTask;
            });
            if (jobs.Count == 0 && _image.Store.IsInstalled(row.Tier)) OnInstalled(row);
            RefreshStatuses();
        }
        catch (Exception e)
        {
            Log.Warning($"Download image model failed: {Family.Id}/{row.Tier.Id}, {e.Message}");
            _context.Messages.ShowNotification(Loc.Text(LangKey.ImageModelDownloadFailed, row.Label, e.Message),
                severity: MessageSeverity.Error);
        }
    }

    // 下好了就用它画一张
    [RelayCommand]
    private void Draw(ImageTierRowData row) => _image.OpenDraw(FindEntry(row.Tier) ?? AddEntry(row.Tier));

    [RelayCommand]
    private void OpenDoc() => _image.OpenUrl(Family.Doc);

    [RelayCommand]
    private void OpenSourceSettings() => _context.OpenSourceSettings();

    public void Dispose()
    {
        _context.QueueView.Changed -= RefreshStatuses;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private void OnInstalled(ImageTierRowData row)
    {
        RefreshStatuses();
        _onInstalledChanged();
        if (FindEntry(row.Tier) != null) return;
        ImageModelInfo entry = AddEntry(row.Tier);
        _context.Messages.ShowNotification(Loc.Text(LangKey.ImageModelReady, entry.Name),
            severity: MessageSeverity.Success);
    }

    private ImageModelInfo? FindEntry(ImageCatalogTier tier) => _image.Models()
        .FirstOrDefault(x => x.Local is { } local && local.Family == Family.Id && local.Tier == tier.Id);

    // 名字：家族名，多档都装了再带上档位
    private ImageModelInfo AddEntry(ImageCatalogTier tier)
    {
        HashSet<string> taken = _image.Models().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        string name = taken.Contains(Family.Name) ? $"{Family.Name} ({ImageTierText.Label(tier.Id)})" : Family.Name;
        for (int i = 2; taken.Contains(name); i++) name = $"{Family.Name} ({ImageTierText.Label(tier.Id)} {i})";
        ImageModelInfo entry = LocalImageModels.Create(Family, tier, name);
        _image.AddModel(entry);
        return entry;
    }
}
