using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 按模型与设置拼 sd-server 的启动参数。只传有必要的：其余交给 sd.cpp 的默认与 --auto-fit
/// </summary>
public static class SdCppServerArgs
{
    /// <summary>
    /// 日志与报错里的服务名
    /// </summary>
    public const string ServerName = "sd-server";

    // 不报 /health；模型建好后才开端口，这个探针 200 就是就绪
    private const string ReadyPath = "sdcpp/v1/capabilities";

    /// <summary>
    /// 端口之外的参数
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="settings">引擎设置</param>
    /// <returns>逐项参数</returns>
    public static IReadOnlyList<string> Build(SdCppModelSetup model, SdCppSettingConfig settings)
    {
        List<string> args = [];
        foreach (SdCppComponent component in model.Components)
        {
            args.Add("--" + component.Flag);
            args.Add(component.Path);
        }

        args.AddRange(model.ExtraArguments);
        // 启动时就把权重读进来：坏文件、内存不够在启动阶段就报出来，第一张图也不用再等加载
        args.Add("--eager-load");
        if (settings.DiffusionFlashAttention) args.Add("--diffusion-fa");
        if (settings.OffloadToCpu) args.Add("--offload-to-cpu");
        if (settings.ClipOnCpu) args.Add("--clip-on-cpu");
        if (settings.MemoryMap) args.Add("--mmap");
        if (settings.ForceCpu) args.AddRange(["--backend", "cpu"]);
        if (settings.Threads > 0) args.AddRange(["-t", settings.Threads.ToString()]);
        args.AddRange(EngineArguments.Split(settings.ExtraArguments));
        return args;
    }

    /// <summary>
    /// 启动描述：只听本机回环，端口由进程自选
    /// </summary>
    /// <param name="executablePath">sd-server</param>
    /// <param name="args">端口之外的参数</param>
    /// <param name="readyTimeout">就绪超时（含加载权重）</param>
    /// <returns>启动描述</returns>
    public static LocalServerLaunch Launch(string executablePath, IReadOnlyList<string> args, TimeSpan readyTimeout) =>
        new(ServerName, executablePath, port => [..args, "--listen-ip", "127.0.0.1", "--listen-port", port.ToString()],
            readyTimeout)
        {
            HealthPath = ReadyPath,
            ExpectedLogLines = 48f
        };
}
