using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// shell 调用的兜底时限：到点还没返回，就先把一句说明交回给模型，不再陪着干等。
///
/// [MFA绕坑] 绕:外面再套一层时限 因:stateless 模式进程退出后调无参 <c>WaitForExit()</c> 等输出管道关闭，
/// 不受 Timeout 管——`cd x &amp;&amp; nohup y &gt; log &amp;` 这种只重定向了最后一段的后台写法，
/// 后台子 shell 一直占着管道，调用就永远不返回（实测成员卡了 29 分钟，全群等他）
/// 删除条件:框架那句 WaitForExit 带上时限或改成只等进程本身
/// </summary>
internal sealed class ShellHangGuardFunction : DelegatingAIFunction
{
    internal const string HangNotice =
        "命令本身已经结束或超时，但它放到后台的进程还占着输出管道，这次调用没等到输出收尾就先返回了，输出没取回来。" +
        "后台跑的任务要把整串的输出都重定向走，例如 `(cmd1 && cmd2) > log 2>&1 &`，之后用 `tail log` 看进度。";

    private readonly TimeSpan _limit;

    public ShellHangGuardFunction(AIFunction innerFunction, TimeSpan limit) : base(innerFunction)
    {
        _limit = limit;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // 放到线程池上跑：命令瞬间退出时框架会在同步段里就卡进 WaitForExit，不挪走的话连时限都等不到
        Task<object?> call = Task.Run(() => base.InvokeCoreAsync(arguments, cancellationToken).AsTask(), CancellationToken.None);
        Task finished = await Task.WhenAny(call, Task.Delay(_limit, cancellationToken)).ConfigureAwait(false);
        if (finished == call) return await call.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        _ = call.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); //后台进程收尾后它自己会结束，异常别漏成未观察
        return HangNotice;
    }
}
