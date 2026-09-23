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
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat;

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
    private List<ChatMessage>? _history = []; //null = 已卸载,下次访问按 _historyReload 取回;卸载条件见 SessionManager 驻留策略(ADR 0036)
    private Func<List<ChatMessage>>? _historyReload; //卸载后把历史取回来的入口;只有落盘过的会话给得出

    /// <summary>存档格式版本(4 起:头文件 .meta.json + 历史 .history.jsonl 分离)</summary>
    public int FormatVersion { get; set; } = 4;

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

    /// <summary>会话覆写的模型名；为空表示无覆写、跟随全局当前模型</summary>
    public string? SessionModelName { get; set; }

    /// <summary>
    /// 派活给它的那个会话；为空表示这是一个普通会话。非空即<b>子会话</b>，
    /// 见 <c>docs/CONTEXT.md</c> 的「子会话」与 ADR 0021。
    /// </summary>
    public string? ParentSessionId { get; set; }

    /// <summary>
    /// 派活者（主代理）的产出目录名（相对 <c>AgentOutputLayout.RootPath</c>）。
    /// 子代理的产出直接落进派活者会话的目录,不单开目录——同名覆盖风险接受:需要隔离的是
    /// 多个主会话之间的污染(见 AgentOutputLayout),同一会话内的主代理与子代理共用一个。
    /// 仅子会话有意义;旧存档没有这个值,回退子会话自己的目录。
    /// </summary>
    public string? ParentOutputFolderName { get; set; }

    /// <summary>会话是不是子会话</summary>
    [JsonIgnore]
    public bool IsSubSession => !string.IsNullOrEmpty(ParentSessionId);

    /// <summary>
    /// 子会话装配成哪一种子代理。<b>必须落盘</b>：重开一个子会话续跑时，装配要按同一档重建，
    /// 否则探索型会被重建成通用型——那就是「子代理比派活的能力更大」。仅子会话有意义。
    /// </summary>
    public ESubAgentType SubAgentType { get; set; } = ESubAgentType.General;

    /// <summary>
    /// 被点名的子智能体名（`AgentOptionsFactory.SanitizeAgentName` 的产物）；
    /// 空串表示通用匿名子代理（人格为空、能力取派活者那一份）。仅子会话有意义。
    /// </summary>
    public string SubAgentName { get; set; } = string.Empty;

    /// <summary>
    /// 派活时给的一句话身份/职业（可选）；空串表示未设定。随会话落盘：
    /// 重开子会话续跑时注入「# 角色」段，也直接用作会话标题。仅子会话有意义。
    /// </summary>
    public string SubAgentRole { get; set; } = string.Empty;

    /// <summary>
    /// 这是一个<b>群壳会话</b>：历史是群流水（用户与成员的群发言，只给人看、<b>永不喂给模型</b>），
    /// 自己永不跑轮。成员各有一个真会话，群发言投递进他们自己的历史（ADR 0046）。
    /// </summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// 群的类型，建群时定、之后不变（ADR 0042「已定」）。智能体群绑工作区（就是本会话的
    /// <see cref="WorkspacePath"/>），可混装普通角色；普通群不绑，只收普通角色。仅群壳有意义。
    /// </summary>
    public bool IsAgentGroup { get; set; }

    /// <summary>成员会话标识，顺序即发言顺序。仅群壳有意义</summary>
    public List<string> GroupMemberSessionIds { get; set; } = [];

    /// <summary>
    /// 所属群壳会话；非空即<b>群成员会话</b>。刻意不复用 <see cref="ParentSessionId"/>：
    /// 那会让成员按子代理装配（能力与群壳取交集、不能再开子代理），而成员是 peer（ADR 0046 决策 2）。
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// 群流水交到哪儿了：下标在它之前的群发言都已投递给这个成员。仅群成员有意义。
    /// 必须落盘——重开应用之后要接着投，而不是把整段群流水再塞给他一遍。
    /// </summary>
    public int GroupCursor { get; set; }

    /// <summary>会话是不是群成员会话</summary>
    [JsonIgnore]
    public bool IsGroupMember => !string.IsNullOrEmpty(GroupId);

    /// <summary>
    /// 这个子会话是<b>后台派出、报告还没交回</b>。仅子会话有意义。
    ///
    /// 必须落盘：进程被杀时它就是「父会话里那条『已派出』永远等不到下文」的唯一线索，
    /// 启动时据此扫描收口（往父会话落一条「因退出而中止」）。进程内它还是唤醒排队的依据。
    /// 见 [ADR 0025]。
    /// </summary>
    public bool BackgroundReportPending { get; set; }

    /// <summary>
    /// 最近一次派发/续跑的开始时刻（仅子会话有意义）。每次派发时由
    /// <c>BackgroundSubAgentDispatcher.Dispatch</c> 写入并随会话落盘——
    /// 右栏「子代理」面板的「本次已运行 / 末轮耗时」靠它重算，不依赖 UI 计时器。
    /// 旧数据没有该字段，显示时回退到最后更新时间戳。
    /// </summary>
    public DateTimeOffset? LastRunStartedAt { get; set; }

    /// <summary>会话累计输入 token（响应 usage 不随消息持久化，累计值记在本体上）</summary>
    public long TotalInputTokens { get; set; }

    /// <summary>会话累计输出 token</summary>
    public long TotalOutputTokens { get; set; }

    /// <summary>
    /// 会话累计思考（推理）token。与累计值一样记在本体上——
    /// 响应 usage 不随消息持久化，不记的话切回会话思考累计就丢了。
    /// </summary>
    public long TotalReasoningTokens { get; set; }

    /// <summary>
    /// 最近一次响应的输入 token，即这个会话的上下文占用。
    /// 与累计值一样记在本体上——不记的话每次切回会话都要等下一次响应才知道有多满。
    /// </summary>
    public long LastInputTokens { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>最后更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 对话历史。不随会话头序列化——单独以 JSONL 追加式持久化。
    ///
    /// <b>可能是卸载态</b>：本体缓存有上限（见 <c>SessionManager</c> 的驻留策略），
    /// 冷会话的历史会被卸掉，读到这里时按需重载。因此
    /// <b>不要把它取出来存成字段再跨 await 用</b>——中途卸载会让你手上那份成为孤儿，
    /// 往里写的东西没人落盘。要么现取现用，要么先 <c>Pin</c> 住。
    /// </summary>
    [JsonIgnore]
    public List<ChatMessage> History
    {
        get => _history ??= _historyReload?.Invoke() ?? [];
        set => _history = value;
    }

    /// <summary>
    /// 交代「卸载之后怎么把历史取回来」。只有落盘过的会话给得出——
    /// 临时会话只存在于内存，卸了就真没了，因此永远不给。
    /// </summary>
    /// <param name="reload">重载入口</param>
    internal void SetHistoryReload(Func<List<ChatMessage>> reload) => _historyReload = reload;

    /// <summary>
    /// 把历史从内存里卸掉（下次访问按需重载）。会话本体<b>不换实例</b>，
    /// 于是所有持有它的人都不受影响；受影响的只有持有 <see cref="ChatMessage"/>
    /// <b>实例</b>的界面条目，重载之后它们认不回来——而那正是「冷会话」的定义。
    /// </summary>
    /// <returns>真的卸掉了返回 true</returns>
    internal bool UnloadHistory()
    {
        if (_historyReload == null || _history == null) return false;

        _history = null;
        return true;
    }

    /// <summary>
    /// 本轮开始时刻。每轮由 <c>TurnDriver</c> 盖章，<c>SessionChatHistoryProvider</c>
    /// 落盘时取用后清空——一次性凭据，没有它就退化成落盘时刻。
    ///
    /// 存在的理由：框架交给持久化的请求消息是<b>重建的副本</b>，丢了我们在
    /// <see cref="CreateMessage"/> 里盖的 <c>CreatedAt</c>。补成落盘时刻的话，
    /// 一轮跑几分钟（工具往返、长回复）之后用户消息会显示得比模型回复还晚。
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? TurnStartedAt { get; set; }

    /// <summary>
    /// 本轮检索到的知识库片段。由 <c>MemoryContextProvider</c> 盖章，
    /// <c>SessionChatHistoryProvider</c> 落盘时取用后清空——与 <see cref="TurnStartedAt"/>
    /// 同一套一次性凭据模式。
    ///
    /// 走凭据而不是当场写进历史：检索发生在轮次开始时，那一刻本轮的用户消息还没落进
    /// <see cref="History"/>（它由 <c>StoreChatHistoryAsync</c> 在轮次结束后才追加），
    /// 当场写会让检索卡排到用户气泡<b>前面</b>去。
    /// </summary>
    [JsonIgnore]
    public string PendingKnowledgeSnippets { get; private set; } = "";

    /// <summary>本轮检索到片段时触发，供 <c>TurnDriver</c> 转成界面通知</summary>
    public event Action<string>? KnowledgeRetrieved;

    /// <summary>
    /// 历史被追加了（参数是新增段的起始下标）。<b>外驱的界面壳靠它跟上</b>——
    /// 它不驱动这一轮，拿不到内容流，只能等这个信号再去读新增的那几条。
    ///
    /// 粒度是<b>每次服务调用</b>（框架每调一次模型就落一次盘），不是逐 token。
    /// 逐 token 走的是 <see cref="LiveTurn"/>：同一条内容流的分岔，不是第二份真相。
    /// 两者的分工写在那里。
    ///
    /// ⚠️ <b>可能来自后台线程</b>（子代理与定时任务都不在 UI 线程上），订阅方自行 marshal。
    /// </summary>
    public event Action<int>? HistoryAppended;

    /// <summary>
    /// 框架完成了<b>一次服务调用</b>并把它的请求与响应落进了历史。
    ///
    /// 与 <see cref="HistoryAppended"/> 的区别：那个信号凡追加都发（别处交回报告也算），
    /// 这个只在执行者自己的服务调用边界上发——执行者据此往内容流里放消息边界
    /// （<c>MessageBoundaryContent</c>），中途被别人追加一条不会把正在流的气泡切成两半。
    ///
    /// ⚠️ 来自执行线程，订阅方自行 marshal。
    /// </summary>
    public event Action? ServiceCallPersisted;

    /// <summary>
    /// 通报一次服务调用已落盘（由历史提供器在每次落盘后调用）
    /// </summary>
    public void NotifyServiceCallPersisted()
    {
        ServiceCallPersisted?.Invoke();
    }

    /// <summary>
    /// 本会话的实时内容分岔口。跑这一轮的 <c>TurnDriver</c> 把内容交给它，
    /// 打开着的窗口把自己的渲染落点挂上去（<see cref="LiveTurnStream.Observe"/>），
    /// 于是<b>观察者也能逐 token 看</b>，而不是只等按服务调用粒度落盘的 <see cref="HistoryAppended"/>。
    ///
    /// 与历史的分工：流负责助手正文/思考段/工具卡以及被消费的用户消息（<c>UserMessageContent</c>），
    /// 历史负责它产不出的那几类（检索卡、旁白、交接文档、后续报告）与落盘配对。
    /// </summary>
    [JsonIgnore]
    public LiveTurnStream LiveTurn { get; } = new();

    /// <summary>
    /// 历史里的某<b>一条被原地换掉</b>了（不是追加）。参数是下标与<b>被换掉的那一条</b>
    /// ——界面靠后者认回自己当初为它渲染出的条目，从而只重建那一处。
    ///
    /// 带上位置而不是发个"整份改写了"：界面若因此清空重放，满屏气泡的 markdown 渲染器
    /// 会一起重建，表现是整个窗口闪一下。
    ///
    /// 目前唯一的来源是「后续报告原地替换上一份」（见 <c>SubAgentReportHandoff</c>）——
    /// 刻意<b>不</b>由 <see cref="Save"/> 统一发：那个方法到处都在调，
    /// 变成每次保存都让界面重建一次。
    ///
    /// ⚠️ 同样<b>可能来自后台线程</b>，订阅方自行 marshal。
    /// </summary>
    public event Action<int, ChatMessage>? HistoryMessageReplaced;

    /// <summary>
    /// 通知订阅方历史里的某一条被原地换掉了。只该由「确实绕过了当前界面去改历史」的那些路径调用
    /// </summary>
    /// <param name="index">被替换的下标</param>
    /// <param name="replaced">被换掉的那一条</param>
    public void NotifyHistoryMessageReplaced(int index, ChatMessage replaced) =>
        HistoryMessageReplaced?.Invoke(index, replaced);

    /// <summary>
    /// 记录本轮检索到的知识库片段：存进一次性凭据等落盘，同时通知界面即时显示
    /// </summary>
    /// <param name="snippets">拼好的片段文本</param>
    public void ReportKnowledgeRetrieved(string snippets)
    {
        if (string.IsNullOrEmpty(snippets)) return;

        PendingKnowledgeSnippets = snippets;
        KnowledgeRetrieved?.Invoke(snippets);
    }

    /// <summary>取走并清空本轮的检索片段凭据</summary>
    /// <returns>片段文本；本轮没检索到则为空串</returns>
    public string TakeKnowledgeSnippets()
    {
        string snippets = PendingKnowledgeSnippets;
        PendingKnowledgeSnippets = "";
        return snippets;
    }

    /// <summary>输入框草稿（尚未发送的输入内容）。随会话头持久化，切会话/重启后恢复；发送成功后清空。</summary>
    public string ComposerDraft { get; set; } = "";

    /// <summary>自定义模板参数</summary>
    public Dictionary<string, object?> CustomParams { get; set; } = [];

    /// <summary>
    /// 本会话产生的、由应用自己落盘的附件文件（粘贴的图片等），删除会话时一并清理。
    /// 只记录应用创建的文件——用户从磁盘选中的附件是他的原始文件，绝不能跟着会话被删掉。
    /// </summary>
    public List<string> OwnedAttachmentFiles { get; set; } = [];

    /// <summary>
    /// 临时会话：不落盘、不进索引、不出现在会话列表，但在内存中可按标识解析
    /// （自定义 ChatHistoryProvider 需要靠标识反查本会话）。
    /// 快捷翻译/解释等一次性调用即为临时会话，用户点"转为对话"时调用 <see cref="Persist"/> 提升为正式会话。
    /// </summary>
    [JsonIgnore]
    public bool IsTransient { get; set; }

    /// <summary>
    /// 无人值守 shell 预授权命令模式（glob）。定时任务在挂接执行者前设置，
    /// 只属于"这一次无头运行"而非会话本身，因此仅运行期有效、不落盘。
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string>? PreAuthorizedShellPatterns { get; set; }

    /// <summary>
    /// 本会话内用户点"记住同类命令"放行的 shell 命令模式（glob）。
    /// 随会话持久化；审批规则每次执行现取现用，追加无需重建装配。
    /// 工具级的"本会话总是允许"由框架审批状态承担，这里只管 shell 的命令粒度。
    /// </summary>
    public List<string> SessionApprovedShellPatterns { get; set; } = [];

    /// <summary>
    /// 记住一条会话级 shell 放行模式并立即持久化（重复添加忽略）。
    /// 加锁写、审批规则侧快照读——放行发生在用户交互线程，规则跑在运行线程。
    /// </summary>
    /// <param name="pattern">glob 模式</param>
    public void AddSessionApprovedShellPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;
        lock (SessionApprovedShellPatterns)
        {
            if (SessionApprovedShellPatterns.Contains(pattern)) return;
            SessionApprovedShellPatterns.Add(pattern);
        }

        SaveMeta();
    }

    /// <summary>
    /// 会话级 shell 放行模式的线程安全快照（审批规则读取用）
    /// </summary>
    /// <returns>模式列表副本</returns>
    public IReadOnlyList<string> SnapshotSessionApprovedShellPatterns()
    {
        lock (SessionApprovedShellPatterns)
        {
            return SessionApprovedShellPatterns.ToArray();
        }
    }

    /// <summary>所属角色</summary>
    [JsonIgnore]
    public CharacterData CharacterData =>
        _characterData ??= CharacterManager.Instance.GetCharacterData(CharacterId);

    /// <summary>
    /// 换绑角色。<b>不要只改 <see cref="CharacterId"/></b>——角色本体与记忆库都有缓存字段，
    /// 漏清就会出现"系统提示已经换了人、记忆库还挂在旧角色上"这种半换状态。
    ///
    /// 执行者不在此处重挂：装配快照含角色标识与重算的系统提示，
    /// 下一轮发送时挂接会自然重建，因此生成中换角色不会打断当前这一轮。
    /// </summary>
    /// <param name="character">新角色</param>
    public void ChangeCharacter(CharacterData character)
    {
        if (character.CharacterId == CharacterId) return;

        CharacterId = character.CharacterId;
        _characterData = character;
        // 用户手动挂过库时那份优先,不动;没挂过才让它回落到新角色自带的库
        if (string.IsNullOrEmpty(MemoryName)) _memory = null;
        SaveMeta(); //只动了头字段,历史一个字没改——Save() 会把整份历史重新序列化一遍
    }

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
            SaveMeta(); //同上:换知识库不动历史

        }
    }

    [JsonIgnore] private ModelRunningData? _modelRunningData;

    /// <summary>该对话是否有会话覆写（钉选了专属模型）</summary>
    [JsonIgnore]
    public bool HasSessionModelOverride => !string.IsNullOrEmpty(SessionModelName);

    /// <summary>
    /// 该对话实际问话的模型（有效模型）：运行期覆写 → 会话覆写名 → 全局当前模型。
    /// 会话覆写名在模型列表里找不到时回落全局，但名字保留（模型回来自动恢复）。
    /// </summary>
    [JsonIgnore]
    public ModelRunningData? ChatModelRunningData
    {
        get => _modelRunningData ?? FindModelByName(SessionModelName) ?? LlmManager.Instance.CurrentRunningModel;
        set
        {
            _modelRunningData = value;
            // 临时会话转正（Persist）走 JSON 落盘，JsonIgnore 的运行期覆写带不过去——
            // 在这里把名字一并盖到持久化字段上，转正后覆写不丢
            if (value != null) SessionModelName = value.ModelName;
        }
    }

    private static ModelRunningData? FindModelByName(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return null;
        return LlmManager.Instance.CacheModelDictionary.GetValueOrDefault(modelName);
    }

    /// <summary>首条消息时间;历史为空或该消息没有时间戳时回落会话创建时间</summary>
    [JsonIgnore]
    public DateTime FirstTime => LocalTimeOf(History.Count > 0 ? History[0] : null, CreatedAt);

    /// <summary>末条消息时间;历史为空或该消息没有时间戳时回落会话更新时间</summary>
    [JsonIgnore]
    public DateTime LastTime => LocalTimeOf(History.Count > 0 ? History[^1] : null, UpdatedAt);

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
        if (!string.IsNullOrEmpty(characterData.FirstGreeting)) AddNarration(characterData);
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
            SessionModelName = SessionModelName,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            MessageCount = History.Count,
            HasComposerDraft = !string.IsNullOrWhiteSpace(ComposerDraft),
            ParentSessionId = ParentSessionId,
            SubAgentType = SubAgentType,
            SubAgentName = SubAgentName,
            BackgroundReportPending = BackgroundReportPending,
            LastRunStartedAt = LastRunStartedAt,
            IsGroup = IsGroup,
            IsAgentGroup = IsAgentGroup,
            GroupId = GroupId,
        };
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
        History.Add(CreateMessage(role, message, imageBytes, imageMediaType));
        Save();
    }

    /// <summary>
    /// 追加开场白。它是一条<b>货真价实的 assistant 消息</b>——落盘、也供给模型，
    /// 只是带上旁白标记，界面据此居中展示而不是画成角色气泡。
    ///
    /// 判据是显式标记而非「历史里的第一条」：位置是会变的（删条目、分叉、裁剪），
    /// 而「这句是开场白」是写下它的那一刻就定死的事实。
    ///
    /// <b>public</b>：懒建路径（<c>ConversationSessionBinder.CreateAsync</c>）也要补——
    /// 否则选角色后首轮发送建出的会话没有开场白，模型第一轮会自我重介绍。
    /// </summary>
    /// <param name="characterData">开场白所属角色（参数替换要用它）</param>
    public void AddNarration(CharacterData characterData)
    {
        ChatMessage data = CreateMessage(ChatRole.Assistant, characterData.TryRender(characterData.FirstGreeting));
        data.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        data.AdditionalProperties[ChatMessageAnnotations.Narration] = true;

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
    /// 本会话的<b>唯一</b>执行者（惰性创建）。页面、快捷技能、调度等一切入口都必须经它运行，
    /// 一个会话绝不允许有第二个执行者——它内部对同会话的并发请求排队。
    /// 普通角色与智能体共用它，由角色的 <see cref="CharacterData.IsAgent"/> 决定装配形态。
    /// </summary>
    [JsonIgnore]
    public ICharacterRunner Runner => _runner ??= CharacterRunnerFactory.Instance.CreateRunner();

    /// <summary>
    /// 释放本会话的执行者（若从未创建则无事发生）。会话被删除或从缓存卸载时调用；
    /// 之后再次访问 <see cref="Runner"/> 会重新惰性创建。
    /// </summary>
    public async ValueTask DisposeRunnerAsync()
    {
        ICharacterRunner? runner = _runner;
        _runner = null;
        if (runner != null) await runner.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 流式生成一条回复，产出<b>完整内容</b>增量（正文、思考等）。
    /// 本轮的输入与输出由历史提供器统一写入历史，调用方不要预先把输入加进 <see cref="History"/>。
    /// 只要正文的调用方自行过滤 <see cref="TextContent"/>——不在这里额外开一条只出正文的口子，
    /// 否则取消补存那段逻辑就要跟着复制一份。
    /// </summary>
    /// <param name="input">本轮用户输入；为 null 表示基于现有历史重新生成</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>内容<b>增量</b>流</returns>
    public async IAsyncEnumerable<AIContent> GenerateCompletionStreamingContent(ChatMessage? input = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (input == null && Count == 0)
        {
            yield return new TextContent("Error: No message");
            yield break;
        }

        await Runner.AttachAsync(this, cancellationToken).ConfigureAwait(false);

        List<ChatMessage> turnInput = input == null ? [] : [input];
        StringBuilder finalText = StringBuilderPool.Get();
        try
        {
            await foreach (AIContent content in Runner.RunAsync(turnInput, cancellationToken)
                               .ConfigureAwait(false))
            {
                // 只在本地累积一份正文用于取消时补存,对外透出的仍是增量
                if (content is TextContent { Text.Length: > 0 } text) finalText.Append(text.Text);
                yield return content;
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
    /// 立即全量保存(头文件 + 历史整写)。这是默认路径——历史、编辑等有价值数据
    /// 不能坐在任何延迟窗里等崩溃/强杀,只有低价值高频的偏好类字段才允许用 <see cref="SaveDebounced"/>。
    /// </summary>
    public void Save()
    {
        SessionManager.Instance.Save(this);
    }

    /// <summary>
    /// 只保存会话头(标题/参数/统计等),不动历史文件
    /// </summary>
    /// <param name="touchUpdatedAt">是否刷新 UpdatedAt 并通知列表重排(草稿落盘时传 false)</param>
    public void SaveMeta(bool touchUpdatedAt = true)
    {
        SessionManager.Instance.SaveMeta(this, touchUpdatedAt);
    }

    /// <summary>
    /// 追加保存:把 History 中自 fromIndex 起的新消息追加进历史文件并刷新会话头。
    /// 轮次结束的常规落盘走这里,成本与会话长度无关。
    /// </summary>
    /// <param name="fromIndex">新消息在 History 中的起始下标</param>
    public void SaveAppended(int fromIndex)
    {
        SessionManager.Instance.Append(this, fromIndex);
        // 落了盘的那一段不必再给中途挂上来的观察者补发:它从历史里读得到,补发就是渲染两遍
        LiveTurn.NoteHistoryPersisted();
        HistoryAppended?.Invoke(fromIndex);
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
    /// 清空历史。框架附加状态(todos/mode/审批)一并删除——它是围绕这段历史建立的，
    /// 留着会让下次挂接把旧任务清单读回来，出现「历史空了但清单还在」。
    /// </summary>
    public void Clear()
    {
        History.Clear();
        Save();
        SessionManager.Instance.DeleteAgentState(SessionId);
    }

    private string AuthorNameOf(ChatRole role)
    {
        if (role == ChatRole.User) return CharacterManager.Instance.UserCharacterName;
        if (role == ChatRole.System) return "System";
        return CharacterData.CharacterName;
    }

    /// 缺时间戳时回落到调用方给的会话级时间戳,<b>不能回落"现在"</b>——
    /// 那会让同一条旧消息每次读到不同的时间(旧存档里的消息确实可能没有时间戳)
    private static DateTime LocalTimeOf(ChatMessage? message, DateTimeOffset fallback)
    {
        return (message?.CreatedAt ?? fallback).LocalDateTime;
    }
}
