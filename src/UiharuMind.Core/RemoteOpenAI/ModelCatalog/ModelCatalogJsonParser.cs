using System.Text.Json;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// 解析服务商 /v1/models 的响应（{object, data:[...]} 形状）。
/// 纯函数，供 fetcher 与测试共用；坏 JSON 抛 <see cref="JsonException"/>。
/// </summary>
public static class ModelCatalogJsonParser
{
    /// <summary>
    /// 解析目录响应
    /// </summary>
    /// <param name="json">/v1/models 的响应体</param>
    /// <returns>模型候选列表；data 缺失时为空列表</returns>
    public static IReadOnlyList<ModelCatalogEntry> Parse(string json)
    {
        var response = JsonSerializer.Deserialize<ModelCatalogResponse>(json, SaveUtility.SaveJsonOptions);
        return response?.Data ?? [];
    }
}
