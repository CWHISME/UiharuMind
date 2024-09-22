namespace UiharuMind.Core.Core.UiharuScreenCapture;

public interface IScreenCapture
{
    /// <summary>
    /// 截取指定的整个屏幕
    /// </summary>
    /// <param name="screenId"></param>
    /// <returns></returns>
    public Task Capture(int screenId);
}