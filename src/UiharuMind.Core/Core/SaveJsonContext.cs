using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
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
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Core;

/// <summary>
/// 存档（<see cref="SaveUtility"/>）的 source-gen 序列化上下文，存档类型的唯一登记处：没登记的类型存取时直接抛。
/// 设置一族经 <see cref="ConfigBase.Save"/> 按运行时类型写，派生类各自登记
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
[JsonSerializable(typeof(GoalModeState))]
[JsonSerializable(typeof(GoalModeScheduleRule))]
[JsonSerializable(typeof(GoalModeRecentState))]
[JsonSerializable(typeof(DownloadState))]
[JsonSerializable(typeof(ModelManifest))]
[JsonSerializable(typeof(BaseRemoteModelConfig))]
[JsonSerializable(typeof(RemoteAgnesModelConfig))]
[JsonSerializable(typeof(RemoteDeepSeekModelConfig))]
[JsonSerializable(typeof(RemoteSensenovaModelConfig))]
[JsonSerializable(typeof(RemoteZhipuModelConfig))]
[JsonSerializable(typeof(RemoteCommandCodeModelConfig))]
[JsonSerializable(typeof(RemoteOpenCodeModelConfig))]
[JsonSerializable(typeof(RemoteModelConfig))]
[JsonSerializable(typeof(ModelCatalogResponse))]
[JsonSerializable(typeof(List<ModelCatalogEntry>))]
// 设置一族
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
[JsonSerializable(typeof(NewConversationDraftConfig))]
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
[JsonSerializable(typeof(List<GoalModeState>))]
[JsonSerializable(typeof(List<GoalModeRecentState>))]
[JsonSerializable(typeof(List<McpTrustRecord>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, McpServerLocalState>))]
[JsonSerializable(typeof(Dictionary<string, List<McpTrustRecord>>))]
internal partial class SaveJsonContext : JsonSerializerContext
{
}