using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 非 Windows 平台的读法：直接问 Avalonia 的剪贴板。macOS / Linux 那边本来就没有
/// OLE 延迟渲染那套东西，不需要 <see cref="WindowsClipboardTextReader"/> 的绕法
/// </summary>
public sealed class AvaloniaClipboardTextReader : IClipboardTextReader
{
    private readonly Func<IClipboard> _clipboard;

    /// <param name="clipboard">取系统剪贴板（窗口就绪后才能取，所以传的是取值函数）</param>
    public AvaloniaClipboardTextReader(Func<IClipboard> clipboard)
    {
        _clipboard = clipboard;
    }

    public Task<string?> ReadTextAsync() => _clipboard().TryGetValueAsync(DataFormat.Text);

    public void Dispose()
    {
    }
}
