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

    public Task SetTextAsync(string text) => _clipboard.SetTextAsync(text);

    public Task RestoreAsync(DataTransfer? saved) =>
        saved != null ? _clipboard.SetDataAsync(saved) : _clipboard.ClearAsync();

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
