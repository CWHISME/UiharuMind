using System.Runtime.CompilerServices;

namespace UiharuMind.Core.AI;

/// <summary>
/// 当前请求上下文的载体：<b>全仓唯一持有那个 <see cref="AsyncLocal{T}"/> 的地方</b>。
///
/// 三个入口——<see cref="Current"/> 读、<see cref="Push"/> 在 <c>using</c> 作用域内写、
/// <see cref="Bind{T}"/> 把一份上下文逐次挂到异步流上（迭代器跨 <c>yield</c> 会丢 <see cref="AsyncLocal{T}"/>，
/// 一个外层 <c>using</c> 覆盖不了重发）。
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

    /// <summary>
    /// 让 <paramref name="source"/> 的<b>每一次</b> <c>MoveNextAsync</c> 都跑在 <paramref name="context"/> 之下。
    ///
    /// <para>
    /// 为什么不是「枚举外面套一个 using」：<c>AsyncLocal</c> 在异步迭代器里<b>跨 yield 不保留</b>——
    /// 写下的值只在当轮 MoveNext 有效，下一次 MoveNext 回到调用方的执行上下文（实测；
    /// 表现为重发请求丢了会话亲和头，见 ADR 0099 修订）。把 push 放进每一次 MoveNext，
    /// 作用域开合与它要覆盖的那次调用同生共死，重发天然被覆盖，既不依赖编译器怎么调度续体，
    /// 也不会把值漏给调用方。
    /// </para>
    /// <para>异常原样穿过（本方法只加作用域，不解释失败）。</para>
    /// <para>
    /// 作用域的边界就是 <c>MoveNextAsync</c> 本身：枚举器自己的 <c>DisposeAsync</c> 不在里面——
    /// 那一步不读请求上下文（当前的 HTTP 层与清洗层都只在自己的 SendAsync/读路径里读），
    /// 真要在释放时读，就得把它也包进来。
    /// </para>
    /// </summary>
    /// <param name="context">这一发要挂的上下文快照</param>
    /// <param name="source">被绑定的流</param>
    /// <param name="cancellationToken">取消</param>
    /// <typeparam name="T">流里的元素</typeparam>
    /// <returns>每步都带着上下文的流</returns>
    public static async IAsyncEnumerable<T> Bind<T>(LlmRequestContext context, IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<T> enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool moved;
                using (Push(context)) moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                if (!moved) yield break;
                yield return enumerator.Current;
            }
        }
    }

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
