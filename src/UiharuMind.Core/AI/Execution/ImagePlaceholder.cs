namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 模型看不到图时写进正文的那句话。<b>投影（Read 预览）与降级（历史里的内联附件）共用这一处</b>：
/// 同一件事两种措辞，模型会在同一份历史里读到两套说法。
/// </summary>
internal static class ImagePlaceholder
{
    private const string Subject = "this model cannot read images";

    /// <summary>
    /// 被省掉的图片的占位文本
    /// </summary>
    /// <param name="count">省掉了几张；多张合成一句，不必逐张重复</param>
    /// <param name="path">图片路径；没有（或也只能给路径而无从解析）时为 null</param>
    /// <returns>一行占位文本</returns>
    public static string Omitted(int count, string? path)
    {
        string what = count <= 1 ? "Image omitted" : $"{count} images omitted";
        return path is { Length: > 0 } ? $"[{what}: {Subject} — {path}]" : $"[{what}: {Subject}]";
    }
}
