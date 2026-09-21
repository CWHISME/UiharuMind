namespace UiharuMind.Shared.Windows.Focus;

/// <summary>
/// 窗口「怎么取焦点」的平台差异。窗口基类只按生命周期调这几个钩子，具体做什么由实现决定，
/// 平台相关的原生手段不进基类。每个窗口一个实例——实现可能要记录窗口自己的状态。
/// </summary>
public interface IWindowFocusBehavior
{
    /// <summary>Show 之前。</summary>
    /// <param name="window">目标窗口</param>
    void PrepareShow(UiharuWindowBase window);

    /// <summary>Show 之后、取焦点之前。</summary>
    /// <param name="window">目标窗口</param>
    void AfterShow(UiharuWindowBase window);

    /// <summary>让窗口取得焦点。</summary>
    /// <param name="window">目标窗口</param>
    void Focus(UiharuWindowBase window);

    /// <summary>关闭或隐藏之前。</summary>
    /// <param name="window">目标窗口</param>
    void PrepareClose(UiharuWindowBase window);
}
