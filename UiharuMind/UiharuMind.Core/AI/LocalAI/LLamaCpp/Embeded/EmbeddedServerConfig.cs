namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Embeded;

public class EmbeddedServerConfig
{
    public string Endpoint { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
    public int EmbeddingModelMaxTokenTotal { get; set; }
    public string APIKey { get; set; } = "";
}
