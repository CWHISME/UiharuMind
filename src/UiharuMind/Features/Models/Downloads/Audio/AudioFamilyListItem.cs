using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 语音模型列表里的一个家族
/// </summary>
/// <param name="family">家族</param>
public partial class AudioFamilyListItem(AudioCppFamily family) : ObservableObject
{
    [ObservableProperty] private bool _isInstalled;

    /// <summary>
    /// 家族
    /// </summary>
    public AudioCppFamily Family { get; } = family;

    public string Title => Family.DisplayName;

    public bool IsRecommended => Family.IsRecommended;

    public string? MaturityText => AudioModelText.MaturityText(Family.Status);

    /// <summary>
    /// 第二行：合成 · 5 个版本
    /// </summary>
    public string SubText => Loc.Text(LangKey.AudioFamilySummary, AudioModelText.TaskText(Family.Task), Family.Packages.Count);

    /// <summary>
    /// 是否匹配搜索词（名称、id 或语种）
    /// </summary>
    /// <param name="query">搜索词，已去空白</param>
    /// <returns>匹配为 true</returns>
    public bool Matches(string query) =>
        query.Length == 0 ||
        Family.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Family.Family.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Family.Languages.Contains(query, StringComparer.OrdinalIgnoreCase);
}
