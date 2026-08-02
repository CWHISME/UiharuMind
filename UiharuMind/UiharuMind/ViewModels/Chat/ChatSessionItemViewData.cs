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
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views;

namespace UiharuMind.ViewModels.Chat;

/// <summary>
/// 聊天会话列表条目:元数据驱动的展示 + 条目级操作(改名/复制/删除/清空/记忆库),
/// 会话内容的展示与生成由 ConversationViewModel 承载,与本类无关。
/// </summary>
public partial class ChatSessionItemViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly ChatSessionMeta _meta;
    private ChatSession? _session;

    /// <summary>会话元数据(对话内容页按它加载本体)</summary>
    public ChatSessionMeta Meta => _meta;

    /// <summary>会话标识(列表比对用它,不必为此加载本体)</summary>
    public string SessionId => _meta.SessionId;

    /// <summary>
    /// 会话本体,首次访问时按需加载。列表展示所需字段全部取自元数据,
    /// 只有条目级操作(清空/改名/记忆库)才会触发加载。
    /// </summary>
    public ChatSession ChatSession => _session ??=
        SessionManager.Instance.Load(_meta.SessionId) ?? new ChatSession { SessionId = _meta.SessionId };

    [ObservableProperty] private string _name;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _timeString;

    [ObservableProperty] private string _memoryTipsName;
    [ObservableProperty] private MemoryData? _memoryData;
    [ObservableProperty] private string? _memoryIndexTips;
    [ObservableProperty] private string _memoryStatusKey = "None";

    /// <summary>会话内容被就地改写(改名/清空历史)。若该会话正被展示,页面壳据此刷新对话区</summary>
    public event Action<ChatSessionItemViewData>? OnSessionMutated;

    public ChatSessionItemViewData(ChatSession chatSession)
        : this(chatSession.ToMeta())
    {
        _session = chatSession;
    }

    public ChatSessionItemViewData(ChatSessionMeta meta)
        : this(meta, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public ChatSessionItemViewData(ChatSessionMeta meta, IMessageService messageService)
    {
        _messageService = messageService;
        _meta = meta;
        _description = meta.Description;
        _name = meta.Title;
        _icon = IconUtils.GetCharacterBitmapOrDefault(
            CharacterManager.Instance.GetCharacterData(meta.CharacterId));
        _timeString = CalcTimeString();
        _memoryTipsName = "";
        MemoryData = ResolveMemory(meta);
        RefreshMemoryInfo();
    }

    /// <summary>
    /// 解析记忆库而不触发本体加载
    /// </summary>
    private static MemoryData? ResolveMemory(ChatSessionMeta meta)
    {
        if (!string.IsNullOrEmpty(meta.MemoryName) &&
            MemoryManager.Instance.TryGetMemoryData(meta.MemoryName, out MemoryData? memory))
        {
            return memory;
        }

        return CharacterManager.Instance.GetCharacterData(meta.CharacterId).Memory;
    }

    //================= 条目级操作 =================

    [RelayCommand]
    public async Task ClearChatHistory()
    {
        if (!await _messageService.ConfirmAsync(Lang.ClearTips)) return;
        ChatSession.Clear();
        TimeString = "";
        OnSessionMutated?.Invoke(this);
    }

    [RelayCommand]
    public void EditCharater()
    {
        UIManager.ShowEditCharacterWindow(new CharacterInfoViewData(ChatSession.CharacterData),
            x => x.SaveCharacter());
    }

    [RelayCommand]
    public async Task Rename()
    {
        var result = await UIManager.ShowStringEditWindow(ChatSession.Title);
        if (string.IsNullOrEmpty(result)) return;

        // 标题是纯显示字段:改名不动文件、不删不加
        ChatSession.Title = result;
        ChatSession.Save();
        Name = result;
        OnSessionMutated?.Invoke(this);
    }

    [RelayCommand]
    public void Copy()
    {
        SessionManager.Instance.Copy(ChatSession);
    }

    [RelayCommand]
    public async Task Delete()
    {
        if (await _messageService.ConfirmAsync(Lang.DeleteAllClipboardHistoryTips))
            SessionManager.Instance.Delete(ChatSession);
    }

    //================= 记忆库 =================

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
        ChatSession.Memory = newValue;
        RefreshMemoryInfo();
    }

    private void OnMemoryStateChanged()
    {
        Dispatcher.UIThread.Post(RefreshMemoryInfo);
    }

    private void RefreshMemoryInfo()
    {
        MemoryTipsName = Lang.MemoryTitle + (MemoryData?.Name ?? Lang.NoMemory);
        RefreshMemoryIndexTips();
    }

    private void RefreshMemoryIndexTips()
    {
        var memory = MemoryData;
        if (memory == null)
        {
            MemoryIndexTips = Lang.NoMemory;
            MemoryStatusKey = "None";
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(GetMemoryIndexStateText(memory));
        if (memory.LastIndexedAt != null)
            sb.AppendLine(Lang.MemoryIndexLastIndexed +
                          memory.LastIndexedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
        if (!string.IsNullOrEmpty(memory.LastIndexError))
            sb.AppendLine(Lang.MemoryIndexLastError + GetMemoryIndexErrorText(memory.LastIndexError));

        MemoryIndexTips = sb.ToString();
        MemoryStatusKey = !string.IsNullOrEmpty(memory.LastIndexError) ? "Error" :
            memory.IndexDirty || memory.LastIndexedAt == null ? "Dirty" : "Ready";
    }

    private string GetMemoryIndexStateText(MemoryData memory)
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

    private string CalcTimeString()
    {
        // 已加载则用末条消息时间,否则用元数据的更新时间——不为一行时间去加载本体
        DateTime lastTime = _session?.LastTime ?? _meta.UpdatedAt.LocalDateTime;
        return DateTime.Now.Date == lastTime.Date
            ? lastTime.ToString("HH:mm")
            : lastTime.ToString("yyyy/MM/dd");
    }
}
