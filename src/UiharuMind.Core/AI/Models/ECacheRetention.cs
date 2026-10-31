namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 提示缓存的保留时长。三档而不是布尔：Anthropic 的 <c>ephemeral</c> 默认约 5 分钟，
/// 长空闲间隔的会话每轮都会冷一次；给到 <c>1h</c> 要服务端认（各家支持情况不同），
/// 而 <c>None</c> 是「一个断点都不打」——不是所有场景都希望缓存留痕。
///
/// <para>只有<b>显式断点型</b>的线（Anthropic Messages）需要它：自动前缀型的服务端自己决定存多久，
/// 客户端说了不算。</para>
/// </summary>
public enum ECacheRetention
{
    /// <summary>不打断点</summary>
    None,

    /// <summary>默认寿命（约 5 分钟）</summary>
    Short,

    /// <summary>延长到 1 小时；服务端不支持时由各线自行降级</summary>
    Long,
}
