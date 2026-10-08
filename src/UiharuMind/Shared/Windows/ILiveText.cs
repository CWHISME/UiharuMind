using System;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 边生成边长的正文（流式的思考过程）。文本窗只认这个接口，不认识具体的条目
/// </summary>
public interface ILiveText
{
    /// <summary>
    /// 订阅增量并取当下全文。两步须在同一锁内完成：增量落在快照之前已含在全文里，落在之后会经回调补上
    /// </summary>
    /// <param name="onAppend">增量接收者（界面线程调用）</param>
    /// <returns>当下全文</returns>
    string Subscribe(Action<string> onAppend);

    /// <summary>
    /// 退订
    /// </summary>
    /// <param name="onAppend">与订阅时相同的委托</param>
    void Unsubscribe(Action<string> onAppend);
}
