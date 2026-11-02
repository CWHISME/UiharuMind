using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI;

/// <summary>
/// 一次服务调用<b>实际解析出来的模型</b>。挂在 <see cref="LlmRequestContext"/> 上，
/// 由 <c>LazyChatClient</c> 解析完就写进来，用量记账在收口时读它。
///
/// <para>
/// 为什么要有这个口：记账侧原先自己再解析一次（<c>TurnDriver.ResolveUsageIdentity</c>），
/// 与真正发请求的那次解析是两条独立的路。会话模型在运行中被改过、或者两次解析之间全局模型变了，
/// 两条路就会给出不同的答案——账记成另一个模型，费用按另一个供应商的价目算。
/// 这里让「发了什么」只有一个答案：请求路径解析出什么，账就记什么。
/// </para>
/// <para>
/// 为什么是可变对象：与 <see cref="LlmRetryTally"/> 同一个理由——<see cref="LlmRequestContext"/>
/// 是不可变快照，AsyncLocal 里写下的值不向调用方回流，引用可以。
/// </para>
/// </summary>
public sealed class LlmServedModel
{
    private ModelRunningData? _model; //本次请求实际解析出的模型

    /// <summary>本次请求实际解析出的模型；还没解析出来时为 null</summary>
    public ModelRunningData? Model => Volatile.Read(ref _model);

    /// <summary>记下这次请求解析出的模型</summary>
    /// <param name="model">发请求的那个模型</param>
    public void Note(ModelRunningData model) => Volatile.Write(ref _model, model);
}
