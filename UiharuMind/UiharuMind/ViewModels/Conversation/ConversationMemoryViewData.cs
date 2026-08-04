/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.ViewModels.Conversation;

/// <summary>
/// 当前会话的记忆库面板数据:库名/索引状态/编辑入口。
/// 原属聊天列表项(ChatSessionItemViewData),合并输入区后由对话 VM 按会话持有,
/// agent 会话与角色会话共用同一入口。
/// </summary>
public partial class ConversationMemoryViewData : ObservableObject
{
    private readonly ChatSession _session;

    [ObservableProperty] private MemoryData? _memoryData;
    [ObservableProperty] private string? _memoryTooltip;
    [ObservableProperty] private string _memoryStatusKey = "None";

    public ConversationMemoryViewData(ChatSession session)
    {
        _session = session;
        MemoryData = session.Memory;
        RefreshMemoryInfo();
    }

    /// <summary>
    /// 解除对记忆库状态事件的订阅(会话切换丢弃本面板前调用)
    /// </summary>
    public void Detach()
    {
        if (MemoryData != null) MemoryData.StateChanged -= OnMemoryStateChanged;
    }

    [RelayCommand]
    private void MemoryEditor()
    {
        UIManager.ShowMemorySelectWindow(UIManager.GetFocusWindow(), x => { MemoryData = x; },
            MemoryData);
    }

    partial void OnMemoryDataChanged(MemoryData? oldValue, MemoryData? newValue)
    {
        if (oldValue != null) oldValue.StateChanged -= OnMemoryStateChanged;
        if (newValue != null) newValue.StateChanged += OnMemoryStateChanged;
        _session.Memory = newValue;
        RefreshMemoryInfo();
    }

    private void OnMemoryStateChanged()
    {
        Dispatcher.UIThread.Post(RefreshMemoryInfo);
    }

    private void RefreshMemoryInfo()
    {
        var memory = MemoryData;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Lang.MemoryTitle + (memory?.Name ?? Lang.NoMemory));

        if (memory == null)
        {
            MemoryTooltip = sb.ToString();
            MemoryStatusKey = "None";
            return;
        }

        sb.AppendLine(GetMemoryIndexStateText(memory));
        if (memory.LastIndexedAt != null)
            sb.AppendLine(Lang.MemoryIndexLastIndexed +
                          memory.LastIndexedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
        if (!string.IsNullOrEmpty(memory.LastIndexError))
            sb.AppendLine(Lang.MemoryIndexLastError + GetMemoryIndexErrorText(memory.LastIndexError));

        MemoryTooltip = sb.ToString();
        MemoryStatusKey = !string.IsNullOrEmpty(memory.LastIndexError) ? "Error" :
            memory.IndexDirty || memory.LastIndexedAt == null ? "Dirty" : "Ready";
    }

    private static string GetMemoryIndexStateText(MemoryData memory)
    {
        if (memory.IndexDirty) return Lang.MemoryIndexNeedUpdate;
        if (memory.LastIndexedAt == null) return Lang.MemoryIndexNotBuilt;
        return Lang.MemoryIndexReady;
    }

    private static string GetMemoryIndexErrorText(string error)
    {
        if (error.StartsWith("Embedding model startup failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Failed to load LLamaSharp embedding model", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Remote embedding backend is not implemented", StringComparison.OrdinalIgnoreCase))
            return Lang.MemoryIndexEmbeddingServerUnavailable;

        if (error.StartsWith("LLamaSharp embedding request failed", StringComparison.OrdinalIgnoreCase))
            return GetLocalizedText("MemoryIndexEmbeddingRequestFailed");

        return error switch
        {
            "Embedding server is unavailable." => Lang.MemoryIndexEmbeddingServerUnavailable,
            "Embedding model is unavailable." => Lang.MemoryIndexEmbeddingServerUnavailable,
            "Embedding server startup timed out." => Lang.MemoryIndexEmbeddingServerTimeout,
            "Memory name not set" => Lang.MemoryIndexMemoryNameMissing,
            "Memory vector store unavailable" => Lang.MemoryIndexVectorStoreUnavailable,
            "Memory index update failed" => Lang.MemoryIndexUpdateFailed,
            "Memory source validation failed" => GetLocalizedText("MemorySourceValidationFailed"),
            "Memory vector dimension mismatch" => GetLocalizedText("MemoryIndexDimensionMismatch"),
            "Embedding input is too large" => GetLocalizedText("MemoryIndexEmbeddingInputTooLarge"),
            _ => error
        };
    }

    private static string GetLocalizedText(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;
}
