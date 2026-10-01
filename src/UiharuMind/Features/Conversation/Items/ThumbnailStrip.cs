using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 一张缩略图的来源：盘上的文件，或内存里的字节（消息里内联的图）。原图点开时从这里现解
/// </summary>
/// <param name="FilePath">文件绝对路径；字节来源为 null</param>
/// <param name="Bytes">图片字节；文件来源为空</param>
public readonly record struct ImageThumbnailSource(string? FilePath, ReadOnlyMemory<byte> Bytes)
{
    /// <summary>来自文件</summary>
    /// <param name="path">绝对路径</param>
    /// <returns>来源</returns>
    public static ImageThumbnailSource FromFile(string path) => new(path, ReadOnlyMemory<byte>.Empty);

    /// <summary>来自字节</summary>
    /// <param name="bytes">图片字节</param>
    /// <returns>来源</returns>
    public static ImageThumbnailSource FromBytes(ReadOnlyMemory<byte> bytes) => new(null, bytes);
}

/// <summary>
/// 条目上常驻的一张缩略图。只攥缩略图那几百 KB，不攥原图解码后的十来 MB（ADR 0041 的内存账）
/// </summary>
public sealed class ImageThumbnail
{
    private readonly ImageThumbnailSource _source;

    /// <param name="source">来源</param>
    /// <param name="bitmap">已解好的缩略图</param>
    public ImageThumbnail(ImageThumbnailSource source, Bitmap bitmap)
    {
        _source = source;
        Bitmap = bitmap;
    }

    /// <summary>缩略图</summary>
    public Bitmap Bitmap { get; }

    /// <summary>文件来源的路径；字节来源为 null</summary>
    public string? FilePath => _source.FilePath;

    /// <summary>
    /// 现解一份原尺寸的图（点开预览用）。调用方拥有它，用完负责释放
    /// </summary>
    /// <returns>原图</returns>
    public Bitmap DecodeFull()
    {
        if (_source.FilePath != null) return new Bitmap(_source.FilePath);
        using MemoryStream stream = new(_source.Bytes.ToArray());
        return new Bitmap(stream);
    }
}

/// <summary>
/// 条目上的一组缩略图。解码放到线程池（切回一个图多的会话时逐张同步解码会卡住界面线程），
/// 解完回界面线程按来源顺序挂上；条目在解完前被裁掉（<see cref="Release"/>）时，晚到的结果当场释放、不再挂。
/// 解不了的那张静默跳过。组合进各条目使用，须在界面线程调用
/// </summary>
public sealed class ThumbnailStrip
{
    private int _generation; //每释放一次加一：晚到的解码结果据此认出条目已被裁掉
    private bool _loading; //解码在路上，防同一组重复起解

    /// <summary>已挂上的缩略图，按来源顺序</summary>
    public ObservableCollection<ImageThumbnail> Items { get; } = [];

    /// <summary>
    /// 后台解一组缩略图并挂上。已有图或正在解时什么都不做
    /// </summary>
    /// <param name="sources">来源，按显示顺序</param>
    /// <param name="decodeWidth">缩略图解码宽度（像素）；比它窄的图按原尺寸解，不放大</param>
    /// <returns>挂上（或因已被释放而丢弃）时完成；宿主在这之后刷新自己的「有没有图」</returns>
    public async Task LoadAsync(IReadOnlyList<ImageThumbnailSource> sources, int decodeWidth)
    {
        if (sources.Count == 0 || _loading || Items.Count > 0) return;

        int generation = _generation;
        _loading = true;
        List<ImageThumbnail> decoded = await Task.Run(() => sources
                .Select(source => TryDecode(source, decodeWidth))
                .OfType<ImageThumbnail>()
                .ToList())
            .ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _loading = false;
            if (generation != _generation)
            {
                foreach (ImageThumbnail thumbnail in decoded) thumbnail.Bitmap.Dispose();
                return;
            }

            foreach (ImageThumbnail thumbnail in decoded) Items.Add(thumbnail);
        });
    }

    /// <summary>
    /// 释放全部缩略图；路上还没挂的那批到了也直接释放
    /// </summary>
    public void Release()
    {
        _generation++;
        // 先摘绑定再释放，顺序反了就是把还在界面上的位图放掉
        ImageThumbnail[] stale = Items.ToArray();
        Items.Clear();
        foreach (ImageThumbnail thumbnail in stale) thumbnail.Bitmap.Dispose();
    }

    private static ImageThumbnail? TryDecode(ImageThumbnailSource source, int decodeWidth)
    {
        try
        {
            byte[] bytes = source.FilePath != null
                ? File.Exists(source.FilePath) ? File.ReadAllBytes(source.FilePath) : []
                : source.Bytes.ToArray();
            if (bytes.Length == 0) return null;

            using MemoryStream stream = new(bytes);
            Bitmap bitmap = ImageFormats.TryReadSize(bytes, out int width, out _) && width <= decodeWidth
                ? new Bitmap(stream)
                : Bitmap.DecodeToWidth(stream, decodeWidth);
            return new ImageThumbnail(source, bitmap);
        }
        catch (Exception e)
        {
            Log.Warning($"Load thumbnail failed '{source.FilePath ?? "(inline)"}': {e.Message}");
            return null;
        }
    }
}
