namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 单次请求的思考力度。Default 表示不干预,沿用模型配置自身的行为;
/// 其余档位由各后端翻译为自己的请求参数(如智谱的 thinking 与 OpenAI 系的 reasoning_effort)。
/// </summary>
public enum EThinkingMode
{
    Default,
    None,
    Medium,
    High,
    Max,
}
