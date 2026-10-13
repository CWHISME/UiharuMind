using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.WorldSettings;
using UiharuMind.Core.AutoClick;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.Core;

/// <summary>
/// SaveUtility 有界类型的 source-gen 序列化上下文（主题五 A2）。
/// 范围是拍板确定的<b>有界类型</b>：角色卡、记忆/世界书/自动点击、MCP 族、
/// PromptSnippet、ScheduledAgentTask、DownloadState、ModelManifest、远程模型配置族。
/// <b>ConfigBase 一族 11 个设置派生不在里面</b>——<see cref="ConfigBase.Save"/> 走反射 object 路由
/// （泛型化会把 T 静态推断成基类、派生字段静默丢，该行为变化被拍板否决；AOT 已关门，反射端可用）。
/// </summary>
/// <remarks>
/// <see cref="CharacterCard"/> 的 <c>List&lt;object&gt;</c> 字段（AlternateGreetings/GroupOnlyGreetings）
/// 由单点 converter 处理（见 SaveJsonContext 的 modifier 挂在 SaveUtility.SaveJsonOptions 上），
/// 不在此声明 object 值类型——宽容读外部角色卡不炸。
/// </remarks>
[JsonSourceGenerationOptions(
    // 严格复刻反射 SaveUtility.JsonOptions 的形状：PascalCase 属性名、枚举按数字、WhenWritingNull。
    // 任何 naming policy / enum converter 都会改写旧存档格式（护栏会抓）。
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CharacterCard))]
[JsonSerializable(typeof(CharacterData))]
[JsonSerializable(typeof(MemoryData))]
[JsonSerializable(typeof(WorldSetting))]
[JsonSerializable(typeof(AutoClickSession))]
[JsonSerializable(typeof(McpServersFile))]
[JsonSerializable(typeof(McpServerLocalState))]
[JsonSerializable(typeof(McpToolCatalog))]
[JsonSerializable(typeof(McpTrustRecord))]
[JsonSerializable(typeof(PromptSnippet))]
[JsonSerializable(typeof(ScheduledAgentTask))]
[JsonSerializable(typeof(DownloadState))]
[JsonSerializable(typeof(ModelManifest))]
[JsonSerializable(typeof(BaseRemoteModelConfig))]
[JsonSerializable(typeof(RemoteAgnesModelConfig))]
[JsonSerializable(typeof(RemoteDeepSeekModelConfig))]
[JsonSerializable(typeof(RemoteSensenovaModelConfig))]
[JsonSerializable(typeof(RemoteZhipuModelConfig))]
[JsonSerializable(typeof(RemoteModelConfig))]
// ConfigBase 一族（AOT 试编卡点 #2：AOT 下反射不存在，设置/记忆读写静默失效，需 source-gen）
[JsonSerializable(typeof(SettingConfig))]
[JsonSerializable(typeof(QuickToolSetting))]
[JsonSerializable(typeof(QuickToolPromptSetting))]
[JsonSerializable(typeof(ChatSettingConfig))]
[JsonSerializable(typeof(ModelSettingConfig))]
[JsonSerializable(typeof(ModelRuntimeSettingConfig))]
[JsonSerializable(typeof(DebugSettingConfig))]
[JsonSerializable(typeof(AgentSettingConfig))]
[JsonSerializable(typeof(ChatPromptExecutionSettings))]
[JsonSerializable(typeof(TextFileSettingConfig))]
[JsonSerializable(typeof(GroupModelMemoryConfig))]
[JsonSerializable(typeof(AutoClickSettingConfig))]
[JsonSerializable(typeof(ImageModelSettingConfig))]
[JsonSerializable(typeof(SdCppSettingConfig))]
[JsonSerializable(typeof(EmbeddingModelSettingConfig))]
[JsonSerializable(typeof(LLamaCppSettingConfig))]
[JsonSerializable(typeof(RemoteModelSettingConfig))]
[JsonSerializable(typeof(DownloadSourceSettingConfig))]
[JsonSerializable(typeof(AudioCppSettingConfig))]
[JsonSerializable(typeof(AudioSettingConfig))]
[JsonSerializable(typeof(AudioServiceSettingConfig))]
// 作为根类型出现过的泛型容器（Load/Save 直接传 List/Dictionary 的地方）
[JsonSerializable(typeof(List<PromptSnippet>))]
[JsonSerializable(typeof(List<ScheduledAgentTask>))]
[JsonSerializable(typeof(List<McpTrustRecord>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, McpServerLocalState>))]
[JsonSerializable(typeof(Dictionary<string, List<McpTrustRecord>>))]
internal partial class SaveJsonContext : JsonSerializerContext
{
}