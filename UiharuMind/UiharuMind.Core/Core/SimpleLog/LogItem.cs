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

public class LogItem
{
    private string _logStr;

    public string LogString
    {
        get { return _logStr; }
    }

    private ELogType _logType;

    public ELogType LogType
    {
        get { return _logType; }
    }

    public LogItem(ELogType type, string str)
    {
        _logStr = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss]") + str;
        _logType = type;
    }

    public override string ToString()
    {
        return LogString;
    }
}