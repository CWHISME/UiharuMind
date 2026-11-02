using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 系统剪贴板上的 <see cref="IPasteboard"/>：逐项逐格式取原始数据照样放回，取不到的格式（如延迟提供、已失效的）跳过
/// </summary>
public sealed class AvaloniaPasteboard : IPasteboard
{
    private readonly IClipboard _clipboard;

    /// <param name="clipboard">系统剪贴板</param>
    public AvaloniaPasteboard(IClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public async Task<DataTransfer?> SaveAsync()
    {
        try
        {
            using IAsyncDataTransfer? current = await _clipboard.TryGetDataAsync();
            if (current == null) return null;
            DataTransfer copy = new();
            foreach (IAsyncDataTransferItem item in current.Items)
            {
                DataTransferItem saved = new();
                bool any = false;
                foreach (DataFormat format in item.Formats)
                    any |= Keep(saved, format, await item.TryGetRawAsync(format));
                if (any) copy.Add(saved);
            }

            return copy.Items.Count > 0 ? copy : null;
        }
        catch (Exception e)
        {
            Log.Warning($"Snapshot clipboard failed: {e.Message}");
            return null;
        }
    }

    public async Task SetTextAsync(string text)
    {
        await _clipboard.SetTextAsync(text);
        await FlushAsync();
    }

    public async Task RestoreAsync(DataTransfer? saved)
    {
        if (saved != null) await _clipboard.SetDataAsync(saved);
        else await _clipboard.ClearAsync();
        await FlushAsync();
    }

    /// <summary>
    /// 放上去就得 flush。不 flush 的话数据挂在我们自己的托管对象上延迟渲染，前台应用那一下粘贴要回调我们的
    /// UI 线程——借用的文字和还原后的内容都可能粘不出来，进程一退更是直接丢
    /// </summary>
    private async Task FlushAsync()
    {
        try
        {
            await _clipboard.FlushAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"Flush clipboard failed: {e.Message}");
        }
    }

    private static bool Keep(DataTransferItem item, DataFormat format, object? raw)
    {
        switch (format, raw)
        {
            case (DataFormat<string> typed, string value):
                item.Set(typed, value);
                return true;
            case (DataFormat<byte[]> typed, byte[] value):
                item.Set(typed, value);
                return true;
            case (DataFormat<IStorageItem> typed, IStorageItem value):
                item.Set(typed, value);
                return true;
            case (DataFormat<Bitmap> typed, Bitmap value):
                item.Set(typed, value);
                return true;
            default:
                return false;
        }
    }
}
