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
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

internal sealed class LLamaCppRuntimeService
{
    private readonly LLamaCppVersionManager _llamaCppVersionManager = new();

    /// <summary>
    /// 下载的 llama.cpp 引擎包放这里
    /// </summary>
    public static string EngineRoot => Path.Combine(AppPaths.External.Engine, "LLamaCpp");

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
        VersionManager versions = await GetLocalVersionsCore()
            .ConfigureAwait(false);
        if (versions.VersionsList.FindIndex(x => x.Name == LLamaCppSettingConfig.Current.SelectedRuntimeVersion) < 0)
        {
            CurrentVersion = null;
        }

        CurrentVersion ??= versions.VersionsList.FirstOrDefault();
        return versions;
    }

    /// <summary>
    /// 解压引擎包到安装目录并校验，成功后删掉压缩包
    /// </summary>
    public Task InstallArchiveAsync(VersionInfo version, CancellationToken token)
    {
        return _llamaCppVersionManager.InstallArchiveAsync(version, true, token);
    }

    public async Task<VersionManager> PullLatestVersion()
    {
        return await PullLatestVersionCore().ConfigureAwait(false);
    }

    /// <summary>
    /// 起一个对话用的 llama-server，就绪后回调对话客户端，然后陪跑到进程结束
    /// </summary>
    /// <exception cref="LocalServerException">起不来，或没被要求停却退出了</exception>
    public async Task Run(
        VersionInfo version,
        ILlmModel model,
        RuntimeResolvedParameters parameters,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(model.ModelPath))
            throw new LocalServerException($"Model '{model.ModelName}' has no file path.");

        LLamaCppSettingConfig config = LLamaCppSettingConfig.Current;
        string serverPath = config.GetExeServerPath(version.ExecutablePath) ?? "";
        LocalServerProcess server = await LocalServerProcess.StartAsync(
                LLamaCppServerArgs.Launch(serverPath, LLamaCppServerArgs.Build(model, parameters, config.Server),
                    TimeSpan.FromSeconds(Math.Max(30, config.Server.LoadTimeoutSeconds)),
                    LLamaCppServerArgs.ParseEnvironment(config.Server.EnvironmentVariables)),
                onLoading, token)
            .ConfigureAwait(false);
        onLoadOver?.Invoke(OpenAICompatibleChatClient.Create(
            new OpenAICompatibleHttpHandler(port: server.Port), model, "UiharuMind", "None"));
        await server.Completion.ConfigureAwait(false);
        if (!token.IsCancellationRequested)
            throw new LocalServerException(
                $"llama-server exited unexpectedly (exit code {server.ExitCode}).", server.LogTail);
    }

    private async Task<VersionManager> GetLocalVersionsCore()
    {
        string path = EngineRoot;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        string internalPath = Path.Combine(LLamaCppSettingConfig.Current.DefaultRuntimePath, "LLamaCpp");
        return await _llamaCppVersionManager.GetLocalVersions(path, internalPath).ConfigureAwait(false);
    }

    private async Task<VersionManager> PullLatestVersionCore()
    {
        string path = EngineRoot;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        _llamaCppVersionManager.PreferStableReleases = LLamaCppSettingConfig.Current.UseStableChannel;
        return await _llamaCppVersionManager.GetLatestVersion(path).ConfigureAwait(false);
    }
}
