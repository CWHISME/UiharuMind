namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 图不在正文里时写进去的那句话。<b>三种成因共用这一处</b>，但措辞必须分开：
/// 模型看不了图（投影、降级）与「因为请求太大被裁掉」（预算）要让它做的事完全不同——
/// 前者只能转交别的工具，后者还能自己把文件再读一遍。同一件事两套说法，模型会在同一份历史里读到两种口径。
/// </summary>
internal static class ImagePlaceholder
{
    private const string Subject = "this model cannot read images";

    /// <summary>
    /// 被省掉的图片的占位文本（模型看不了图）
    /// </summary>
    /// <param name="count">省掉了几张；多张合成一句，不必逐张重复</param>
    /// <param name="path">图片路径；没有（或也只能给路径而无从解析）时为 null</param>
    /// <returns>一行占位文本</returns>
    public static string Omitted(int count, string? path)
    {
        string what = count <= 1 ? "Image omitted" : $"{count} images omitted";
        return path is { Length: > 0 } ? $"[{what}: {Subject} — {path}]" : $"[{what}: {Subject}]";
    }

    /// <summary>
    /// 因为请求体超过图片预算而被裁掉的占位文本。
    ///
    /// <b>不能沿用 <see cref="Omitted"/> 的措辞</b>：那句说的是「这个模型读不了图」，
    /// 而这里模型明明能读，只是这一张不再随请求发送。照抄会让它以为自己没有视觉能力，
    /// 于是不再看图、也不再去读——白白丢掉一个还能用的手段。
    /// 所以这里写明成因，并明确给它后路：别凭记忆断言内容，需要就重新读
    /// </summary>
    /// <param name="count">裁掉了几张；多张合成一句</param>
    /// <returns>一行占位文本</returns>
    public static string BudgetTrimmed(int count) => count <= 1
        ? "[Image omitted: the request exceeded its size budget, so it is no longer attached. " +
          "Do not describe it from memory — read the file again if you need it.]"
        : $"[{count} images omitted: the request exceeded its size budget, so they are no longer attached. " +
          "Do not describe them from memory — read the files again if you need them.]";
}
