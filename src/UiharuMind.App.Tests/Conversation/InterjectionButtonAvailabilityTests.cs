using CommunityToolkit.Mvvm.Input;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 插话按钮在流式输出期间被置灰的根因回归。
///
/// Avalonia 的 <see cref="Avalonia.Controls.Button"/> 会把 Command 的 CanExecute 并入自身的
/// IsEnabledCore（<c>base.IsEnabledCore &amp;&amp; _commandCanExecute</c>），所以哪怕把按钮的
/// IsEnabled 绑定去掉，只要 Command.CanExecute 返回 false，按钮照样禁。
/// 而 AsyncRelayCommand <b>默认禁并发</b>：自己还在执行（IsRunning）时 CanExecute 返回 false。
/// 「流式输出中想插话」= 再次点发送按钮 = 同一个 SendMessage 命令还在跑 = 按钮被禁，正好卡死。
/// 修复：SendMessage 用 <c>AllowConcurrentExecutions</c>，执行期间 CanExecute 恒 true。
/// 下面两条把框架的这个行为钉死，防止有人把该选项撤回去。
/// </summary>
public class InterjectionButtonAvailabilityTests
{
    [Fact]
    public async Task AsyncRelayCommand_ByDefault_DisablesItselfWhileRunning()
    {
        AsyncRelayCommand command = new(async () => await Task.Delay(200));
        Assert.True(command.CanExecute(null));

        Task running = command.ExecuteAsync(null);
        // 默认禁并发:自己还在跑,CanExecute 就到 false——Avalonia 按钮因此置灰
        Assert.False(command.CanExecute(null));

        await running;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncRelayCommand_AllowConcurrentExecutions_StaysEnabledWhileRunning()
    {
        AsyncRelayCommand command = new(
            async () => await Task.Delay(200),
            AsyncRelayCommandOptions.AllowConcurrentExecutions);

        Task running = command.ExecuteAsync(null);
        // 允许并发后,执行期间仍可再次触发——插话入口必须保持可点
        Assert.True(command.CanExecute(null));

        await running;
    }
}