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

using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime.Backends;

internal sealed class LLamaCppRuntimeService
{
    private readonly LLamaCppVersionManager _llamaCppVersionManager = new();
    private readonly Dictionary<string, GGufModelInfo> _modelInfos = new();
    
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
        VersionManager versions = await GetLocalVersions(SettingConfig.BackendRuntimeEnginePath)
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
        return await PullLatestVersion(SettingConfig.BackendRuntimeEnginePath).ConfigureAwait(false);
    }

    public async Task Run(
        VersionInfo version,
        ILlmModel model,
        RuntimeResolvedParameters parameters,
        Action<float>? onLoading = null,
        Action<IChatClient>? onLoadOver = null,
        int? port = null,
        CancellationToken token = default)
    {
        int loadingCount = 0;
        bool loadOver = false;

        const float loadingMaxCount = 128f;
        const int loadingMinCount = 16;

        void OnMessageUpdate(string msg)
        {
            if (loadOver) return;

            if (msg.StartsWith("error", StringComparison.OrdinalIgnoreCase))
            {
                onLoadOver?.Invoke(CreateChatClient(model));
                return;
            }

            if (!msg.Contains("server is listening", StringComparison.OrdinalIgnoreCase))
            {
                loadingCount++;
                if (loadingCount % loadingMinCount == 0)
                {
                    float loadingPercent = Math.Min(1, loadingCount / loadingMaxCount);
                    onLoading?.Invoke(loadingPercent);
                }

                if (loadingCount >= loadingMaxCount)
                {
                    Log.Debug($"Loading over count {loadingCount}");
                }

                return;
            }

            Log.Debug($"Loading over {loadingCount}");
            loadOver = true;
            onLoadOver?.Invoke(CreateChatClient(model));
        }

        await StartServer(
                version.ExecutablePath,
                model,
                parameters,
                port ?? LLamaCppSettingConfig.Current.DefaultPort,
                OnMessageUpdate,
                token)
            .ConfigureAwait(false);
    }

    private async Task StartServer(
        string executablePath,
        ILlmModel model,
        RuntimeResolvedParameters parameters,
        int port,
        Action<string>? onMessageUpdate,
        CancellationToken token)
    {
        if (string.IsNullOrEmpty(model.ModelPath))
        {
            Log.Error("Can't run server without model file path");
            return;
        }

        string? serverPath = LLamaCppSettingConfig.Current.GetExeServerPath(executablePath);
        if (string.IsNullOrWhiteSpace(serverPath) || !File.Exists(serverPath))
        {
            Log.Error($"Can't find server executable {serverPath}");
            return;
        }

        string args = BuildServerArgs(model, parameters, port);
        Log.Debug("Start server:" + args);
        await ProcessHelper.StartProcess(serverPath, args, onMessageUpdate, token)
            .ConfigureAwait(false);
    }

    private IChatClient CreateChatClient(ILlmModel model)
    {
        var handler = new OpenAICompatibleHttpHandler(model, port: LLamaCppSettingConfig.Current.DefaultPort);
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(handler))
        };
        var client = new ChatClient("UiharuMind", new ApiKeyCredential("None"), options);
        return client.AsIChatClient();
    }

    public async Task<IReadOnlyDictionary<string, GGufModelInfo>> GetModelList(VersionInfo? version)
    {
        return await ScanAllLocalModels(version).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, GGufModelInfo>> ScanAllLocalModels(
        VersionInfo? version,
        bool force = false)
    {
        _modelInfos.Clear();
        await ScanLocalModels(version, ModelSettingConfig.Current.DefaultLocalModelPath, _modelInfos, force).ConfigureAwait(false);
        return await ScanLocalModels(version, ModelSettingConfig.Current.LocalModelPath, _modelInfos, force).ConfigureAwait(false);
    }

    private Task<Dictionary<string, GGufModelInfo>> ScanLocalModels(
        VersionInfo? version,
        string modelPath,
        Dictionary<string, GGufModelInfo> modelInfos,
        bool force)
    {
        if (!Directory.Exists(modelPath)) return Task.FromResult(modelInfos);

        bool isChanged = false;
        foreach (string file in Directory.GetFiles(modelPath, "*.gguf", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.Contains("mmproj", StringComparison.Ordinal)) continue;

            string projPath = "";
            if (!force && ModelSettingConfig.Current.ModelInfos.TryGetValue(fileName, out GGufModelInfo? info))
            {
                info.ModelPath = file;
                info.ModelProjPath = projPath;
                if (info.ContextLength <= 0)
                {
                    info.ApplyMetadata(GGufMetadataReader.TryRead(file));
                    isChanged = true;
                }

                modelInfos[fileName] = info;
                continue;
            }

            isChanged = true;
            info = new GGufModelInfo
            {
                ModelName = fileName,
                ModelPath = file,
                ModelProjPath = projPath
            };
            info.ApplyMetadata(GGufMetadataReader.TryRead(file));

            modelInfos[fileName] = info;
            ModelSettingConfig.Current.ModelInfos[fileName] = info;
        }

        if (isChanged) LLamaCppSettingConfig.Current.Save();
        return Task.FromResult(modelInfos);
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

    private static string BuildServerArgs(ILlmModel model, RuntimeResolvedParameters parameters, int port)
    {
        List<string> args =
        [
            $"-m \"{model.ModelPath}\"",
            "--no-webui",
            $"--alias {Path.GetFileNameWithoutExtension(model.ModelPath)}",
            $"--port {port}",
            "-to 0",
            $"-c {parameters.ContextSize}",
            $"-b {parameters.BatchSize}",
            $"-ub {parameters.UBatchSize}",
            $"-ngl {parameters.GpuLayers}"
        ];

        if (parameters.Threads > 0)
            args.Add($"--threads {parameters.Threads}");
        if (parameters.FlashAttention)
            args.Add("--flash-attn");

        return string.Join(' ', args);
    }
}
