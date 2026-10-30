namespace UiharuMind.Core.AI;

/// <summary>
/// 一次请求的上下文<b>快照</b>——不可变，只在整份构造好之后交给
/// <see cref="LlmRequestScope"/> 挂到异步流上，下一发请求再构造新的一份。
///
/// 为什么必须走环境（AsyncLocal）而不是参数：模型客户端由 OpenAI SDK 封装，
/// 从 <c>ChatOptions</c> 到 <c>PipelineMessage</c> 之间没有可插入的通道，
/// 而改写发生在管道最里层，拿不到调用栈上的局部变量。这是唯一的传法。
///
/// 但「只能走环境」不等于「谁都能随时改」。快照 + 作用域带来三条约束：
/// 读的人只看到一份完整状态（不会读到一半新一半旧）；写的人用一个 <c>using</c> 交出控制权，
/// 作用域结束自动还原成进来时的那份（不会漏复位，也不会把外层的值一起抹掉）；
/// 嵌套时后者只替换自己关心的字段，其余沿用外层（见 <see cref="LazyChatClient"/> 的用法）。
/// </summary>
public sealed record LlmRequestContext
{
    /// <summary>什么都没有：没有场景在发请求时的取值</summary>
    public static LlmRequestContext Empty { get; } = new();

    /// <summary>
    /// 本次请求禁止调用工具（写入 <c>tool_choice: "none"</c>）。
    ///
    /// 用于写交接文档那种旁路请求：它要的是纯文本产出，但<b>工具定义必须照常带上</b>——
    /// 工具在请求体里紧跟 system 之后，缺了它请求前缀从一开始就与常规轮次岔开，
    /// 服务端的前缀缓存整个作废，而那一发恰好是最大的一次请求。
    ///
    /// 不走 <c>ChatOptions</c>：MEAI 的 <c>ChatToolMode</c> 只有 Auto / RequireAny / RequireSpecific，
    /// <b>没有 None</b>，抽象层表达不了「带着工具但不许调」。
    /// </summary>
    public bool ForbidToolCalls { get; init; }

    /// <summary>
    /// 本次请求的<b>会话亲和</b>值，取会话自己的（<see cref="Chat.ChatSession.CacheAffinity"/>；
    /// 子会话为什么不继承父会话，理由在那边）。
    ///
    /// 由 <see cref="Execution.LazyChatClient"/> 每发请求前落地——绑在客户端实例上，
    /// 不靠调用方在流程里设一次（那样子会话、群背景、压缩这些旁路各要补一遍，漏一个就静默失效）；
    /// HTTP 层读它写请求头，头名由供应商配置给（<see cref="Models.ILlmModel.SessionAffinityHeaders"/>）。
    /// </summary>
    public string? SessionAffinity { get; init; }

    /// <summary>
    /// 这一发<b>看见了加密思考</b>时通知一次，交出票据原文（流里出现 <c>reasoning.encrypted</c> 条目）。
    ///
    /// 由 <see cref="Execution.LazyChatClient"/> 每发请求挂上，HTTP 层观测到就调。
    /// 存在的理由有两个：
    /// ① 加密思考的正文永远不会下发，而"它正在思考"这件事只在流里一闪而过——
    /// 没有这个信号，思考卡只能等落盘那一刻补，界面上整段思考期是空的；
    /// ② 票据要原样存下来，下一发原样带回（密文不能解析后再编码）。
    ///
    /// 传的是回调而不是标志位：回调是同一个对象，HTTP 层在深处调它，调用方拿得到结果——
    /// 而 <c>AsyncLocal</c> 里写下的值不会向调用方回流，标志位那样传不出来。
    /// </summary>
    public Action<byte[]>? EncryptedReasoningObserved { get; init; }
}
