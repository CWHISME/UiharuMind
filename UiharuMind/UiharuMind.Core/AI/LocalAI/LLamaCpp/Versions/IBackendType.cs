namespace UiharuMind.Core.LLamaCpp.Versions;

public interface IBackendType
{
    string Name { get; }

    bool IsAvailable { get; }

    // 执行特定操作
    void ExecuteOperation();
}