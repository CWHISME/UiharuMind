using System;

namespace UiharuMind.Utils.Clipboard;

public interface IClipboardMonitor : IDisposable
{
    event Action? OnClipboardChanged;
}