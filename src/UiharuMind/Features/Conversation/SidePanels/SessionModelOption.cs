using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>钉选名落到哪一类条目</summary>
public enum SessionModelSelectionKind
{
    Default,
    Model,
    Missing,
}

/// <summary>本会话模型下拉的一项：默认哨兵、具体模型，或已不在列表里的保留钉选</summary>
public partial class SessionModelOption : ObservableObject
{
    public bool IsDefault { get; init; }
    public bool IsUnavailable { get; init; }
    public string? ModelName { get; set; }
    public bool IsRemoteModel { get; set; }
    public bool IsVisionModel { get; set; }

    [ObservableProperty] private string _displayName = string.Empty;

    /// <summary>
    /// 模型下拉的一份快照：默认哨兵在首位，后接当前模型清单。给模态的短命界面用——
    /// 模型列表在那几秒里变了的概率和代价都不值得挂订阅
    /// </summary>
    /// <param name="defaultLabel">默认哨兵的显示名（如「跟随全局」）</param>
    /// <returns>选项，默认在首位</returns>
    public static List<SessionModelOption> SnapshotWithDefault(string defaultLabel)
    {
        List<SessionModelOption> options = [new SessionModelOption { IsDefault = true, DisplayName = defaultLabel }];
        if (App.ModelService?.ModelSources is not { } sources) return options;

        foreach (ModelRunningData model in sources)
        {
            options.Add(new SessionModelOption
            {
                ModelName = model.ModelName,
                DisplayName = model.ModelName,
                IsRemoteModel = model.IsRemoteModel,
                IsVisionModel = model.IsVisionModel,
            });
        }

        return options;
    }

    /// <summary>
    /// 钉选名对应哪类条目：空=默认，列表里有=模型，没有=保留的缺失条。
    /// 纯函数，不碰任何单例，可单测。
    /// </summary>
    /// <param name="pinnedName">会话覆写名，空表示默认</param>
    /// <param name="availableNames">当前列表里的模型名</param>
    public static SessionModelSelectionKind ResolveSelection(string? pinnedName, IEnumerable<string> availableNames)
    {
        if (string.IsNullOrEmpty(pinnedName)) return SessionModelSelectionKind.Default;
        foreach (string name in availableNames)
        {
            if (name == pinnedName) return SessionModelSelectionKind.Model;
        }

        return SessionModelSelectionKind.Missing;
    }
}
