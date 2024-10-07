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

namespace UiharuMind.Utils.Clipboard;

public interface IClipboardMonitor : IDisposable
{
    event Action? OnClipboardChanged;
}