namespace UiharuMind.Shared.WindowManagement;

/// <summary>
/// 应用级激活策略：窗口管理器只调这几个钩子，平台差异由实现决定。
/// 收敛 UIManager 里仅有的两处 Mac 平台调用，默认实现保持非 Mac 平台行为不变。
/// </summary>
public interface IApplicationActivationPolicy
{
    /// <summary>按当前可见主窗口情况设置应用激活策略（Mac 的 activationPolicy 转换）。</summary>
    /// <param name="isRegular">是否按常规应用（可被 Dock/切换器命中）呈现</param>
    void SetRegularMode(bool isRegular);

    /// <summary>把应用抬到前台（忽略其它 App 的激活请求）。</summary>
    void ActivateIgnoringOtherApps();
}