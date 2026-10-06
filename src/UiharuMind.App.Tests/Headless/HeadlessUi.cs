using Avalonia.Headless;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 无头界面测试的入口：把测试体搬到 Avalonia 的调度线程上跑。
///
/// <b>为什么不用官方的 <c>Avalonia.Headless.XUnit</c></b>（那个包提供现成的 <c>[AvaloniaFact]</c>）：
/// 它最新的 12.1.2 是对着 <c>xunit.v3.extensibility.core 3.2.2</c> 编的，而本仓用的是 4.0.1，
/// 它的 discoverer 一跑就 <c>MissingMethodException</c>（<c>TestIntrospectionHelper.GetTestCaseDetails</c>
/// 签名变了）。为一个特性标记把整套测试栈降级不划算，而这里要接的其实只有一件事——
/// 「在会话的调度线程上执行」。等官方跟上 4.x 就可以把这层去掉，测试体一行都不用改。
///
/// 会话按程序集起一份（认 <c>AvaloniaTestApplicationAttribute</c>），但<b>每次 Dispatch
/// 会新建一个应用实例</b>，所以测试之间不共享控件树与静态视觉状态。
/// </summary>
internal static class HeadlessUi
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessUi).Assembly);

    private const int SetupAttempts = 3;

    /// <summary>在无头界面线程上跑一段测试体</summary>
    /// <param name="body">测试体</param>
    public static void Run(Action body) => DispatchWithSetupRetry(() => Session.Dispatch(body, CancellationToken.None));

    /// <summary>在无头界面线程上跑一段异步测试体，等它连同回到界面线程的续体一起跑完</summary>
    /// <param name="body">测试体</param>
    public static void RunAsync(Func<Task> body) =>
        DispatchWithSetupRetry(() => Session.Dispatch(async () =>
        {
            await body();
            return true;
        }, CancellationToken.None));

    // 每次 Dispatch 都会重置 Dispatcher.UIThread 再建应用；重置后谁先碰它谁就是界面线程。
    // 前面测试留下的后台续体（异步加载、静态事件里的 Post）偶尔恰好在这一刻碰到它，
    // 建应用就撞「The calling thread cannot access this object」——测试体一行都还没跑。
    // 只重试这种建应用阶段的失败；测试体里抛的照常报错
    private static void DispatchWithSetupRetry(Func<Task> dispatch)
    {
        for (int attempt = 1;; attempt++)
        {
            try
            {
                dispatch().GetAwaiter().GetResult();
                return;
            }
            catch (InvalidOperationException e) when (attempt < SetupAttempts && IsSetupAffinityFailure(e))
            {
                Thread.Sleep(50);
            }
        }
    }

    private static bool IsSetupAffinityFailure(InvalidOperationException e) =>
        e.StackTrace?.Contains("HeadlessUnitTestSession.EnsureIsolatedApplication", StringComparison.Ordinal) == true;
}
