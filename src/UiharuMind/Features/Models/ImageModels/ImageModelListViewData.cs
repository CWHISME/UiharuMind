using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 生图模型页签里本地模型用的动作（开窗、删文件、跳页面），由组装处给
/// </summary>
/// <param name="OpenLocalEditor">打开本地模型的参数窗：模型、其他模型已用的名字 → 保存后的新实例</param>
/// <param name="OpenDraw">打开画图窗口，默认选中这个模型（null 选第一个）</param>
/// <param name="DeleteLocalFiles">删本地模型不再有人用的组件文件（停引擎、在后台删）</param>
/// <param name="GoToMarket">去「获取模型」的生图分类</param>
/// <param name="OpenEngineSettings">去设置的「本地模型（生图）」</param>
/// <param name="EngineStatus">本地引擎的状态条；null 不显示</param>
/// <param name="IsTierInstalled">某一档的组件都在本机：打开页签时据此补回没登记的本地模型；null 不补</param>
/// <param name="SizeOnDisk">本地模型的组件占多大；null 不显示</param>
public sealed record ImageModelListActions(
    Func<ImageModelInfo, IReadOnlyCollection<string>, Task<ImageModelInfo?>> OpenLocalEditor,
    Action<ImageModelInfo?> OpenDraw,
    Func<ImageModelInfo, Task<IReadOnlyList<string>>> DeleteLocalFiles,
    Action GoToMarket,
    Action OpenEngineSettings,
    EngineStatusData? EngineStatus = null,
    Func<ImageCatalogTier, bool>? IsTierInstalled = null,
    Func<ImageModelInfo, long>? SizeOnDisk = null)
{
    /// <summary>
    /// 什么都不做（测试与没接本地生图时）
    /// </summary>
    public static ImageModelListActions None { get; } = new((_, _) => Task.FromResult<ImageModelInfo?>(null), _ => { },
        _ => Task.FromResult<IReadOnlyList<string>>([]), () => { }, () => { });
}

/// <summary>
/// 生图模型列表：增删改与排序。列表顺序即回退顺序（ADR 0052），每次改动立刻落盘，
/// 下一次出图就按新的来——装配快照只记「有没有」，顺序与内容每次出图现读。
/// </summary>
public partial class ImageModelListViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly Func<ImageModelInfo?, IReadOnlyCollection<string>, Task<ImageModelInfo?>> _openEditor;
    private readonly ImageModelListActions _actions;
    private readonly ImageModelCatalog _catalog;

    /// <param name="messageService">删除确认</param>
    /// <param name="openEditor">打开远程模型编辑框：待编辑的模型（null 为新建）、其他模型已用的名字 → 确认后的新实例</param>
    /// <param name="actions">本地模型用的动作；null 都不做</param>
    /// <param name="catalog">可下载列表（本地条目显示家族名）；null 取内置</param>
    public ImageModelListViewData(IMessageService messageService,
        Func<ImageModelInfo?, IReadOnlyCollection<string>, Task<ImageModelInfo?>> openEditor,
        ImageModelListActions? actions = null, ImageModelCatalog? catalog = null)
    {
        _messageService = messageService;
        _openEditor = openEditor;
        _actions = actions ?? ImageModelListActions.None;
        _catalog = catalog ?? ImageModelCatalog.BuiltIn;
        AdoptInstalled();
        Refresh();
    }

    /// <summary>列表行，按回退顺序</summary>
    public ObservableCollection<ImageModelItemViewData> Items { get; } = new();

    /// <summary>有生图模型</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>本地引擎的状态</summary>
    public EngineStatusData? EngineStatus => _actions.EngineStatus;

    /// <summary>列表里有本地模型时才显示引擎状态条</summary>
    public bool ShowEngineStatus => EngineStatus != null && Items.Any(x => x.IsLocal);

    private static List<ImageModelInfo> Models => ImageModelSettingConfig.Current.Models;

    private string? FamilyName(ImageModelInfo model) =>
        model.Local?.Family is { } family ? _catalog.Find(family)?.Name : null;

    /// <summary>
    /// 按配置重建列表。熔断会随时间恢复，切到这一页时重刷一次状态
    /// </summary>
    public void Refresh()
    {
        Items.Clear();
        for (int i = 0; i < Models.Count; i++)
            Items.Add(new ImageModelItemViewData(Models[i], i, Models.Count, FamilyName(Models[i]),
                _actions.SizeOnDisk?.Invoke(Models[i]) ?? 0));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEngineStatus));
    }

    /// <summary>
    /// 把一个模型加到列表末尾（获取模型里装好的本地模型走这里）
    /// </summary>
    /// <param name="model">模型</param>
    public void Append(ImageModelInfo model) => Commit(models => models.Add(model));

    [RelayCommand]
    private async Task Add()
    {
        ImageModelInfo? created = await _openEditor(null, NamesExcept(null));
        if (created == null) return;

        Commit(models => models.Add(created));
    }

    [RelayCommand]
    private async Task Edit(ImageModelItemViewData item)
    {
        ImageModelInfo? edited = item.IsLocal
            ? await _actions.OpenLocalEditor(item.Model, NamesExcept(item.Model))
            : await _openEditor(item.Model, NamesExcept(item.Model));
        int index = Models.IndexOf(item.Model);
        if (edited == null || index < 0) return;

        Commit(models => models[index] = edited);
    }

    [RelayCommand]
    private async Task Delete(ImageModelItemViewData item)
    {
        if (!item.IsLocal)
        {
            if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.ImageModelDeleteConfirm, item.Name))) return;
            Commit(models => models.Remove(item.Model));
            return;
        }

        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.ImageModelLocalDeleteConfirm, item.Name))) return;
        try
        {
            // 先删文件再移出列表；按引用删时会排除它自己
            await _actions.DeleteLocalFiles(item.Model);
            _messageService.ShowNotification(Loc.Text(LangKey.ModelDeleteFilesDone, item.Name),
                severity: MessageSeverity.Success);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _messageService.ShowNotification(Loc.Text(LangKey.ModelDeleteFilesFailed, e.Message),
                severity: MessageSeverity.Error);
        }

        Commit(models => models.Remove(item.Model));
    }

    [RelayCommand]
    private void Draw(ImageModelItemViewData? item) => _actions.OpenDraw(item?.Model);

    [RelayCommand]
    private void GoToMarket() => _actions.GoToMarket();

    [RelayCommand]
    private void OpenEngineSettings() => _actions.OpenEngineSettings();

    [RelayCommand]
    private void MoveUp(ImageModelItemViewData item) => Move(item, -1);

    [RelayCommand]
    private void MoveDown(ImageModelItemViewData item) => Move(item, 1);

    private void Move(ImageModelItemViewData item, int offset)
    {
        int from = Models.IndexOf(item.Model);
        int to = from + offset;
        if (from < 0 || to < 0 || to >= Models.Count) return;

        Commit(models => (models[from], models[to]) = (models[to], models[from]));
    }

    // 只在建页签时补：删条目后组件在后台删，Refresh 时补会把刚删的又加回来
    private void AdoptInstalled()
    {
        if (_actions.IsTierInstalled is not { } isInstalled) return;
        IReadOnlyList<ImageModelInfo> found = LocalImageEntries.FindUnlisted(_catalog, Models, isInstalled);
        if (found.Count > 0) ImageModelSettingConfig.Current.ReplaceModels([..Models, ..found]);
    }

    private IReadOnlyCollection<string> NamesExcept(ImageModelInfo? self) =>
        Models.Where(m => !ReferenceEquals(m, self)).Select(m => m.Name).ToList();

    private void Commit(Action<List<ImageModelInfo>> change)
    {
        List<ImageModelInfo> next = new(Models);
        change(next);
        ImageModelSettingConfig.Current.ReplaceModels(next);
        Refresh();
    }
}
