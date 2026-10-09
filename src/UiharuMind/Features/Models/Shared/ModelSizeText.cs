using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.Models.Shared;

/// <summary>
/// 模型列表里本地模型的磁盘大小写法（对话、语音、生图三个页签共用）
/// </summary>
public static class ModelSizeText
{
    /// <summary>
    /// 大小文字
    /// </summary>
    /// <param name="bytes">字节数</param>
    /// <returns>如「4.7 GB」；不大于 0 为空</returns>
    public static string Format(long bytes) => bytes > 0 ? GameUtils.FormatBytes(bytes) : "";

    /// <summary>
    /// 在说明后面接上大小
    /// </summary>
    /// <param name="text">说明</param>
    /// <param name="bytes">字节数；不大于 0 不接</param>
    /// <returns>「说明 · 大小」</returns>
    public static string Join(string text, long bytes) => bytes > 0 ? $"{text} · {Format(bytes)}" : text;
}
