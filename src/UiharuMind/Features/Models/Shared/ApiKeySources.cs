using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Features.Models.Shared;

/// <summary>
/// 新建生图模型、在线语音服务时，从同一服务地址下的远程对话模型沿用密钥（只复制，不引用）
/// </summary>
public static class ApiKeySources
{
    /// <summary>
    /// 同一主机、填了密钥的对话模型
    /// </summary>
    /// <param name="chatModels">远程对话模型</param>
    /// <param name="endpoint">要填密钥的服务地址</param>
    /// <returns>可沿用的</returns>
    public static IReadOnlyList<RemoteModelInfo> Matching(IEnumerable<RemoteModelInfo> chatModels, string endpoint) =>
        chatModels.Where(m => !string.IsNullOrEmpty(m.ApiKey) && SameHost(m.ModelPath, endpoint)).ToList();

    /// <summary>
    /// 两个地址是不是同一主机
    /// </summary>
    public static bool SameHost(string left, string right) =>
        Uri.TryCreate(left.Trim(), UriKind.Absolute, out Uri? a) &&
        Uri.TryCreate(right.Trim(), UriKind.Absolute, out Uri? b) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是合法的 http(s) 绝对地址
    /// </summary>
    public static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
}
