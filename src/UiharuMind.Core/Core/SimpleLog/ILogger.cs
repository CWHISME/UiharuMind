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

namespace UiharuMind.Core.Core.SimpleLog;

public interface ILogger
{
    public void Debug(string rawMessage, LogItem message);
    public void Warning(string rawMessage, LogItem message);

    public void Error(string rawMessage, LogItem message);
}