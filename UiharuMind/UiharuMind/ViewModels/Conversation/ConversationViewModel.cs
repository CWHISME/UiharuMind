/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using UiharuMind.Core.AI.Agent;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.Views;

namespace UiharuMind.ViewModels.Conversation;

/// <summary>
/// 一次对话的视图模型，角色扮演与 agent 共用这一个实现。
/// 阶段 3 之后两者跑的是同一条路：session.Runner.RunAsync() → AIContent 流 → ApplyContent()，
/// 差异只剩"暴露哪些操作面板"(workspace / 权限档 / todo 侧栏 vs 角色卡 / 参数 / 翻译插件)，
/// 由角色的 ECharacterKind 控制显隐，因此不需要为此分出子类；
/// 原先的 ConversationViewModelBase 只有一个实现，已并入本类。
/// </summary>
public partial class ConversationViewModel : ViewModelBase
{

    public ObservableCollection<ConversationItemBase> Items { get; } = new();

    /// <summary>附件集合(文件路径或内存字节),由输入框上方区域展示</summary>
    public ObservableCollection<ConversationAttachment> Attachments { get; } = new();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _inputPlaceholder = string.Empty;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _scrollToEnd;
    [ObservableProperty] private KeyGesture _sendGesture = new(Key.Enter);

    [RelayCommand]
    private async Task SendMessage()
    {
        string text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = string.Empty;
        await SendCoreAsync(text);
    }

    [RelayCommand]
    private void StopSending()
    {
        OnStopSending();
    }

    [RelayCommand]
    private void InputExtra()
    {
        OnInputExtra();
    }

    //================= 附件 =================

    /// <summary>添加文件附件</summary>
    public void AddAttachmentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Attachments.Add(new ConversationAttachment
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            MediaType = GetMediaType(path),
        });
    }

    /// <summary>添加内存字节附件(如粘贴图片)</summary>
    public void AddAttachmentBytes(byte[] bytes, string mediaType = "image/png", string? fileName = null)
    {
        if (bytes == null || bytes.Length == 0) return;
        Attachments.Add(new ConversationAttachment
        {
            Bytes = bytes,
            FileName = fileName ?? $"pasted_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png",
            MediaType = mediaType,
        });
    }

    [RelayCommand]
    private void RemoveAttachment(ConversationAttachment item)
    {
        Attachments.Remove(item);
    }

    [RelayCommand]
    private void PreviewAttachment(ConversationAttachment item)
    {
        // 非图片文件:打开其所在目录
        if (!item.IsImage)
        {
            if (!string.IsNullOrEmpty(item.FilePath))
                App.FilesService.OpenFolder(Path.GetDirectoryName(item.FilePath) ?? item.FilePath);
            return;
        }

        Bitmap? bitmap = null;
        try
        {
            if (item.Bytes != null)
            {
                using var stream = new MemoryStream(item.Bytes);
                bitmap = new Bitmap(stream);
            }
            else if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                bitmap = new Bitmap(item.FilePath);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Preview attachment failed '{item.FileName}': {e.Message}");
            return;
        }

        if (bitmap != null) UIManager.ShowPreviewImageWindowAtMousePosition(bitmap);
    }

    /// <summary>根据路径推断 MIME 类型;非图片返回通用二进制类型</summary>
    protected static string GetMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };
    }

    public ObservableCollection<TodoDisplayItem> Todos { get; } = new();

    [ObservableProperty] private int _permissionModeIndex = 1; //默认 AutoEdit
    [ObservableProperty] private string? _workspacePath;
    [ObservableProperty] private EAgentMode _currentMode = EAgentMode.Execute;
    [ObservableProperty] private bool _hasTodos;

    /// <summary>当前会话元数据(未开始首轮前为空)</summary>
    public ChatSessionMeta? CurrentMeta { get; private set; }

    /// <summary>会话集合变化(新会话创建/一轮结束),页面据此刷新左侧列表</summary>
    public event Action? SessionsChanged;

    /// <summary>当前模式显示标签</summary>
    public string ModeLabel => LocalizationManager.Instance.GetString(
        CurrentMode == EAgentMode.Plan ? "AgentPlanMode" : "AgentModeExecute");

    private readonly ICharacterRunner _runner = AgentHost.Instance.CreateRunner();

    private CancellationTokenSource? _runCancellation;
    private TextConversationItem? _streamingText;
    private ThinkingItem? _streamingThinking;
    private readonly List<ApprovalRequestItem> _pendingApprovals = new();

    public ConversationViewModel()
    {
        var agentSetting = AgentSettingConfig.Current;
        _permissionModeIndex = Math.Clamp(agentSetting.DefaultPermissionModeIndex, 0, 2);
        _currentMode = agentSetting.DefaultPlanMode ? EAgentMode.Plan : EAgentMode.Execute;
        if (!string.IsNullOrEmpty(agentSetting.DefaultWorkspacePath) &&
            Directory.Exists(agentSetting.DefaultWorkspacePath))
        {
            _workspacePath = agentSetting.DefaultWorkspacePath;
        }

        InputPlaceholder = LocalizationManager.Instance.GetString("AgentInputWatermark");
        LocalizationManager.Instance.LanguageChanged += () =>
        {
            InputPlaceholder = LocalizationManager.Instance.GetString("AgentInputWatermark");
            OnPropertyChanged(nameof(ModeLabel));
        };
    }

    //================= 模式 / 配置 =================

    [RelayCommand]
    private void CycleMode()
    {
        CurrentMode = CurrentMode.Next();
    }

    private void OnInputExtra()
    {
        CycleMode();
    }

    partial void OnCurrentModeChanged(EAgentMode value)
    {
        ApplyMode();
        OnPropertyChanged(nameof(ModeLabel));
    }

    partial void OnPermissionModeIndexChanged(int value)
    {
        if (CurrentMeta == null) return;
        CurrentMeta.PermissionModeIndex = value;
        PersistSessionSettings();
    }

    partial void OnWorkspacePathChanged(string? value)
    {
        if (CurrentMeta == null) return;
        CurrentMeta.WorkspacePath = value;
        PersistSessionSettings();
    }

    [RelayCommand]
    private async Task SelectWorkspace()
    {
        string path = await App.FilesService.OpenSelectFolderAsync(WorkspacePath);
        if (!string.IsNullOrEmpty(path)) WorkspacePath = path;
    }

    [RelayCommand]
    private void ClearWorkspace()
    {
        WorkspacePath = null;
    }

    [RelayCommand]
    private async Task AddAttachment()
    {
        var file = await App.FilesService.OpenFileAsync(UIManager.GetFocusWindow());
        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) AddAttachmentPath(path);
    }

    //================= 发送与运行循环 =================

    private async Task SendCoreAsync(string text)
    {
        // 运行中输入 = 插话:入注入队列,agent 下一次机会消费
        if (IsGenerating)
        {
            if (_runner.TryInject(new[] { new ChatMessage(ChatRole.User, text) }))
            {
                Items.Add(CreateUserItem(text));
            }

            return;
        }

        List<ConversationAttachment>? attachments = Attachments.Count > 0 ? Attachments.ToList() : null;
        Attachments.Clear();
        Items.Add(CreateUserItem(text));
        ScrollToEnd = true;
        await RunTurnAsync(BuildUserMessage(text, attachments), text);
    }

    private void OnStopSending()
    {
        _runCancellation?.Cancel();
        foreach (ApprovalRequestItem approval in _pendingApprovals.ToList())
        {
            approval.CancelAsDeny();
        }
    }

    private async Task RunTurnAsync(ChatMessage userMessage, string titleSeed)
    {
        IsGenerating = true;
        _runCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _runCancellation.Token;
        try
        {
            ChatSessionMeta meta = await EnsureSessionAsync(titleSeed, cancellationToken);
            List<ChatMessage>? nextMessages = new() { userMessage };

            while (nextMessages is { Count: > 0 })
            {
                List<ApprovalRequestItem> turnApprovals = new();
                try
                {
                    await foreach (AIContent content in _runner.RunAsync(nextMessages, cancellationToken))
                    {
                        ApplyContent(content, turnApprovals);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                CloseStreamSegment();
                await RefreshTodosAsync();

                if (turnApprovals.Count == 0) break;

                // 审批往返:等待用户对每个请求做出决定,回应作为下一轮输入
                nextMessages = new List<ChatMessage>();
                foreach (ApprovalRequestItem approval in turnApprovals)
                {
                    nextMessages.Add(await approval.Response);
                }

                _pendingApprovals.RemoveAll(turnApprovals.Contains);
                if (cancellationToken.IsCancellationRequested) break;
            }

            await _runner.SaveStateAsync();
        }
        catch (Exception e)
        {
            Log.Error($"Agent turn failed: {e}");
            Items.Add(new ErrorItem { Message = e.Message });
        }
        finally
        {
            CloseStreamSegment();
            IsGenerating = false;
            _runCancellation = null;
            _pendingApprovals.Clear();
            SessionsChanged?.Invoke();
        }
    }

    /// <summary>
    /// AIContent → 会话条目(实时流与历史回放共用)
    /// </summary>
    private void ApplyContent(AIContent content, List<ApprovalRequestItem>? approvalCollector)
    {
        switch (content)
        {
            case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                if (_streamingThinking == null) Items.Add(_streamingThinking = new ThinkingItem());
                _streamingThinking.Append(reasoning.Text);
                break;

            case TextContent text when !string.IsNullOrEmpty(text.Text):
                if (_streamingText == null) Items.Add(_streamingText = CreateAssistantItem());
                _streamingText.Append(text.Text);
                break;

            case FunctionCallContent call:
                CloseStreamSegment();
                if (AgentContentFormatter.IsHousekeepingTool(call.Name))
                {
                    _ = RefreshTodosAsync();
                    break;
                }

                Items.Add(new ToolCallItem
                {
                    CallId = call.CallId,
                    ToolName = call.Name,
                    IconGlyph = AgentContentFormatter.GetToolIcon(call.Name),
                    ArgumentSummary = AgentContentFormatter.SummarizeArguments(call),
                    ArgumentsJson = call.Arguments == null
                        ? string.Empty
                        : string.Join("\n", call.Arguments.Select(x => $"{x.Key}: {x.Value}")),
                });
                break;

            case FunctionResultContent result:
                if (Items.OfType<ToolCallItem>().LastOrDefault(x => x.CallId == result.CallId) is { } item)
                {
                    item.IsRunning = false;
                    item.IsSuccess = result.Exception == null;
                    item.ResultText = result.Result?.ToString() ?? result.Exception?.Message ?? string.Empty;
                }

                break;

            case ToolApprovalRequestContent approvalRequest:
                CloseStreamSegment();
                ApprovalRequestItem approvalItem = new(approvalRequest);
                Items.Add(approvalItem);
                _pendingApprovals.Add(approvalItem);
                approvalCollector?.Add(approvalItem);
                break;

            case ErrorContent error:
                Items.Add(new ErrorItem { Message = error.Message });
                break;
        }
    }

    //================= agent / 会话装配 =================

    private async Task<ChatSessionMeta> EnsureSessionAsync(string titleSeed, CancellationToken cancellationToken)
    {
        if (CurrentMeta == null)
        {
            string title = titleSeed.Length > 30 ? titleSeed[..30] + "…" : titleSeed;
            ChatSession created = new()
            {
                CharacterId = nameof(DefaultCharacter.WorkspaceAgent),
                Title = title,
                Description = titleSeed,
                WorkspacePath = WorkspacePath,
                PermissionModeIndex = PermissionModeIndex,
            };
            SessionManager.Instance.Add(created);
            CurrentMeta = created.ToMeta();
            Title = CurrentMeta.Title;
            await _runner.AttachAsync(created, cancellationToken);
            ApplyMode();
            SessionsChanged?.Invoke();
            return CurrentMeta;
        }

        await AttachAsync(CurrentMeta, cancellationToken);
        ApplyMode();
        return CurrentMeta;
    }

    /// <summary>
    /// 绑定执行者到给定会话。工作目录与权限档取自会话本体，
    /// 变化时由执行者内部按装配指纹重建并迁移框架附加状态。
    /// </summary>
    private async Task AttachAsync(ChatSessionMeta meta, CancellationToken cancellationToken)
    {
        ChatSession? session = SessionManager.Instance.Load(meta.SessionId);
        if (session == null) return;

        session.WorkspacePath = WorkspacePath;
        session.PermissionModeIndex = PermissionModeIndex;
        await _runner.AttachAsync(session, cancellationToken);
    }

    private void ApplyMode()
    {
        _runner.SetMode(CurrentMode);
    }

    /// <summary>
    /// 把界面上改动的工作目录与权限档写回会话本体
    /// </summary>
    private void PersistSessionSettings()
    {
        if (CurrentMeta == null) return;
        ChatSession? session = SessionManager.Instance.Load(CurrentMeta.SessionId);
        if (session == null) return;
        session.WorkspacePath = CurrentMeta.WorkspacePath;
        session.PermissionModeIndex = CurrentMeta.PermissionModeIndex;
        session.Save();
    }

    private ChatMessage BuildUserMessage(string text, List<ConversationAttachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0) return new ChatMessage(ChatRole.User, text);

        bool isVision = LlmManager.Instance.CurrentRunningModel?.IsVisionModel == true;
        List<AIContent>? contents = isVision ? new() { new TextContent(text) } : null;
        List<string> fileReferences = new();

        foreach (ConversationAttachment attachment in attachments)
        {
            // 仅图片且为视觉模型时内联字节;其余文件一律以路径文本引用
            if (isVision && attachment.IsImage)
            {
                try
                {
                    byte[] data = attachment.Bytes ?? File.ReadAllBytes(attachment.FilePath!);
                    contents!.Add(new DataContent(data, attachment.MediaType));
                }
                catch (Exception e)
                {
                    Log.Warning($"Attachment load failed '{attachment.FileName}': {e.Message}");
                    fileReferences.Add(attachment.FilePath ?? attachment.FileName);
                }
            }
            else
            {
                fileReferences.Add(attachment.FilePath ?? attachment.FileName);
            }
        }

        if (isVision && (contents!.Count > 1 || fileReferences.Count == 0))
        {
            if (fileReferences.Count > 0)
                contents.Add(new TextContent(string.Join('\n', fileReferences.Select(x => $"[Attached file: {x}]"))));
            return new ChatMessage(ChatRole.User, contents);
        }

        string reference = string.Join('\n', fileReferences.Select(x => $"[Attached file: {x}]"));
        return new ChatMessage(ChatRole.User, $"{text}\n{reference}");
    }

    //================= 加载与回放 =================

    /// <summary>
    /// 切换到指定会话(null = 新会话空态);运行中的轮次会被打断
    /// </summary>
    /// <param name="meta">会话元数据</param>
    public async Task LoadSessionAsync(ChatSessionMeta? meta)
    {
        _runCancellation?.Cancel();
        ClearStreamState();
        _runner.ClearSession();
        CurrentMeta = meta;
        Title = meta?.Title ?? string.Empty;
        if (meta == null) return;

        WorkspacePath = meta.WorkspacePath;
        PermissionModeIndex = meta.PermissionModeIndex;

        try
        {
            await AttachAsync(meta, CancellationToken.None);

            CurrentMode = _runner.GetMode();
            ReplayMessages(_runner.GetHistory());
            await RefreshTodosAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"Load session failed: {e.Message}");
            Items.Add(new ErrorItem { Message = e.Message });
        }
    }

    /// <summary>
    /// 历史消息回放:与实时流共用 ApplyContent 管道
    /// </summary>
    private void ReplayMessages(IEnumerable<ChatMessage> messages)
    {
        foreach (ChatMessage message in messages)
        {
            if (message.Role == ChatRole.User)
            {
                string text = message.Text;
                if (!IsFrameworkInjected(message) && !string.IsNullOrWhiteSpace(text))
                {
                    Items.Add(CreateUserItem(text));
                }

                continue;
            }

            foreach (AIContent content in message.Contents)
            {
                ApplyContent(content, null);
            }

            CloseStreamSegment();
        }

        foreach (ToolCallItem item in Items.OfType<ToolCallItem>().Where(x => x.IsRunning))
        {
            item.IsRunning = false;
        }

        foreach (ApprovalRequestItem item in Items.OfType<ApprovalRequestItem>().Where(x => !x.IsResolved))
        {
            item.CancelAsDeny();
        }
    }

    /// <summary>
    /// 识别非真实用户输入的 user 角色消息:框架上下文提供器注入的消息
    /// (todo 快照、模式切换通知等)带 _attribution 溯源标记;审批回应为控制消息。
    /// 它们是模型上下文的一部分(持久化属正常),但不应渲染为用户气泡。
    /// </summary>
    private static bool IsFrameworkInjected(ChatMessage message)
    {
        if (message.AdditionalProperties?.ContainsKey("_attribution") == true) return true;
        return message.Contents.Any(x => x is ToolApprovalResponseContent);
    }

    //================= todo =================

    private async Task RefreshTodosAsync()
    {
        if (!_runner.HasSession) return;
        try
        {
            IReadOnlyList<TodoSnapshot> todos = await _runner.GetTodosAsync();
            Todos.Clear();
            foreach (TodoSnapshot todo in todos)
            {
                Todos.Add(new TodoDisplayItem(todo));
            }

            HasTodos = Todos.Count > 0;
        }
        catch (Exception e)
        {
            Log.Warning($"Refresh todos failed: {e.Message}");
        }
    }

    //================= 条目构造 =================

    private static TextConversationItem CreateUserItem(string text)
    {
        return new TextConversationItem(true)
        {
            Message = text,
            SenderName = LocalizationManager.Instance.GetString("AgentSenderUser"),
            SenderColor = Avalonia.Media.Brushes.LightGreen,
            Icon = IconUtils.DefaultUserIcon,
            Timestamp = DateTime.Now.ToString("HH:mm"),
        };
    }

    /// <summary>助手条目;头像暂用默认角色图,阶段 3 接入 agent 角色自身的头像</summary>
    private static TextConversationItem CreateAssistantItem()
    {
        return new TextConversationItem(false)
        {
            SenderName = "Agent",
            SenderColor = Avalonia.Media.Brushes.DeepSkyBlue,
            Icon = IconUtils.DefaultCharIcon,
            Timestamp = DateTime.Now.ToString("HH:mm"),
            IsDone = false,
        };
    }

    private void CloseStreamSegment()
    {
        if (_streamingText != null) _streamingText.IsDone = true;
        _streamingText = null;
        _streamingThinking = null;
    }

    private void ClearStreamState()
    {
        Items.Clear();
        Todos.Clear();
        HasTodos = false;
        _pendingApprovals.Clear();
        _streamingText = null;
        _streamingThinking = null;
    }
}

/// <summary>
/// todo 侧栏显示项
/// </summary>
public class TodoDisplayItem
{
    /// <summary>内容描述</summary>
    public string Content { get; }

    /// <summary>状态图形符号</summary>
    public string StatusGlyph { get; }

    /// <summary>是否已完成(删除线样式)</summary>
    public bool IsCompleted { get; }

    public TodoDisplayItem(TodoSnapshot todo)
    {
        Content = todo.Title;
        IsCompleted = todo.IsComplete;
        StatusGlyph = todo.IsComplete ? "✓" : "○";
    }
}
