using System;
using System.Threading.Tasks;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 读剪贴板里的文本。
///
/// <para>
/// 单独一个接口，是因为「听变化」和「读内容」是两件事：监听器只该发通知，
/// 读取在各平台的做法完全不同（Windows 要绕开 OLE 延迟渲染、其余平台走 Avalonia），
/// 混在 <see cref="IClipboardMonitor"/> 里就只能靠 <c>is</c> 判具体类型。
/// </para>
/// </summary>
public interface IClipboardTextReader : IDisposable
{
    /// <summary>
    /// 读剪贴板文本
    /// </summary>
    /// <returns>文本；剪贴板上没有文本、或这一次读不到时为 null</returns>
    Task<string?> ReadTextAsync();
}
