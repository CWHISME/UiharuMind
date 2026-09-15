/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Clowd.Clipboard;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.Core;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Clipboard;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Features.Clipboard;

namespace UiharuMind.Features.Clipboard;

public class ClipboardService : IDisposable
{
    private readonly Window _target;

    private IClipboard Clipboard => _target.Clipboard!;
    private readonly IClipboardMonitor? _clipboardMonitor;

    /// <summary>
    /// 历史记录。每次变更立即落盘，本类只负责调它，不再持有任何历史内容
    /// </summary>
    public ClipboardHistoryStore History { get; }

    public event Action<string>? OnClipboardStringChanged;

    // public event Action<Bitmap>? OnClipboardImageChanged;
    public event Action? OnClipboardChanged;

    public const string ImageTypePngWin = "image/png";

    public const string ImageTypePngMac = "public.png";
    public const string ImageTypeTiffMac = "public.tiff";

    private static readonly DataFormat<byte[]> ImageFormatPngWin = DataFormat.CreateBytesPlatformFormat(ImageTypePngWin);
    private static readonly DataFormat<byte[]> ImageFormatPngMac = DataFormat.CreateBytesPlatformFormat(ImageTypePngMac);
    private static readonly DataFormat<byte[]> ImageFormatTiffMac = DataFormat.CreateBytesPlatformFormat(ImageTypeTiffMac);

    // public string ImageType => PlatformUtils.IsWindows ? ImageTypePngWin : ImageTypePngMac;

    private bool _isSelfCopying;
    private string? _lastRecordedText; //上一条记进历史的文本,只用于去重

    public bool IsSelfCopying
    {
        get => _isSelfCopying;
        set => _isSelfCopying = value;
    }

    public ClipboardService(Window target)
    {
        _target = target;

        //初始化剪切板监控
        _clipboardMonitor = CreateClipboardMonitor();
        if (_clipboardMonitor != null) _clipboardMonitor.OnClipboardChanged += OnSystemClipboardChanged;

        History = new ClipboardHistoryStore(AppPaths.Data.ClipboardHistory);

        //按用户设置清理过期记录(默认关闭)。收藏项由存储层豁免
        int retentionDays = ConfigManager.Instance.Setting.ClipboardRetentionDays;
        if (retentionDays > 0) DeleteClipboardHistoryOlderThan(retentionDays);

        //检查图片目录是否存在图片，但是历史记录又没有添加的
        Task.Run(CheckAndRecordImagesInClipboardHistory);
    }

    public void CopyToClipboard(string text, bool ignoreSelfCopying = false)
    {
        // _target.Dispatcher.Invoke(() => { Clipboard.SetText(text); });
        if (ignoreSelfCopying) _isSelfCopying = true;
        try
        {
            Clipboard.SetTextAsync(text);
            OnClipboardStringChanged?.Invoke(text);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    public async Task<string?> GetFromClipboard()
    {
        return await Clipboard.TryGetValueAsync(DataFormat.Text);
    }

    /// <summary>
    /// 把图片放上系统剪贴板。
    ///
    /// <b>位图移交给剪贴板，调用方不得释放，也不要在别处共用同一张。</b>
    /// 规则 2-3 的两档（确定性释放 / 进程级缓存）都不适用于这里：Avalonia 的
    /// <c>SetBitmapAsync</c> 把位图包成 data transfer 交给平台，数据是<b>等到有人粘贴时才回头来取</b>的
    /// （<c>IClipboard.SetDataAsync</c> 原文：provides data upon request、caller must NOT dispose），
    /// 剪贴板一直持有到下次复制把它顶掉，而<b>那个时刻我们观测不到</b>——所以既不能当场释放，
    /// 也不能 await 完再释放，只能交出去不管，等剪贴板放手后由 GC 收。
    ///
    /// 这一条已经踩过两次：一次是预览窗关闭时释放了共用的那张，一次是历史项复制后当场 using 释放，
    /// 表现都是「复制静默失败、粘贴出不来」。要给别处继续用的图，先 <c>CloneBitmap</c> 一份再交过来。
    /// </summary>
    /// <param name="bitmap">要放上剪贴板的位图，<b>本方法接管</b></param>
    /// <param name="ignoreSelfCopying">是否忽略由此引发的剪贴板变化通知</param>
    public void CopyImageToClipboard(Bitmap bitmap, bool ignoreSelfCopying = false)
    {
        if (ignoreSelfCopying) _isSelfCopying = true;

        try
        {
            if (PlatformUtils.IsWindows)
            {
                //window 剪切板似乎很麻烦，直接分离实现了
#pragma warning disable CA1416
                ClipboardAvaloniaCustom.SetImage(bitmap);
#pragma warning restore CA1416
            }
            else
            {
                Clipboard.SetBitmapAsync(bitmap);
            }

            // OnClipboardImageChanged?.Invoke(bitmap);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    public async Task<Bitmap?> GetImageFromClipboard()
    {
        if (PlatformUtils.IsWindows)
        {
#pragma warning disable CA1416
            return await ClipboardAvaloniaCustom.GetImageAsync().ConfigureAwait(false);
#pragma warning restore CA1416
        }

        try
        {
            var bitmap = await Clipboard.TryGetBitmapAsync();
            if (bitmap != null) return bitmap;

            foreach (var format in new[] { ImageFormatPngWin, ImageFormatPngMac, ImageFormatTiffMac })
            {
                var data = await Clipboard.TryGetValueAsync(format);
                if (data is { Length: > 0 })
                {
                    using var stream = new MemoryStream(data);
                    return new Bitmap(stream);
                }
            }

            // var formats = await Clipboard.GetDataFormatsAsync();
            Log.Warning($"No image found in clipboard.");
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        return null;
    }

    /// <summary>
    /// 清除剪切板历史记录
    /// </summary>
    public void ClearClipboardHistory()
    {
        DeleteImageFiles(History.Clear());
        OnClipboardChanged?.Invoke();
    }

    /// <summary>
    /// 将指定记录移动至第一个。只改排序键，不动它的时间
    /// </summary>
    /// <param name="id">记录主键</param>
    public void MoveClipboardHistoryItemFirst(long id)
    {
        History.MoveToFront(id);
        OnClipboardChanged?.Invoke();
    }

    /// <summary>
    /// 删除指定记录
    /// </summary>
    /// <param name="ids">记录主键</param>
    public void DeleteClipboardHistoryItems(IReadOnlyCollection<long> ids)
    {
        DeleteImageFiles(History.Delete(ids));
        OnClipboardChanged?.Invoke();
    }

    /// <summary>
    /// 清理指定天数之前的非收藏记录
    /// </summary>
    /// <param name="days">保留天数</param>
    public void DeleteClipboardHistoryOlderThan(int days)
    {
        DeleteImageFiles(History.DeleteOlderThan(DateTime.Now.AddDays(-days)));
        OnClipboardChanged?.Invoke();
    }

    // 图片文件必须在记录落盘之后才删。反过来一旦崩在中间,
    // 历史里就留下一条指向不存在文件的记录,而那个反过来没人能修
    private static void DeleteImageFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Log.Warning($"Delete clipboard image failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 将图片记录至剪切板历史
    /// </summary>
    /// <param name="bitmap"></param>
    /// <param name="fileName"></param>
    public void RecordImageToHistory(Bitmap? bitmap, string? fileName = null)
    {
        if (bitmap == null) return;
        string fullPath = Path.Combine(AppPaths.Data.ClipboardImages,
            fileName ?? $"Uiharu_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        string? dir = Path.GetDirectoryName(fullPath);
        if (dir == null) return;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        if (!File.Exists(fullPath)) bitmap.Save(fullPath, PngBitmapEncoderOptions.Default);
        History.AddImage(fullPath);
        Dispatcher.UIThread.Post(() =>
        {
            // OnClipboardImageChanged?.Invoke(bitmap);
            OnClipboardChanged?.Invoke();
        });
    }

    /// <summary>
    /// 检查真实图片文件，并将未记录的图片记录至剪切板历史
    /// </summary>
    private void CheckAndRecordImagesInClipboardHistory()
    {
        if(!Directory.Exists(AppPaths.Data.ClipboardImages)) return;
        var files = Directory.GetFiles(AppPaths.Data.ClipboardImages, "*.png");
        bool recorded = false;
        foreach (var file in files)
        {
            // 补记只写一条记录,不再把 PNG 解码成 Bitmap 再原样存回去——
            // 那一趟解码+编码纯属白做,启动时有几十张图就卡几十次
            if (History.AddImageIfMissing(file) != null) recorded = true;
        }

        if (recorded) Dispatcher.UIThread.Post(() => OnClipboardChanged?.Invoke());
    }

    private void OnSystemClipboardChanged()
    {
        if (_isSelfCopying)
        {
            _isSelfCopying = false;
            return;
        }

        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                await Task.Delay(100);
                var clipboardContent = await GetFromClipboard();
                if (string.IsNullOrEmpty(clipboardContent))
                {
                    //是图片吗
                    // var image = await GetImageFromClipboard();
                    // if (image != null) OnClipboardImageChanged?.Invoke(image);
                    return;
                }

                //排除一下相同项。比的是内存里记着的上一条,不回查数据库:
                //系统剪贴板每变一次就查一次全文,纯属浪费
                if (string.Equals(_lastRecordedText, clipboardContent, StringComparison.Ordinal)) return;
                _lastRecordedText = clipboardContent;
                History.AddText(clipboardContent);
                OnClipboardStringChanged?.Invoke(clipboardContent);
                OnClipboardChanged?.Invoke();
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
            }
        });
    }

    public void Dispose()
    {
        // 没有「退出时保存」这一步了:每次变更当场就已落盘,
        // 调试模式直接终止进程也不会丢——那正是旧实现丢一大截的原因
        _clipboardMonitor?.Dispose();
        History.Dispose();
    }


    //==========================================================

    IClipboardMonitor? CreateClipboardMonitor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacClipboardMonitor(500);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsClipboardMonitor();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxClipboardMonitor(() => GetFromClipboard(), 1000);
        }

        Log.Error("This platform is not supported for clipboard monitoring.");
        return null;
    }
}