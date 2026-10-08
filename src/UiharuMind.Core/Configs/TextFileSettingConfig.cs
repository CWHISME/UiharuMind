/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.CompilerServices;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 文本文件编辑窗（TextFileWindow）的全局偏好。独立于主设置：它是独立功能，
/// 自己的开关只服务自己，不掺进 SettingConfig。改一处、存盘、所有文本窗生效——
/// 所以开关都要显式通知，自动属性改了别的窗口不会知道。存盘由改开关的地方调 <see cref="ConfigBase.Save"/>。
/// </summary>
public class TextFileSettingConfig : TConfigBase<TextFileSettingConfig>
{
    private bool _wordWrap = true;
    private bool _codeLineNumbers = true;
    private bool _textLineNumbers;
    private bool _syntaxHighlight = true;

    [SettingConfigDesc("Text file editor: wrap long lines by default.",
        "文本文件编辑窗：默认自动换行。")]
    public bool WordWrap
    {
        get => _wordWrap;
        set => SetField(ref _wordWrap, value);
    }

    [SettingConfigDesc("Text file editor: show line numbers for code files.",
        "文本文件编辑窗：代码文件显示行号。")]
    public bool CodeLineNumbers
    {
        get => _codeLineNumbers;
        set => SetField(ref _codeLineNumbers, value);
    }

    [SettingConfigDesc("Text file editor: show line numbers for prose files (Markdown, plain text).",
        "文本文件编辑窗：正文文件（Markdown、纯文本）显示行号。")]
    public bool TextLineNumbers
    {
        get => _textLineNumbers;
        set => SetField(ref _textLineNumbers, value);
    }

    [SettingConfigDesc("Text file editor: enable syntax highlighting by default.",
        "文本文件编辑窗：默认启用语法高亮。")]
    public bool SyntaxHighlight
    {
        get => _syntaxHighlight;
        set => SetField(ref _syntaxHighlight, value);
    }

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

    // 只通知不存盘：读配置时反序列化也走 setter，在这里存盘会把读了一半的配置写回去
    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
    }
}