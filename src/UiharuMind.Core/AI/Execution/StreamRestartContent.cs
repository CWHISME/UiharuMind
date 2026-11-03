using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 这一发要<b>整段重来</b>：此前属于它的内容全部作废，界面该把它们摘掉再重新画。
///
/// <para>
/// 断线重试与 <c>max_tokens</c> 自愈都会把同一份请求再发一次，而两次尝试在内容流里是
/// <b>连着</b>的——没有任何天然分隔。界面按追加语义装配，于是第二发的内容会续进第一发
/// 已经画出来的思考卡/气泡里：轻则同一段思考出现两遍，重则先收段再重开，多出一张同样的卡。
/// 实际后果比观感严重：那一发的工具调用一次都不会执行（框架要等流走完才执行），
/// 所以留在界面上的半截卡片本就该消失。
/// </para>
/// <para>
/// 与 <see cref="MessageBoundaryContent"/> 同一条路：随流走、由转录器认领。
/// <b>不进历史、不回喂模型</b>——落盘前由 <see cref="SessionChatHistoryProvider"/> 摘掉。
/// </para>
/// </summary>
public sealed class StreamRestartContent : AIContent
{
    /// <summary>无状态，共用一个实例即可</summary>
    public static readonly StreamRestartContent Instance = new();

    private StreamRestartContent()
    {
    }
}
