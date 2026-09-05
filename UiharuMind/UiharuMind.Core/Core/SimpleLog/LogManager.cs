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

using System.Text.Json;

namespace UiharuMind.Core.Core.SimpleLog;

public class LogManager
{
    private const int MaxItems = 5000; //日志保留上限
    private const int TrimBatch = 1000; //超限后一次裁掉的条数,摊薄 RemoveRange 的搬移开销

    private static LogManager? _instance;
    private static readonly object Locker = new object();
    private static SpinLock _spinLocker = new SpinLock();

    public static LogManager Instance
    {
        get
        {
            if (_instance == null)
                lock (Locker)
                {
                    if (_instance == null) _instance = new LogManager();
                    return _instance;
                }

            return _instance;
        }
    }

    private readonly List<LogItem> _logItems = new List<LogItem>(64);

    /// <summary>
    /// 日志改变事件。在锁外触发,来自哪个线程取决于打日志的线程,订阅方需自行派发到所需线程
    /// </summary>
    public event Action<LogItem>? OnLogChange;

    public ILogger? Logger;

    // 入表必须独立于"有没有装 ILogger":写成 Logger?.Debug(str, AddLog(...)) 时,
    // null 条件运算符会连实参一起跳过——Logger 未装好之前的日志一条都不会进列表
    public void Log(string str)
    {
        LogItem item = AddLog(ELogType.Log, str);
        Logger?.Debug(str, item);
    }

    public void LogWarning(string str)
    {
        LogItem item = AddLog(ELogType.Warning, str);
        Logger?.Warning(str, item);
    }

    public void LogError(string str)
    {
        LogItem item = AddLog(ELogType.Error, str);
        Logger?.Error(str, item);
    }

    /// <summary>
    /// 取当前日志的快照
    /// </summary>
    /// <returns>独立副本,调用方可自由遍历,不会与写入线程竞争</returns>
    public List<LogItem> GetSnapshot()
    {
        bool islock = false;
        try
        {
            _spinLocker.Enter(ref islock);
            return new List<LogItem>(_logItems);
        }
        finally
        {
            if (islock) _spinLocker.Exit();
        }
    }

    public void SaveLog(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        SaveUtility.Save();
        string logPath = Path.Combine(path, "Log.txt");
        if (File.Exists(logPath))
        {
            File.Move(logPath, Path.Combine(path, "LastLog.txt"), true);
        }

        File.WriteAllText(logPath,
            JsonSerializer.Serialize(GetSnapshot(), new JsonSerializerOptions() { WriteIndented = true }));
    }

    public void ClearLog()
    {
        bool islock = false;
        try
        {
            _spinLocker.Enter(ref islock);
            _logItems.Clear();
        }
        finally
        {
            if (islock) _spinLocker.Exit();
        }

        OnLogChange?.Invoke(new LogItem(ELogType.Log, "Clear Log!"));
    }

    private LogItem AddLog(ELogType type, string str)
    {
        LogItem item = new LogItem(type, str);

        bool islock = false;
        try
        {
            _spinLocker.Enter(ref islock);
            _logItems.Add(item);
            if (_logItems.Count > MaxItems) _logItems.RemoveRange(0, TrimBatch);
        }
        finally
        {
            if (islock) _spinLocker.Exit();
        }

        // 事件必须在锁外触发:订阅方会同步改动 UI 绑定集合并触发布局,
        // 在锁内做会让其他打日志的线程在 SpinLock 上空转等待整个 UI 过程
        OnLogChange?.Invoke(item);
        return item;
    }
}
