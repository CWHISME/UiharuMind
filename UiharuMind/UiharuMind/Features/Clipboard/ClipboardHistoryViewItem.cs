/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Core.Clipboard;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 列表上的一条剪贴板历史。
///
/// ⚠️ 它<b>不持有正文</b>，只有预览与主键——历史是无上限的，正文又常常是整段代码或整篇文章，
/// 一并驻留的话内存就正比于「用户复制过的全部内容」。正文在悬停与复制时才按主键现取。
/// </summary>
public partial class ClipboardItem : ObservableObject
{
    private const int TooltipLength = 2000; //悬停预览的上限。全文直接塞进 tooltip 是个布局炸弹

    /// <summary>主键</summary>
    public long Id { get; }

    /// <summary>首次进入剪贴板的时刻。置顶不会改动它</summary>
    public DateTime CreatedAt { get; }

    /// <summary>排序键，分页续取时当游标用</summary>
    public long SortKey { get; }

    /// <summary>图片路径；文本条目为空串</summary>
    public string ImageSource { get; }

    /// <summary>是否为图片条目</summary>
    public bool IsImage { get; }

    /// <summary>列表上显示的日期</summary>
    public string Date => CreatedAt.ToString("(yyyy-MM-dd HH:mm:ss)");

    [ObservableProperty] private string _preview;
    [ObservableProperty] private bool _isFavorite;

    /// <summary>悬停预览的正文，按需加载</summary>
    [ObservableProperty] private string _tooltipText = string.Empty;

    private bool _isTooltipLoaded;

    public ClipboardItem(ClipboardHistoryEntry entry)
    {
        Id = entry.Id;
        CreatedAt = entry.CreatedAt;
        SortKey = entry.SortKey;
        ImageSource = entry.ImagePath ?? string.Empty;
        IsImage = entry.IsImage;
        _preview = entry.Preview;
        _isFavorite = entry.IsFavorite;
    }

    /// <summary>
    /// 把正文取回来填进悬停预览。只取一次，重复悬停不再读盘
    /// </summary>
    public async Task LoadTooltipAsync()
    {
        if (_isTooltipLoaded || IsImage) return;
        _isTooltipLoaded = true;

        string text = await Task.Run(() => App.Clipboard.History.GetText(Id) ?? string.Empty);
        TooltipText = text.Length > TooltipLength
            ? text[..TooltipLength] + string.Format(
                LocalizationManager.Instance.GetString("ClipboardTooltipMore"), text.Length.ToString("N0"))
            : text;
    }

    /// <summary>把这条重新放回剪贴板，并置顶</summary>
    public void CopyToClipboard()
    {
        App.Clipboard.MoveClipboardHistoryItemFirst(Id);
        if (IsImage)
        {
            // 历史项只存路径,这里现解两张,两张都是移交、都不由本处释放:
            // 一张交剪贴板(平台持有到下次复制顶掉,见 CopyImageToClipboard 的注释,当场释放会让粘贴取空),
            // 另一张交预览窗(那个重载是接管语义,关窗时由它释放)
            App.Clipboard.CopyImageToClipboard(new Bitmap(ImageSource), true);
            UIManager.ShowPreviewImageWindowAtMousePosition(new Bitmap(ImageSource),
                horizontalAlignment: HorizontalAlignment.Center, verticalAlignment: VerticalAlignment.Center);
            return;
        }

        string? text = App.Clipboard.History.GetText(Id);
        if (text != null) App.Clipboard.CopyToClipboard(text, true);
    }
}
