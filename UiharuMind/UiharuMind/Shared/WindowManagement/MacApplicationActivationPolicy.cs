using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.WindowManagement;

/// <summary>
/// macOS 激活策略：透传给 <see cref="MacApplicationActivationService"/>。
/// 平台判断仍由服务内部承担（非 Mac 上调用即 no-op）。
/// </summary>
public sealed class MacApplicationActivationPolicy : IApplicationActivationPolicy
{
    /// <inheritdoc />
    public void SetRegularMode(bool isRegular) => MacApplicationActivationService.SetRegularMode(isRegular);

    /// <inheritdoc />
    public void ActivateIgnoringOtherApps() => MacApplicationActivationService.ActivateIgnoringOtherApps();
}