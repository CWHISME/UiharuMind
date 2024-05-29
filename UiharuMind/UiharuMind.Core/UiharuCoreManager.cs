using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.LLamaCpp;

namespace UiharuMind.Core;

public class UiharuCoreManager : Singleton<UiharuCoreManager>, IInitialize
{
    // Kernel kernel = Kernel.CreateBuilder()
    //     .AddOpenAIChatCompletion("m", "k", httpClient: new HttpClient(new MyHandler()))
    //     .Build();
// Console.WriteLine(await kernel.InvokePromptAsync("What color is the sky?"));
    public SettingConfig Setting { get; private set; }

    public LLamaCppServerKernal LLamaCppServer { get; private set; }

    // public ILocalLM LocalLM { get; private set; }

    public void OnInitialize()
    {
        Setting = SaveUtility.Load<SettingConfig>(typeof(SettingConfig));
        LLamaCppServer = new LLamaCppServerKernal();
        
        SetupTest();
    }

    public void Log(object message)
    {
        Console.WriteLine(message);
    }

    private void SetupTest()
    {
        LLamaCppServer.Config.LLamaCppPath =
            "/Users/dragonplus/Documents/Studys/llamacpp/llama-b2865-bin-macos-arm64/bin";
        LLamaCppServer.Config.LocalModelPath = "/Users/dragonplus/Documents/Studys/LLMModels";
    }
}