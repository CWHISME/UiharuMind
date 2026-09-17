/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 文本文件编辑窗（TextFileWindow）的全局偏好。独立于主设置：它是独立功能，
/// 自己的开关只服务自己，不掺进 SettingConfig。改一处、存盘、所有文本窗生效。
/// </summary>
public class TextFileSettingConfig : TConfigBase<TextFileSettingConfig>
{
    [SettingConfigDesc("Text file editor: wrap long lines by default.",
        "文本文件编辑窗：默认自动换行。")]
    public bool WordWrap { get; set; } = true;

    [SettingConfigDesc("Text file editor: show line numbers by default.",
        "文本文件编辑窗：默认显示行号。")]
    public bool ShowLineNumbers { get; set; }

    [SettingConfigDesc("Text file editor: enable syntax highlighting by default.",
        "文本文件编辑窗：默认启用语法高亮。")]
    public bool SyntaxHighlight { get; set; } = true;

    /// <summary>
    /// 最近打开过的文件(最新在前)。「文件 / 最近打开」菜单的数据源，
    /// 打开成功与另存为落定后记一笔。列表操作见 <see cref="RecentPathList"/>。
    /// </summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary><see cref="RecentFiles"/> 的条数上限</summary>
    public const int RecentFilesLimit = 10;

    private RecentPathList? _recentFiles;
    private RecentPathList RecentHistory =>
        _recentFiles ??= new RecentPathList(RecentFiles, RecentFilesLimit);

    /// <summary>
    /// 把一个文件记为最近打开，不存在则忽略
    /// </summary>
    /// <param name="path">文件绝对路径</param>
    public void RememberFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (RecentHistory.Remember(path)) Save();
    }

    /// <summary>移除一条最近文件记录</summary>
    public void ForgetFile(string? path)
    {
        if (RecentHistory.Forget(path)) Save();
    }

    /// <summary>剔除已不存在的文件（菜单每次展开时顺手做一次）</summary>
    public void PruneMissingFiles()
    {
        if (RecentHistory.PruneMissing()) Save();
    }

    /// <summary>清空最近文件记录</summary>
    public void ClearRecentFiles()
    {
        if (RecentHistory.Clear()) Save();
    }
}