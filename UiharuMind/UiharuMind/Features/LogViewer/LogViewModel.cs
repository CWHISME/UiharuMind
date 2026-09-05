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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.LogViewer;

/// <summary>
/// 日志列表。条目数有上限——列表常驻视觉树,无上限的话进程活多久它就长多久,
/// 每条日志都在里面留一个容器
/// </summary>
public class LogViewModel : ViewModelBase
{
    private const int MaxDisplayItems = 2000; //列表展示上限,超出后丢弃最早的

    public ObservableCollection<LogItem> Items { get; } = new();

    public LogViewModel()
    {
        Backfill();
        LogManager.Instance.OnLogChange += OnLogChange;
    }

    /// 回填只取最近一批。原先是在 Task.Run 里遍历 LogManager 的内部列表再逐条灌进来:
    /// 既跨线程改动了 UI 绑定集合,又会与写入线程撞在同一个 List 上
    private void Backfill()
    {
        ELogType level = ConfigManager.Instance.DebugSetting.LogTypeInfo;
        List<LogItem> snapshot = LogManager.Instance.GetSnapshot();

        // 从尾部往前收集最近 MaxDisplayItems 条,再正序灌入
        List<LogItem> matched = new List<LogItem>();
        for (int i = snapshot.Count - 1; i >= 0 && matched.Count < MaxDisplayItems; i--)
        {
            if (level <= snapshot[i].LogType) matched.Add(snapshot[i]);
        }

        for (int i = matched.Count - 1; i >= 0; i--) Items.Add(matched[i]);
    }

    private void OnLogChange(LogItem obj)
    {
        if (ConfigManager.Instance.DebugSetting.LogTypeInfo > obj.LogType) return;

        // 事件来自打日志的那个线程,改动绑定集合必须回到 UI 线程
        if (Dispatcher.UIThread.CheckAccess()) Append(obj);
        else Dispatcher.UIThread.Post(() => Append(obj));
    }

    private void Append(LogItem item)
    {
        Items.Add(item);
        if (Items.Count > MaxDisplayItems) Items.RemoveAt(0);
    }
}
