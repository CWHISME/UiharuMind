namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 单次请求的思考力度。Default 表示不干预,沿用模型配置自身的行为;
/// 其余档位由各后端翻译为自己的请求参数(如智谱的 thinking 与 OpenAI 系的 reasoning_effort)。
/// </summary>
/// <summary>
/// 思考档位在枚举里的顺序就是 UI 下拉的显示顺序,调整顺序会导致按数字序列化的旧配置
/// 错位(Light 插入 None 与 Medium 之间即是一例)——因此这里固定用字符串序列化。
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum EThinkingMode
{
    Default,
    None,
    Light,
    Medium,
    High,
    Max,
}
