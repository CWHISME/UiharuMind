namespace UiharuMind.Features.Audio;

/// <summary>
/// 听写出来的文字送去哪：本应用的输入框（插到光标处）；以后在别的应用里用，就是「粘贴到前台应用」另一种去向
/// </summary>
public interface IDictationTarget
{
    /// <summary>
    /// 现在能不能往里送（控件还在、窗口在前台）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 送进去（界面线程上调）
    /// </summary>
    /// <param name="text">识别出的文字</param>
    void Insert(string text);
}
