using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>产物区的一行</summary>
public sealed partial class GroupArtifactItem : ObservableObject
{
    private GroupArtifact _artifact;

    /// <summary>
    /// 包一件产物
    /// </summary>
    /// <param name="artifact">产物</param>
    public GroupArtifactItem(GroupArtifact artifact)
    {
        _artifact = artifact;
    }

    /// <summary>包着的产物；增量刷新时由 <see cref="GroupArtifactListSync"/> 用展示字段比较判断盘上内容有没有变</summary>
    public GroupArtifact Artifact => _artifact;

    /// <summary>文件名</summary>
    public string Name => Path.GetFileName(_artifact.FullPath);

    /// <summary>显示路径（草稿目录 / 工作区里的相对路径）</summary>
    public string DisplayPath => _artifact.DisplayPath;

    /// <summary>绝对路径（悬停提示）</summary>
    public string FullPath => _artifact.FullPath;

    /// <summary>在草稿目录里（否则是工作区里改的）</summary>
    public bool IsDraft => _artifact.Source == EGroupArtifactSource.DraftRoom;

    /// <summary>谁写的 · 什么时候改的</summary>
    public string MetaText
    {
        get
        {
            string time = ConversationItemFactory.TimestampText(_artifact.LastWrite);
            return _artifact.Authors.Count == 0 ? time : $"{string.Join("、", _artifact.Authors)} · {time}";
        }
    }

    /// <summary>盘上这份产物变了时原位更新：实例不变 → 订阅不重建</summary>
    public void Update(GroupArtifact artifact)
    {
        _artifact = artifact;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayPath));
        OnPropertyChanged(nameof(FullPath)); //ToolTip 绑定它；形态变化时也要刷新
        OnPropertyChanged(nameof(IsDraft));
        OnPropertyChanged(nameof(MetaText));
    }

    /// <summary>用系统默认方式打开</summary>
    [RelayCommand]
    private void Open() => FileOpener.Open(_artifact.FullPath);

    /// <summary>在文件夹中显示</summary>
    [RelayCommand]
    private void Reveal() => FileOpener.RevealInFolder(_artifact.FullPath);
}