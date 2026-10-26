namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>服务商 /v1/models 响应的根形状（OpenAI 系标准）</summary>
public sealed class ModelCatalogResponse
{
    /// <summary>对象类型标记（如 list），暂不使用</summary>
    public string? Object { get; set; }

    /// <summary>模型候选列表</summary>
    public List<ModelCatalogEntry> Data { get; set; } = [];
}
