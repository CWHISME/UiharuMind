namespace UiharuMind.Core.AI.Runtime;

/// <summary>
/// 还没有可用的本地引擎（没下载或没选中版本），本地模型无从运行
/// </summary>
public sealed class LocalEngineNotReadyException()
    : Exception("No local runtime engine is installed or selected.");
