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

    /// <summary>在无头界面线程上跑一段测试体</summary>
    /// <param name="body">测试体</param>
    public static void Run(Action body) =>
        Session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
}
