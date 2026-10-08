using System.Threading.Tasks;
using Avalonia.Input;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 临时借用剪贴板要的三件事：记下原样、放进文字、还原
/// </summary>
public interface IPasteboard
{
    /// <summary>
    /// 记下剪贴板现在的内容
    /// </summary>
    /// <returns>原样的一份拷贝；空的或取不到为 null</returns>
    Task<DataTransfer?> SaveAsync();

    /// <summary>
    /// 放进一段文字
    /// </summary>
    /// <param name="text">文字</param>
    Task SetTextAsync(string text);

    /// <summary>
    /// 还原成记下的样子
    /// </summary>
    /// <param name="saved">记下的；null 为清空</param>
    Task RestoreAsync(DataTransfer? saved);
}
