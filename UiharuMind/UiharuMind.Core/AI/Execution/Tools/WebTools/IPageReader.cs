/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 单个读取器的结果:正文或失败原因二者居其一。失败原因会被兜底链汇总,
/// 全链走空时一并交给模型——只回一句"读取失败"，模型没法判断该换 URL 还是换策略。
/// </summary>
/// <param name="Content">正文,失败时为 null</param>
/// <param name="Error">失败原因,成功时为 null</param>
internal readonly record struct PageReadResult(string? Content, string? Error)
{
    public static PageReadResult Ok(string content) => new(content, null);

    public static PageReadResult Fail(string error) => new(null, error);
}

/// <summary>
/// 网页正文读取的一环。实现只管"把这个 URL 读成纯文本",截断与兜底不归它管。
/// </summary>
internal interface IPageReader
{
    /// <summary>读取器名称,用于日志与错误汇总</summary>
    string Name { get; }

    /// <summary>
    /// 本读取器是否受理这个地址。返回 false 表示"不适用"而非"失败",兜底链会安静跳过,
    /// 不计入失败原因——例如借道第三方服务的读取器不该碰内网地址。
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <returns>受理返回 true</returns>
    bool CanRead(string url) => true;

    /// <summary>
    /// 读取网页正文
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>正文或失败原因</returns>
    Task<PageReadResult> ReadAsync(string url, CancellationToken ct);
}
