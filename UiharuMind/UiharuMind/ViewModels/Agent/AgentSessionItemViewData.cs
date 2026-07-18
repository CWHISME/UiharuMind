/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Agent;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Views;

namespace UiharuMind.ViewModels.Agent;

/// <summary>
/// Agent 会话列表条目:承载重命名/删除等条目级操作(参考 ChatSessionViewData)
/// </summary>
public partial class AgentSessionItemViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly Action<AgentSessionItemViewData> _onDeleted;

    /// <summary>会话元数据</summary>
    public AgentSessionMeta Meta { get; }

    [ObservableProperty] private string _title;

    /// <summary>更新时间文本</summary>
    public string TimeString => Meta.UpdatedAt.ToString("MM-dd HH:mm");

    public AgentSessionItemViewData(AgentSessionMeta meta, Action<AgentSessionItemViewData> onDeleted)
    {
        Meta = meta;
        _title = meta.Title;
        _onDeleted = onDeleted;
        _messageService = App.Services.GetRequiredService<IMessageService>();
    }

    [RelayCommand]
    private async Task Rename()
    {
        string? name = await UIManager.ShowStringEditWindow(Meta.Title);
        if (string.IsNullOrWhiteSpace(name) || name == Meta.Title) return;
        Meta.Title = name;
        Title = name;
        AgentSessionIndex.Instance.SaveMeta(Meta);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (!await _messageService.ConfirmAsync(Lang.DeleteTips)) return;
        AgentSessionIndex.Instance.Delete(Meta.SessionId);
        _onDeleted(this);
    }
}
