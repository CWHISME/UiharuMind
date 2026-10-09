using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 一张参考图：存路径，出图时才读字节；界面只攥一张小缩略图
/// </summary>
/// <param name="Path">文件路径</param>
/// <param name="Thumbnail">缩略图</param>
public sealed record ImageDrawReference(string Path, Bitmap Thumbnail)
{
    /// <summary>文件名（悬停提示）</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// 画图窗口的参考图：有就是改图，第一张是要改的那张、其余是参考（与 GenerateImage 工具同一口径）
/// </summary>
public partial class ImageDrawReferences : ObservableObject, IDisposable
{
    private const int ThumbnailWidth = 128;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp" };

    /// <summary>参考图，按顺序</summary>
    public ObservableCollection<ImageDrawReference> Items { get; } = new();

    /// <summary>有参考图（出图即改图）</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>还能再加</summary>
    public bool CanAdd => Items.Count < ImageRequest.MaxImages;

    /// <summary>增删过</summary>
    public event Action? Changed;

    /// <summary>
    /// 认不认这个文件（拖进来的先过一遍，不是图就不接）
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>按扩展名是图片</returns>
    public static bool IsImageFile(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// 加几张参考图：不是图的、重复的、超出上限的静默跳过
    /// </summary>
    /// <param name="paths">文件路径</param>
    /// <param name="asFirst">放到最前（拿画好的那张接着改）</param>
    /// <returns>加完时完成</returns>
    public async Task AddAsync(IEnumerable<string> paths, bool asFirst = false)
    {
        List<string> fresh = paths.Where(IsImageFile).Distinct()
            .Where(p => Items.All(x => x.Path != p) && File.Exists(p)).ToList();
        int index = asFirst ? 0 : Items.Count;
        foreach (string path in fresh)
        {
            if (!CanAdd) break;
            Bitmap thumbnail;
            try
            {
                // 解码放到线程池：几张大照片同步解码会卡住界面
                thumbnail = await Task.Run(() =>
                {
                    using FileStream stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
                });
            }
            catch (Exception)
            {
                continue; //扩展名是图、内容不是：跳过
            }

            Items.Insert(index++, new ImageDrawReference(path, thumbnail));
            NotifyChanged();
        }
    }

    /// <summary>
    /// 读出参考图交给出图请求
    /// </summary>
    /// <returns>输入图；有文件已被删或认不出时返回出错的文件名</returns>
    public async Task<(List<ImageInput> Inputs, string? Error)> LoadAsync()
    {
        List<ImageInput> inputs = new();
        foreach (ImageDrawReference item in Items)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(item.Path);
            }
            catch (IOException)
            {
                return (inputs, item.FileName);
            }

            if (ImageFormats.Sniff(bytes) is not { } mediaType) return (inputs, item.FileName);
            inputs.Add(new ImageInput(bytes, mediaType));
        }

        return (inputs, null);
    }

    [RelayCommand]
    private void Remove(ImageDrawReference item)
    {
        if (!Items.Remove(item)) return;
        item.Thumbnail.Dispose();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanAdd));
        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (ImageDrawReference item in Items) item.Thumbnail.Dispose();
        Items.Clear();
    }
}
