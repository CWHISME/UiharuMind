using System.Linq;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// 接口地址与 wire 格式的互转。**地址是唯一事实**：格式从地址尾段反推，
/// 而不是另存一个可能和地址打架的字段。
///
/// <para>早先两处各说各话：目录把格式推成 Messages、地址却还是 <c>/chat/completions</c>，
/// 结果是 Messages 形状的请求体打到 chat 端点——404/400，而且从界面上看不出来。
/// 现在添加模型页给的候选地址就是按格式拼好的完整 URL，选哪个地址就是哪个格式。</para>
///
/// <para>后缀表仍只在 <see cref="WireProtocols"/> 里写一份，这里只做换算。</para>
/// </summary>
public static class RemoteEndpoints
{
    /// <summary>三条线，按 chat / responses / messages 顺序</summary>
    public static IReadOnlyList<EApiFormat> All { get; } = [.. WireProtocols.All.Select(protocol => protocol.Format)];

    /// <summary>
    /// 地址属于哪条线；<b>认不出返回 null</b>。
    ///
    /// 认不出与 chat 不是一回事：<c>https://host/v1</c> 这种只填到基址的地址，
    /// 运行时按格式补后缀（<see cref="WireProtocol.Resolve"/>），所以它可以是任何一条线——
    /// 这里若擅自判成 chat，编辑窗口一存就会把 messages 的模型翻成 chat。
    /// </summary>
    /// <param name="address">完整接口地址</param>
    /// <returns>尾段对应的格式；认不出为 null</returns>
    public static EApiFormat? FormatOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        // 尾斜杠会让尾段比对落空（Detect 比的是 AbsolutePath），复制粘贴与代理重写都常带
        string trimmed = address.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)) return null;
        return WireProtocols.Detect(uri)?.Format;
    }

    /// <summary>
    /// 把地址补成该格式的完整端点（裸域补 <c>/v1</c>，<c>/v1</c> 结尾补后缀，
    /// 尾段是另一条线的端点则替换，已到本协议端点层原样保留）。
    /// 与运行时 <see cref="WireHttpHandler"/> 的归一同一套规则——界面给出的候选必须是运行时真会打的那个地址
    /// </summary>
    /// <param name="address">用户填的地址</param>
    /// <param name="format">目标格式</param>
    /// <returns>完整端点地址；地址不成形时原样返回</returns>
    public static string EndpointFor(string address, EApiFormat format)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out _)) return address;
        return WireProtocols.For(format).Resolve(address.Trim()).Uri.ToString();
    }
}
