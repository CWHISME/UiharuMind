using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Clowd.Clipboard;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Utils;
using UiharuMind.Utils.Clipboard;

namespace UiharuMind.Services;

public class ClipboardService : IDisposable
{
    private readonly Window _target;

    private IClipboard Clipboard => _target.Clipboard!;
    private readonly IClipboardMonitor? _clipboardMonitor;

    /// <summary>
    /// 历史记录
    /// </summary>
    public ObservableCollection<ClipboardItem> ClipboardHistoryItems { get; }

    public Action<string>? OnClipboardStringChanged;

    public const string ImageTypePngWin = "image/png";

    public const string ImageTypePngMac = "public.png";

    // public string ImageType => PlatformUtils.IsWindows ? ImageTypePngWin : ImageTypePngMac;
    public const string HistoryFileName = "clipboard_history.json";

    private Timer _timer;
    private bool _isHistoryDirty;

    public ClipboardService(Window target)
    {
        _target = target;

        //初始化剪切板监控
        _clipboardMonitor = CreateClipboardMonitor();
        if (_clipboardMonitor != null) _clipboardMonitor.OnClipboardChanged += OnSystemClipboardChanged;

        ClipboardHistoryItems = SaveUtility.Load<ObservableCollection<ClipboardItem>>(HistoryFileName);

        //初始化定时器，每隔100秒检测保存一次历史记录
        _timer = new Timer(OnTimerElapsed, null, TimeSpan.Zero, TimeSpan.FromSeconds(100));
    }

    public void CopyToClipboard(string text)
    {
        // _target.Dispatcher.Invoke(() => { Clipboard.SetText(text); });
        Clipboard.SetTextAsync(text);
    }

    public async Task<string?> GetFromClipboard()
    {
        return await Clipboard.GetTextAsync();
    }

    public void CopyImageToClipboard(Bitmap bitmap)
    {
        if (PlatformUtils.IsWindows)
        {
            //window 剪切板似乎很麻烦，直接分离实现了
#pragma warning disable CA1416
            ClipboardAvaloniaCustom.SetImage(bitmap);
#pragma warning restore CA1416
            return;
        }

        var dataObject = new DataObject();
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream);
        memoryStream.Position = 0;
        var bytes = memoryStream.ToArray();
        dataObject.Set(ImageTypePngMac, bytes);

        Clipboard.SetDataObjectAsync(dataObject);
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
            var types = await Clipboard.GetFormatsAsync();
            foreach (var type in types)
            {
                if (type is ImageTypePngWin or ImageTypePngMac)
                {
                    var data = await Clipboard.GetDataAsync(type);
                    if (data is byte[] pngBytes)
                    {
                        using var stream = new MemoryStream(pngBytes);
                        return new Bitmap(stream);
                    }
                }
            }

            Log.Warning("No PNG image found in clipboard.");
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        return null;
    }

    private async void OnSystemClipboardChanged()
    {
        var clipboardContent = await Clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(clipboardContent)) return;
        //简单对比排除一下相同项
        if (ClipboardHistoryItems.Count > 0 && clipboardContent.Length == ClipboardHistoryItems[0].Text.Length &&
            clipboardContent[0] == ClipboardHistoryItems[0].Text[0]) return;
        ClipboardHistoryItems.Insert(0, new ClipboardItem(clipboardContent));
        OnClipboardStringChanged?.Invoke(clipboardContent);
        _isHistoryDirty = true;
    }

    private void OnTimerElapsed(object? state)
    {
        SaveUtility.Save(HistoryFileName, ClipboardHistoryItems);
        _isHistoryDirty = false;
    }

    public void Dispose()
    {
        SaveUtility.Save(HistoryFileName, ClipboardHistoryItems);
        _clipboardMonitor?.Dispose();
        _timer.Dispose();
    }


    //==========================================================

    static IClipboardMonitor? CreateClipboardMonitor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacClipboardMonitor(500);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsClipboardMonitor();
        }

        Log.Error("This platform is not supported for clipboard monitoring.");
        return null;
    }
}