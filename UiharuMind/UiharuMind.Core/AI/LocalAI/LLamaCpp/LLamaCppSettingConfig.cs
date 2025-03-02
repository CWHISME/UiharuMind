/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Text.Json.Serialization;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

public class LLamaCppSettingConfig : ConfigBase
{
    public const string ServerWinExeName = "llama-server.exe";
    public const string LookupStatsWinExeName = "llama-lookup-stats.exe";
    public const string ServerExeName = "llama-server";
    public const string LookupStatsExeName = "llama-lookup-stats";

    /// <summary>
    /// 默认运行端口
    /// </summary>
    public int DefautPort { get; set; } = 1369;

    public int DefaultEmbededPort => DefautPort + 1;
    public int DefaultEmbededProxyPort => DefaultEmbededPort + 1;

    public string? LLamaCppPath { get; set; }

    public string LocalModelPath { get; set; } = Path.Combine(SettingConfig.RootDataPath, "Models"); //"./Models";


    [JsonIgnore] public string DefaultEmbededModelPath { get; set; } = "./EmbededModels";

    [JsonIgnore]
    public string ExternalEmbededModelPath { get; set; } = Path.Combine(SettingConfig.RootDataPath, "EmbededModels");


    public Dictionary<string, GGufModelInfo> ModelInfos { get; set; } = new();

    // public string ExeLookupStats => Path.Combine(LLamaCppPath!, "llama-lookup-stats");
    // public string ExeServer => Path.Combine(LLamaCppPath!, ServerExeName);

    // public LLamaCppServerConfig ServerConfig { get; set; } = new();
    public LLamaCppServerDebugConfig DebugConfig { get; set; } = new();
    public LLamaCppServerCpuConfig CpuConfig { get; set; } = new();
    public LLamaCppServerGeneralConfig GeneralConfig { get; set; } = new();
    public LLamaCppServerParamsConfig ParamsConfig { get; set; } = new();
    public LLamaCppServerSpecialConfig SpecialConfig { get; set; } = new();
    public LLamaCppSamplingStrategiesConfig SamplingStrategiesConfig { get; set; } = new();
    public LLamaCppServerRAGConfig RagConfig { get; set; } = new();

    public string GetExeParams()
    {
        return
            $"{CommandLineHelper.GenerateCommandLineArgs(DebugConfig)} {CommandLineHelper.GenerateCommandLineArgs(CpuConfig)} {CommandLineHelper.GenerateCommandLineArgs(GeneralConfig)} {CommandLineHelper.GenerateCommandLineArgs(ParamsConfig)} {CommandLineHelper.GenerateCommandLineArgs(SpecialConfig)} {CommandLineHelper.GenerateCommandLineArgs(SamplingStrategiesConfig)} {CommandLineHelper.GenerateCommandLineArgs(RagConfig)}";
    }

    public string? GetExeLookupStatsPath(string? executablePath)
    {
        if (executablePath == null) return null;
        return Path.Combine(executablePath, PlatformUtils.IsWindows ? LookupStatsWinExeName : LookupStatsExeName);
    }

    public string? GetExeServerPath(string? executablePath)
    {
        if (executablePath == null) return null;
        return Path.Combine(executablePath, PlatformUtils.IsWindows ? ServerWinExeName : ServerExeName);
    }
}