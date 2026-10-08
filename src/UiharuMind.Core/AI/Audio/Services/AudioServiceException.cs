using System.Net;
using System.Text.Json;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 音频服务回了错误。OpenAI 兼容的错误体是 {"error":{"message","type"}}，拆出来调用方才好分情况提示
/// （如 audio.cpp 内存保护拒绝加载时 type 为 insufficient_memory）
/// </summary>
/// <param name="message">服务给的说明；没有时为状态码与原文</param>
/// <param name="statusCode">HTTP 状态码</param>
/// <param name="errorType">错误种类；服务没给为 null</param>
public sealed class AudioServiceException(string message, HttpStatusCode statusCode, string? errorType)
    : HttpRequestException(message, null, statusCode)
{
    /// <summary>
    /// audio.cpp 内存保护拒绝加载
    /// </summary>
    public const string InsufficientMemory = "insufficient_memory";

    /// <summary>
    /// 错误种类；服务没给为 null
    /// </summary>
    public string? ErrorType { get; } = errorType;

    /// <summary>
    /// 从错误响应解析
    /// </summary>
    /// <param name="statusCode">状态码</param>
    /// <param name="body">响应正文</param>
    /// <returns>异常</returns>
    public static AudioServiceException FromResponse(HttpStatusCode statusCode, string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
            {
                string? message = error.TryGetProperty("message", out JsonElement m) ? m.GetString() : null;
                string? type = error.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
                if (!string.IsNullOrWhiteSpace(message)) return new AudioServiceException(message, statusCode, type);
            }
        }
        catch (JsonException)
        {
        }

        return new AudioServiceException($"Audio request failed: {(int)statusCode} {body}", statusCode, null);
    }
}
