using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 一次服务调用<b>发出之前</b>：此后流出来的内容属于新的一发。
///
/// <para>存在的理由：<see cref="MessageBoundaryContent"/> 是落盘那一刻的异步回调，实测会
/// 晚于下一次调用的首条内容。拿它复位解析器的调用级状态，要么清掉下一发刚建立的状态
/// （正文里的标签引用又被当控制标签），要么干脆不复位（状态跨发残留，换到本地内联模型后
/// 思考永远不折叠）。两难的根因是信号不可靠，不是复位规则能调好的。</para>
///
/// <para>由 <c>ServiceCallSignalingChatClient.Starting</c> 在发请求前同步写进
/// <c>HarnessCharacterRunner</c> 的通道里，不迟到。落盘信号仍只管收段与回填裁剪。</para>
///
/// <para>不进历史、不回喂模型：它不经过框架的 <c>ChatResponseUpdate.Contents</c>。</para>
/// </summary>
public sealed class ServiceCallStartedContent : AIContent
{
    /// <summary>无状态，共用一个实例即可</summary>
    public static readonly ServiceCallStartedContent Instance = new();

    private ServiceCallStartedContent()
    {
    }
}
