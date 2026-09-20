using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Shared.WindowManagement;

/// <summary>
/// 按当前平台创建应用激活策略。
/// </summary>
public static class ApplicationActivationPolicyFactory
{
    /// <summary>
    /// 创建当前平台的应用激活策略。
    /// </summary>
    /// <returns>macOS 上是 Mac 实现，其它平台是 no-op 默认实现</returns>
    public static IApplicationActivationPolicy Create()
    {
        return PlatformUtils.IsMacOS ? new MacApplicationActivationPolicy() : new DefaultApplicationActivationPolicy();
    }
}