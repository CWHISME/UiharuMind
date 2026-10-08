using System;
using System.Threading.Tasks;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 听写去向：当时在前台的别的应用（粘贴进去）。本应用自己的窗口在前台时不算——那时该送本应用的输入框
/// </summary>
public sealed class ForegroundAppDictationTarget : IDictationTarget
{
    private readonly Func<bool> _isOwnAppActive;
    private readonly Func<string, Task> _paste;

    /// <param name="isOwnAppActive">本应用有窗口在前台</param>
    /// <param name="paste">把文字粘贴进前台应用</param>
    public ForegroundAppDictationTarget(Func<bool> isOwnAppActive, Func<string, Task> paste)
    {
        _isOwnAppActive = isOwnAppActive;
        _paste = paste;
    }

    public bool IsAvailable => !_isOwnAppActive();

    public void Insert(string text) => _ = PasteAsync(text);

    private async Task PasteAsync(string text)
    {
        try
        {
            await _paste(text);
        }
        catch (Exception e)
        {
            Log.Warning($"Paste dictation into foreground app failed: {e.Message}");
        }
    }
}
