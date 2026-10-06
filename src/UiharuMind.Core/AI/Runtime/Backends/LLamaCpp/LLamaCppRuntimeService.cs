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

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

internal sealed class LLamaCppRuntimeService
{
    private static readonly TimeSpan ChatReadyTimeout = TimeSpan.FromMinutes(10); // 大模型冷加载可能要几分钟

    private readonly LLamaCppVersionManager _llamaCppVersionManager = new();
    
    public VersionInfo? CurrentVersion { get; private set; }

    public LLamaCppRuntimeService()
    {
        InitializeAvailableVersions();
    }

    private void InitializeAvailableVersions()
    {
        VersionManager versions = GetLocalVersions().Result;
        foreach (VersionInfo version in versions.VersionsList)
        {
            if (version.Name == LLamaCppSettingConfig.Current.SelectedRuntimeVersion) CurrentVersion = version;
        }
    }

    public void SetSelectedVersion(VersionInfo? version)
    {
        CurrentVersion = version;
        LLamaCppSettingConfig.Current.SelectedRuntimeVersion = version?.Name;
        LLamaCppSettingConfig.Current.Save();
    }

    public async Task<VersionManager> GetLocalVersions()
    {
        VersionManager versions = await GetLocalVersions(AppPaths.External.Engine)
            .ConfigureAwait(false);
        if (versions.VersionsList.FindIndex(x => x.Name == LLamaCppSettingConfig.Current.SelectedRuntimeVersion) < 0)
        {
            CurrentVersion = null;
        }

        CurrentVersion ??= versions.VersionsList.FirstOrDefault();
        return versions;
    }

    public async Task<VersionManager> PullLatestVersion()
    {
        return await PullLatestVersion(AppPaths.External.Engine).ConfigureAwait(false);
    }

    /// <summary>
    /// 起一个对话用的 llama-server，就绪后回调对话客户端，然后陪跑到进程结束
    /// </summary>
    /// <exception cref="LlamaServerException">起不来，或没被要求停却退出了</exception>
    public async Task Run(
        VersionInfo version,
        ILlmModel model,
        RuntimeResolvedParameters parameters,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(model.ModelPath))
            throw new LlamaServerException($"Model '{model.ModelName}' has no file path.");

        string serverPath = LLamaCppSettingConfig.Current.GetExeServerPath(version.ExecutablePath) ?? "";
        LlamaServerProcess server = await LlamaServerProcess.StartAsync(
                serverPath, BuildServerArgs(model, parameters), ChatReadyTimeout, onLoading, token)
            .ConfigureAwait(false);
        onLoadOver?.Invoke(OpenAICompatibleChatClient.Create(
            new OpenAICompatibleHttpHandler(port: server.Port), model, "UiharuMind", "None"));
        await server.Completion.ConfigureAwait(false);
        if (!token.IsCancellationRequested)
            throw new LlamaServerException(
                $"llama-server exited unexpectedly (exit code {server.ExitCode}).", server.LogTail);
    }

    private async Task<VersionManager> GetLocalVersions(string enginePath)
    {
        string path = Path.Combine(enginePath, "LLamaCpp");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        string internalPath = Path.Combine(LLamaCppSettingConfig.Current.DefaultRuntimePath, "LLamaCpp");
        return await _llamaCppVersionManager.GetLocalVersions(path, internalPath).ConfigureAwait(false);
    }

    private async Task<VersionManager> PullLatestVersion(string enginePath)
    {
        string path = Path.Combine(enginePath, "LLamaCpp");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return await _llamaCppVersionManager.GetLatestVersion(path).ConfigureAwait(false);
    }

    /// <summary>
    /// 对话服务参数（不含 host/port）
    /// </summary>
    internal static IReadOnlyList<string> BuildServerArgs(ILlmModel model, RuntimeResolvedParameters parameters)
    {
        List<string> args =
        [
            "-m", model.ModelPath,
            "--no-webui",
            "--alias", Path.GetFileNameWithoutExtension(model.ModelPath),
            "-to", "0",
            "-c", parameters.ContextSize.ToString(),
            "-b", parameters.BatchSize.ToString(),
            "-ub", parameters.UBatchSize.ToString(),
            "-ngl", parameters.GpuLayers.ToString(),
            // 用模型自带的聊天模板渲染，工具调用也靠它
            "--jinja"
        ];

        if (model is GGufModelInfo { ModelProjPath: { Length: > 0 } projPath })
            args.AddRange(["--mmproj", projPath]);
        if (parameters.Threads > 0)
            args.AddRange(["--threads", parameters.Threads.ToString()]);
        if (parameters.FlashAttention)
            args.AddRange(["--flash-attn", "on"]);

        return args;
    }
}
