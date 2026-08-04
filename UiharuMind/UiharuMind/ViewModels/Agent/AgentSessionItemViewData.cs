/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using UiharuMind.Core.AI.Chat;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.ViewModels.Agent;

/// <summary>
/// Agent 会话列表条目:承载重命名/删除等条目级操作(聊天页对应 ChatSessionItemViewData)
/// </summary>
public partial class AgentSessionItemViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly Action<AgentSessionItemViewData> _onDeleted;

    /// <summary>会话元数据</summary>
    public ChatSessionMeta Meta { get; }

    [ObservableProperty] private string _title;

    /// <summary>更新时间文本</summary>
    public string TimeString => Meta.UpdatedAt.ToString("MM-dd HH:mm");

    public AgentSessionItemViewData(ChatSessionMeta meta, Action<AgentSessionItemViewData> onDeleted)
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
        ChatSession? session = SessionManager.Instance.Load(Meta.SessionId);
        if (session != null)
        {
            session.Title = name;
            session.Save();
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (!await _messageService.ConfirmAsync(Lang.DeleteTips)) return;
        SessionManager.Instance.Delete(Meta.SessionId);
        _onDeleted(this);
    }
}
