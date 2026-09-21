/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Generated;
using UiharuMind.Shared.Collections;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.LogViewer;

/// <summary>
/// 日志列表。持有的是<b>索引项</b>而不是正文——内存占用只随条数增长，
/// 与正文体量无关。正文在点开某条时才从磁盘读回。
/// <para>最新的在最顶上，靠 <see cref="ReversedObservableList{T}"/> 倒着读，不是 <c>Insert(0)</c>。</para>
/// </summary>
public partial class LogViewModel : ViewModelBase
{
    private const int MaxViewItems = 50_000; //与 LogStore 的索引上限对齐
    private const int TrimBatch = 5_000;

    /// <summary>正文已被滚动淘汰时的占位文本</summary>
    private static string DeadBodyHint => Loc.Text(LangKey.LogBodyEvicted);

    /// <summary>倒序视图，绑给列表</summary>
    public ReversedObservableList<LogIndexEntry> Items { get; } = new();

    [ObservableProperty] private LogIndexEntry? _selectedEntry;

    [ObservableProperty] private string _detailText = string.Empty;

    /// <summary>分类筛选：0 全部 / 1 通用 / 2 请求 / 3 响应</summary>
    [ObservableProperty] private int _categoryFilterIndex;

    /// <summary>
    /// 最低等级，序号即 <see cref="ELogType"/>。改动会写回配置——
    /// 它同时决定<b>面板显示</b>与<b>哪些日志会被记下来</b>，不是纯视图状态
    /// </summary>
    [ObservableProperty] private int _minLevelIndex = (int)ConfigManager.Instance.DebugSetting.LogTypeInfo;

    public LogViewModel()
    {
        Rebuild();
        LogManager.Instance.OnLogAppended += OnLogAppended;
    }

    partial void OnCategoryFilterIndexChanged(int value) => Rebuild();

    partial void OnMinLevelIndexChanged(int value)
    {
        ConfigManager.Instance.DebugSetting.LogTypeInfo = (ELogType)value;
        ConfigManager.Instance.DebugSetting.Save();
        Rebuild();
    }

    partial void OnSelectedEntryChanged(LogIndexEntry? value)
    {
        if (value == null)
        {
            DetailText = string.Empty;
            return;
        }

        // 可能是几 MB 的正文,读盘不占 UI 线程
        _ = LoadDetailAsync(value);
    }

    /// <summary>打开日志目录。<b>整个目录</b>才是完整的一份——外置正文在旁边的 Bodies.txt 里</summary>
    [RelayCommand]
    private void OpenFolder() => App.FilesService.OpenFolder(LogManager.Instance.Directory);

    /// <summary>清空</summary>
    [RelayCommand]
    private void Clear()
    {
        LogManager.Instance.ClearLog();
        Items.Reset([]);
        SelectedEntry = null;
    }

    /// <summary>
    /// 按当前筛选导出成一份纯文本。用户因此可以只发出错的那几条，
    /// 而不必把含提示词的整份日志交出去
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        List<LogIndexEntry> entries = Items.ToAppendOrderList();
        string path = Path.Combine(LogManager.Instance.Directory,
            $"Export-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        await Task.Run(() =>
        {
            StringBuilder sb = new();
            foreach (LogIndexEntry entry in entries)
            {
                sb.Append('[').Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append("][")
                    .Append(entry.LogType).Append("][").Append(entry.Category).AppendLine("]");
                sb.AppendLine(LogManager.Instance.ReadText(entry) ?? DeadBodyHint).AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
        });

        App.FilesService.OpenFolder(LogManager.Instance.Directory);
    }

    private async Task LoadDetailAsync(LogIndexEntry entry)
    {
        string text = await Task.Run(() => LogManager.Instance.ReadText(entry) ?? DeadBodyHint);
        if (SelectedEntry == entry) DetailText = text; //读盘期间用户可能已经换选了
    }

    // 切换筛选条件时整体重建。回填只取索引快照,不碰正文
    private void Rebuild()
    {
        List<LogIndexEntry> matched = new();
        foreach (LogIndexEntry entry in LogManager.Instance.GetSnapshot())
        {
            if (Matches(entry)) matched.Add(entry);
        }

        if (matched.Count > MaxViewItems) matched.RemoveRange(0, matched.Count - MaxViewItems);
        Items.Reset(matched);
    }

    private void OnLogAppended(LogIndexEntry entry)
    {
        if (!Matches(entry)) return;

        // 事件来自后台写入线程,改动绑定集合必须回到 UI 线程
        if (Dispatcher.UIThread.CheckAccess()) Append(entry);
        else Dispatcher.UIThread.Post(() => Append(entry));
    }

    private void Append(LogIndexEntry entry)
    {
        Items.Append(entry);
        if (Items.Count > MaxViewItems) Items.TrimOldest(TrimBatch);
    }

    private bool Matches(LogIndexEntry entry)
    {
        if (ConfigManager.Instance.DebugSetting.LogTypeInfo > entry.LogType) return false;
        return CategoryFilterIndex switch
        {
            1 => entry.Category == ELogCategory.General,
            2 => entry.Category == ELogCategory.LlmRequest,
            3 => entry.Category == ELogCategory.LlmResponse,
            _ => true,
        };
    }
}
