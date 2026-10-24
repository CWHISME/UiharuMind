namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 触碰全局 <see cref="SessionManager"/> / 共享会话索引的测试必须串行：私有管理器在
/// <c>SaveIndex</c> 时会先把盘上那份（别的实例写的）并进来，并行跑就会在彼此的断言上撞车。
/// 与 <see cref="AudioCppGgufTests"/> 的 <c>LocalModelSettingState</c> 同一手法
/// </summary>
[CollectionDefinition("ChatSessionState")]
public sealed class ChatSessionStateCollectionDefinition;
