namespace UiharuMind.Core.AI;

/// <summary>
/// 当前请求上下文的载体：<b>全仓唯一持有那个 <see cref="AsyncLocal{T}"/> 的地方</b>。
///
/// 只有两个入口——<see cref="Current"/> 读、<see cref="Push"/> 在 <c>using</c> 作用域内写。
/// 没有可写的静态属性：想改一份上下文就得拿出一份完整的新快照，作用域一结束自动还原。
/// 这样「设了忘记复位」在类型层面就写不出来，而那种漏复位恰好是最难查的一类串号
/// （上一发的思考正文被贴进下一发的请求体，网关报的是「历史对不上」，与真正的原因隔着两层）。
///
/// 读的一方只有两处：`RequestRewrite.For(model)`（改写清单）与 OpenAI 兼容层的 HTTP 处理器（亲和头）。
/// 它们拿到的 <see cref="LlmRequestContext.Empty"/> 表示没有场景在发请求，不是「某个字段缺失」。
/// </summary>
public static class LlmRequestScope
{
    private static readonly AsyncLocal<LlmRequestContext?> _current = new();

    /// <summary>当前异步流上的上下文；没有时是 <see cref="LlmRequestContext.Empty"/></summary>
    public static LlmRequestContext Current => _current.Value ?? LlmRequestContext.Empty;

    /// <summary>
    /// 挂上一份上下文，返回的作用域释放时还原成挂之前的那份（含「之前没有」）。
    ///
    /// <b>还原的是整份引用，不是逐个字段</b>：嵌套时外层 push 的值因此原样回来，
    /// 而不是被内层的某一个字段悄悄覆盖掉。也因为这个，内层应该用
    /// <c>Current with { … }</c> 只替换自己关心的字段，别从 <see cref="LlmRequestContext.Empty"/> 重建——
    /// 那会把外层设的东西一起抹掉。
    /// </summary>
    /// <param name="context">要挂上去的上下文</param>
    /// <returns>释放即还原的作用域</returns>
    public static Scope Push(LlmRequestContext context) => new(_current.Value, context);

    /// <summary>一次挂载的生命周期，<c>using</c> 结束即还原</summary>
    public sealed class Scope : IDisposable
    {
        private readonly LlmRequestContext? _previous;
        private bool _disposed;

        internal Scope(LlmRequestContext? previous, LlmRequestContext context)
        {
            _previous = previous;
            _current.Value = context;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
