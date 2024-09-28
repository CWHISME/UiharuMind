using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppRuntimeEngine : IBackendType
{
    private readonly string _executablePath;

    public string Name { get; }
    public bool IsAvailable { get; }

    public void ExecuteOperation()
    {
    }

    public LLamaCppRuntimeEngine(string name, string executablePath)
    {
        Name = name;
        IsAvailable = true;
        _executablePath = executablePath;
    }
}