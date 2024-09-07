using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Input;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.ScreenCapture;

namespace UiharuMind.Core;

public class UiharuCoreManager : Singleton<UiharuCoreManager>, IInitialize
{
    public bool IsWindows { get; set; }
    public bool IsMacOS { get; set; }

    public SettingConfig Setting { get; private set; }

    public LLamaCppServerKernal LLamaCppServer { get; private set; }
    public InputManager Input => _input;

    // public ILocalLM LocalLM { get; private set; }
    private InputManager _input;

    public void OnInitialize()
    {
        CheckPlatform();
        Setting = SaveUtility.Load<SettingConfig>(typeof(SettingConfig));
        LLamaCppServer = new LLamaCppServerKernal();
        _input = new InputManager();
        _input.Start();

        if (IsWindows) SetupTestWin();
        else SetupTest();
        // SetupTestWin();
    }

    /// <summary>
    /// 主动初始化
    /// </summary>
    /// <param name="logger"></param>
    public void Init(ILogger? logger)
    {
        // Log.Logger = logger ?? new DefaultLogger();
        Log.Debug("UiharuCoreManager initialized");
    }

    public async Task CaptureScreen()
    {
        await ScreenCaptureMac.Capture();
    }

    public async Task CaptureScreen(int screenId)
    {
        await ScreenCaptureMac.Capture(screenId);
    }

    private void CheckPlatform()
    {
        var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        IsWindows = os.Contains("Windows");
        IsMacOS = !IsWindows && (os.Contains("macOS", StringComparison.Ordinal) ||
                                 os.Contains("OS X", StringComparison.Ordinal) ||
                                 os.Contains("Darwin", StringComparison.Ordinal));
    }

    private void SetupTestWin()
    {
        LLamaCppServer.Config.LLamaCppPath =
            "D:\\Solfware\\AI\\llama-b3058-bin-win-vulkan-x64";
        if (!Directory.Exists(LLamaCppServer.Config.LocalModelPath))
            LLamaCppServer.Config.LocalModelPath = "D:\\Solfware\\AI\\LLM_Models";
    }

    private void SetupTest()
    {
        LLamaCppServer.Config.LLamaCppPath =
            "/Users/dragonplus/Documents/Studys/llamacpp/llama-b3617-bin-macos-arm64/bin";
        LLamaCppServer.Config.LocalModelPath = "/Users/dragonplus/Documents/Studys/LLMModels";
    }
}