using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace UiharuMind.Features.ScreenCapture.Ocr;

/// <summary>
/// 图片文字识别（带行包围盒）。平台差异关在实现里，调用方只认这个接口：
/// 用 <see cref="OcrRecognizerFactory.IsSupported"/> 决定是否展示入口，
/// 用 <see cref="OcrRecognizerFactory.Create"/> 拿实例，业务代码不写平台分支。
/// </summary>
public interface IOcrTextRecognizer
{
    /// <summary>
    /// 识别图片中的文字行（阅读顺序）。<b>只读取</b>传入的位图，不接管、不释放。
    /// 识别引擎需要什么中间格式（临时文件等）由实现自己张罗。
    /// </summary>
    /// <param name="image">要识别的图</param>
    /// <param name="cancellationToken">取消令牌（原生识别中途不可打断，仅用于排队阶段）</param>
    /// <returns>识别出的行；无文字或失败返回空列表，不抛异常</returns>
    Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default);
}
