/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Agent;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Chat;

/// <summary>
/// 表示一个对话。历史直接以 <see cref="ChatMessage"/> 持久化——
/// 存储、请求与渲染共用同一个模型，不再需要任何映射层，
/// 也因此能无损承载工具调用、思考内容与审批请求（旧的单文本模型表达不了这些）。
/// </summary>
// 注意:不要让本类实现 IEnumerable<ChatMessage>。System.Text.Json 会把实现了
// IEnumerable<T> 的类型序列化成一个数组,于是所有属性(SessionId/Title/CharacterId/
// CustomParams/WorkspacePath...)全部丢失,存档退化成一个裸消息数组且无法反序列化回来。
// 需要遍历历史请直接用 History。
public class ChatSession
{
    /// <summary>AI 作为首条消息时的作者名（界面据此做旁白式展示）</summary>
    public const string NarratorName = "Narrator";

    /// <summary>存档格式版本</summary>
    public int FormatVersion { get; set; } = 3;

    /// <summary>会话唯一标识，同时是存档文件名</summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>标题（纯显示，允许重复，改名不动文件）</summary>
    public string Title { get; set; } = "Empty";

    /// <summary>列表副标题</summary>
    public string Description { get; set; } = "Empty";

    /// <summary>
    /// 所属角色的标识（<see cref="CharacterData.CharacterId"/>）。角色改名不会断开该引用。
    /// </summary>
    public string CharacterId { get; set; } = nameof(DefaultCharacter.Empty);

    /// <summary>记忆库名</summary>
    public string MemoryName { get; set; } = "";

    /// <summary>绑定的工作目录（仅 agent 会话有意义）</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>权限档索引（仅 agent 会话有意义）</summary>
    public int PermissionModeIndex { get; set; } = 1;

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>最后更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>对话历史</summary>
    public List<ChatMessage> History { get; set; } = [];

    /// <summary>自定义模板参数</summary>
    public Dictionary<string, object?> CustomParams { get; set; } = [];

    /// <summary>
    /// 临时会话：不落盘、不进索引、不出现在会话列表，但在内存中可按标识解析
    /// （自定义 ChatHistoryProvider 需要靠标识反查本会话）。
    /// 快捷翻译/解释等一次性调用即为临时会话，用户点"转为对话"时调用 <see cref="Persist"/> 提升为正式会话。
    /// </summary>
    [JsonIgnore]
    public bool IsTransient { get; set; }

    /// <summary>所属角色</summary>
    [JsonIgnore]
    public CharacterData CharacterData =>
        _characterData ??= CharacterManager.Instance.GetCharacterData(CharacterId);

    /// <summary>
    /// 记忆库。未显式指定时回退到角色的默认记忆——
    /// 该回退只影响运行时解析，不再偷偷改写 <see cref="MemoryName"/> 字段
    /// （旧实现在 getter 里改字段却不落盘，使该字段的值取决于本次运行有没有读过它）。
    /// </summary>
    [JsonIgnore]
    public MemoryData? Memory
    {
        get
        {
            if (_memory != null) return _memory;
            if (!string.IsNullOrEmpty(MemoryName) &&
                MemoryManager.Instance.TryGetMemoryData(MemoryName, out _memory))
            {
                return _memory;
            }

            return _memory = CharacterData.Memory;
        }
        set
        {
            if (_memory == value) return;
            _memory = value;
            MemoryName = value?.Name ?? "";
            Save();
        }
    }

    [JsonIgnore] private ModelRunningData? _modelRunningData;

    /// <summary>该对话对应的模型</summary>
    [JsonIgnore]
    public ModelRunningData? ChatModelRunningData
    {
        get => _modelRunningData ?? LlmManager.Instance.CurrentRunningModel;
        set => _modelRunningData = value;
    }

    /// <summary>首条消息时间</summary>
    [JsonIgnore]
    public DateTime FirstTime => History.Count > 0 ? LocalTimeOf(History[0]) : CreatedAt.LocalDateTime;

    /// <summary>末条消息时间</summary>
    [JsonIgnore]
    public DateTime LastTime => History.Count > 0 ? LocalTimeOf(History[^1]) : UpdatedAt.LocalDateTime;

    private CharacterData? _characterData;
    private MemoryData? _memory;
    private ICharacterRunner? _runner;

    public ChatSession()
    {
    }

    public ChatSession(string title, CharacterData characterData)
    {
        _characterData = characterData;
        CharacterId = characterData.CharacterId;
        Title = title;
        Description = string.IsNullOrEmpty(characterData.FirstGreeting)
            ? characterData.Description
            : characterData.FirstGreeting;
        if (!string.IsNullOrEmpty(characterData.FirstGreeting))
            AddMessage(ChatRole.Assistant, characterData.TryRender(characterData.FirstGreeting));
    }

    /// <summary>
    /// 投影为索引用的元数据
    /// </summary>
    /// <returns>元数据</returns>
    public ChatSessionMeta ToMeta()
    {
        return new ChatSessionMeta
        {
            SessionId = SessionId,
            Title = Title,
            Description = Description,
            CharacterId = CharacterId,
            MemoryName = MemoryName,
            WorkspacePath = WorkspacePath,
            PermissionModeIndex = PermissionModeIndex,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            MessageCount = History.Count,
        };
    }

    /// <summary>
    /// 用给定历史替换现有历史
    /// </summary>
    /// <param name="history">新历史</param>
    public void ReInitHistory(IEnumerable<ChatMessage> history)
    {
        History = history.ToList();
    }

    public int Count => History.Count;

    public ChatMessage this[int index] => History[index];

    /// <summary>
    /// 追加一条消息并落盘
    /// </summary>
    /// <param name="role">角色</param>
    /// <param name="message">文本</param>
    /// <param name="imageBytes">可选图片</param>
    /// <param name="imageMediaType">图片 MIME 类型</param>
    public void AddMessage(ChatRole role, string message, byte[]? imageBytes = null,
        string imageMediaType = "image/jpeg")
    {
        ChatMessage data = CreateMessage(role, message, imageBytes, imageMediaType);
        //如果AI作为第一条消息，那么特殊处理下(特殊显示)
        if (History.Count == 0 && role == ChatRole.Assistant) data.AuthorName = NarratorName;

        History.Add(data);
        Save();
    }

    /// <summary>
    /// 构造一条消息（不入历史）
    /// </summary>
    /// <param name="role">角色</param>
    /// <param name="message">文本</param>
    /// <param name="imageBytes">可选图片</param>
    /// <param name="imageMediaType">图片 MIME 类型</param>
    /// <param name="createdAt">时间戳，默认当前</param>
    /// <returns>消息</returns>
    public ChatMessage CreateMessage(ChatRole role, string message, byte[]? imageBytes = null,
        string imageMediaType = "image/jpeg", DateTimeOffset? createdAt = null)
    {
        List<AIContent> contents = [];
        if (imageBytes is { Length: > 0 }) contents.Add(new DataContent(imageBytes, imageMediaType));
        contents.Add(new TextContent(message));

        return new ChatMessage(role, contents)
        {
            AuthorName = AuthorNameOf(role),
            CreatedAt = createdAt ?? DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// 追加一条模型生成的回复；内容为空则忽略
    /// </summary>
    /// <param name="content">回复文本</param>
    public void AddGeneratedAssistantMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        History.Add(CreateMessage(ChatRole.Assistant, content));
        Save();
    }

    /// <summary>
    /// 删除指定位置的消息
    /// </summary>
    /// <param name="index">下标</param>
    public void RemoveMessageAt(int index)
    {
        if (index < 0 || index >= History.Count) return;
        History.RemoveAt(index);
        Save();
    }

    /// <summary>
    /// 本会话的执行者（惰性创建）。角色扮演与 agent 共用它，
    /// 由角色的 <see cref="ECharacterKind"/> 决定装配形态。
    /// </summary>
    [JsonIgnore]
    public ICharacterRunner Runner => _runner ??= AgentHost.Instance.CreateRunner();

    /// <summary>
    /// 流式生成一条回复。
    /// 本轮的输入与输出由历史提供器统一写入历史，调用方不要预先把输入加进 <see cref="History"/>。
    /// </summary>
    /// <param name="input">本轮用户输入；为 null 表示基于现有历史重新生成</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文本<b>增量</b>流；需要全文请由调用方累积</returns>
    public async IAsyncEnumerable<string> GenerateCompletionStreaming(ChatMessage? input = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (input == null && Count == 0)
        {
            yield return "Error: No message";
            yield break;
        }

        await Runner.AttachAsync(this, cancellationToken).ConfigureAwait(false);

        List<ChatMessage> turnInput = input == null ? [] : [input];
        StringBuilder finalText = StringBuilderPool.Get();
        try
        {
            await foreach (string delta in Runner.RunTextAsync(turnInput, cancellationToken)
                               .ConfigureAwait(false))
            {
                // 只在本地累积一份用于取消时补存,对外透出的仍是增量
                finalText.Append(delta);
                yield return delta;
            }
        }
        finally
        {
            // 取消时框架不会写入历史(InvokedAsync 收到异常即跳过存储),
            // 手动补上本轮输入与已收到的部分内容,与旧行为一致:取消也保留已生成的文本。
            if (cancellationToken.IsCancellationRequested)
            {
                if (input != null) History.Add(input);
                if (finalText.Length > 0) History.Add(CreateMessage(ChatRole.Assistant, finalText.ToString()));
                if (input != null || finalText.Length > 0) Save();
            }

            StringBuilderPool.Release(finalText);
        }
    }

    /// <summary>
    /// 组装一次请求的完整消息列表
    /// </summary>
    /// <returns>消息列表</returns>
    public async Task<List<ChatMessage>> BuildRequestMessagesAsync()
    {
        List<ChatMessage> messages = [];

        // 系统提示由 CharacterPromptBuilder 统一装配(挂载片段 + 自身 Template + 对话模板)
        string instructions = CharacterPromptBuilder.Build(CharacterData, CustomParams);
        if (!string.IsNullOrWhiteSpace(instructions))
            messages.Add(new ChatMessage(ChatRole.System, instructions));

        if (Memory != null && History.Count > 0)
        {
            string longTermMemory = await Memory.GetLongTermMemory(History[^1].Text);
            if (!string.IsNullOrEmpty(longTermMemory))
            {
                messages.Add(new ChatMessage(ChatRole.Tool,
                    "以下是通过文本嵌入模型搜索到的相关信息片段，用户当前的问题极有可能与之相关，请根据片段的相关性(Relevance)参数高低酌情参考：\n" +
                    longTermMemory));
            }
        }

        messages.AddRange(History);
        return messages;
    }

    /// <summary>
    /// 落盘。临时会话为空操作。
    /// </summary>
    public void Save()
    {
        SessionManager.Instance.Save(this);
    }

    /// <summary>
    /// 把临时会话提升为正式会话并落盘
    /// </summary>
    public void Persist()
    {
        if (!IsTransient) return;
        IsTransient = false;
        SessionManager.Instance.Add(this);
    }

    /// <summary>
    /// 清空历史
    /// </summary>
    public void Clear()
    {
        History.Clear();
        Save();
    }

    private string AuthorNameOf(ChatRole role)
    {
        if (role == ChatRole.User) return CharacterManager.Instance.UserCharacterName;
        if (role == ChatRole.System) return "System";
        return CharacterData.CharacterName;
    }

    private static DateTime LocalTimeOf(ChatMessage message)
    {
        return (message.CreatedAt ?? DateTimeOffset.Now).LocalDateTime;
    }
}
