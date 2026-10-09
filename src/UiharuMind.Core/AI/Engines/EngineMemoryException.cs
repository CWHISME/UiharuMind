namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 内存差多少：估的占用与此刻可用
/// </summary>
/// <param name="EstimatedBytes">估的占用（字节）</param>
/// <param name="AvailableBytes">此刻可用内存（字节）</param>
public sealed record EngineMemoryShortfall(long EstimatedBytes, long AvailableBytes);

/// <summary>
/// 拉起前按此刻可用内存估过，起了多半把系统压到换页甚至重启，所以没起。
/// 没人看着的调用（智能体出图）按「这一家用不了」处理；有人看着的入口问过用户后可以带上「照样起」再来一次
/// </summary>
/// <param name="serverName">服务名</param>
/// <param name="shortfall">差多少</param>
public sealed class EngineMemoryException(string serverName, EngineMemoryShortfall shortfall)
    : Exception($"Not enough free memory to start {serverName}: needs about {shortfall.EstimatedBytes / Gib:0.0} GB, " +
                $"{shortfall.AvailableBytes / Gib:0.0} GB available. Close other models or apps and try again.")
{
    private const double Gib = 1024d * 1024 * 1024;

    /// <summary>
    /// 差多少
    /// </summary>
    public EngineMemoryShortfall Shortfall { get; } = shortfall;
}
