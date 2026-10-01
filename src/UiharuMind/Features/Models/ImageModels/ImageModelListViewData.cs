using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 生图模型列表：增删改与排序。列表顺序即回退顺序（ADR 0052），每次改动立刻落盘，
/// 下一次出图就按新的来——装配快照只记「有没有」，顺序与内容每次出图现读。
/// </summary>
public partial class ImageModelListViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly Func<ImageModelInfo?, IReadOnlyCollection<string>, Task<ImageModelInfo?>> _openEditor;

    /// <param name="messageService">删除确认</param>
    /// <param name="openEditor">打开编辑框：待编辑的模型（null 为新建）、其他模型已用的名字 → 确认后的新实例</param>
    public ImageModelListViewData(IMessageService messageService,
        Func<ImageModelInfo?, IReadOnlyCollection<string>, Task<ImageModelInfo?>> openEditor)
    {
        _messageService = messageService;
        _openEditor = openEditor;
        Refresh();
    }

    /// <summary>列表行，按回退顺序</summary>
    public ObservableCollection<ImageModelItemViewData> Items { get; } = new();

    /// <summary>有生图模型</summary>
    public bool HasItems => Items.Count > 0;

    private static List<ImageModelInfo> Models => ImageModelSettingConfig.Current.Models;

    /// <summary>
    /// 按配置重建列表。熔断会随时间恢复，切到这一页时重刷一次状态
    /// </summary>
    public void Refresh()
    {
        Items.Clear();
        for (int i = 0; i < Models.Count; i++) Items.Add(new ImageModelItemViewData(Models[i], i, Models.Count));
        OnPropertyChanged(nameof(HasItems));
    }

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
        ImageModelInfo? edited = await _openEditor(item.Model, NamesExcept(item.Model));
        int index = Models.IndexOf(item.Model);
        if (edited == null || index < 0) return;

        Commit(models => models[index] = edited);
    }

    [RelayCommand]
    private async Task Delete(ImageModelItemViewData item)
    {
        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.ImageModelDeleteConfirm, item.Name))) return;
        Commit(models => models.Remove(item.Model));
    }

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
