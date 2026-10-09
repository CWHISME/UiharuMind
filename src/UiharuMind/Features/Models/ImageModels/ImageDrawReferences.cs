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
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 一张参考图：存路径，出图时才读字节；界面只攥一张小缩略图。
/// 粘贴产生的（<c>IsPasted</c>）文件是临时落盘的，移除/关窗时删除；拖放进来的文件属于用户，不碰。
/// </summary>
/// <param name="Path">文件路径</param>
/// <param name="Thumbnail">缩略图</param>
/// <param name="IsPasted">是否粘贴产生的临时文件（移除/关窗时删除）</param>
public sealed record ImageDrawReference(string Path, Bitmap Thumbnail, bool IsPasted = false)
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
    /// <param name="isPasted">粘贴产生的临时文件，移除/关窗时删除</param>
    /// <returns>加完时完成</returns>
    public async Task AddAsync(IEnumerable<string> paths, bool asFirst = false, bool isPasted = false)
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

            Items.Insert(index++, new ImageDrawReference(path, thumbnail, isPasted));
            NotifyChanged();
        }
    }

    /// <summary>
    /// 把剪贴板粘贴来的图加为参考图：编码 PNG 落盘到给定目录（画图窗用系统临时目录，
    /// 与画好的图库目录分开，不污染图库），再走文件路径的常规链路。追加到末尾，与拖放一致；
    /// 文件标记 <c>IsPasted</c>，移除该参考图或窗口关闭时删除。
    /// </summary>
    /// <param name="bitmap">粘贴图，本方法只借用（编码后即弃），调用方负责释放</param>
    /// <param name="directory">落盘目录</param>
    /// <returns>是否成功加入</returns>
    public async Task<bool> AddPastedBitmapAsync(Bitmap bitmap, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"pasted_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
            await File.WriteAllBytesAsync(path, bitmap.BitmapToBytes());
            await AddAsync([path], isPasted: true);
            return Items.Any(x => x.Path == path);
        }
        catch (Exception e)
        {
            Log.Warning($"Paste reference image failed: {e.Message}");
            return false;
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
        DeletePastedFile(item);
        NotifyChanged();
    }

    // 粘贴参考图的文件是临时落盘的，移除/关窗时删掉；拖放进来的文件属于用户，不碰
    private static void DeletePastedFile(ImageDrawReference item)
    {
        if (!item.IsPasted) return;
        try
        {
            if (File.Exists(item.Path)) File.Delete(item.Path);
        }
        catch (Exception e)
        {
            Log.Warning($"Delete pasted reference image failed: {e.Message}");
        }
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanAdd));
        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (ImageDrawReference item in Items)
        {
            item.Thumbnail.Dispose();
            DeletePastedFile(item);
        }

        Items.Clear();
    }
}
