namespace UiharuMind.Shared.WindowManagement;

/// <summary>
/// 非 macOS 的默认激活策略：no-op。
/// 与「非 Mac 上 MacApplicationActivationService 直接 no-op」的现状等价，
/// 保证应用在 Windows/Linux 上行为不变。
/// </summary>
public sealed class DefaultApplicationActivationPolicy : IApplicationActivationPolicy
{
    /// <inheritdoc />
    public void SetRegularMode(bool isRegular)
    {
    }

    /// <inheritdoc />
    public void ActivateIgnoringOtherApps()
    {
    }
}