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

using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.AI.LocalAI;

/// <summary>
/// 本地AI运行时引擎管理器
/// </summary>
public class RuntimeEngineManager : ServerKernalBase<RuntimeEngineManager, RuntimeEngineSettingConfig>,
    ILlmRuntime
{
    /// <summary>
    /// llamacpp 服务
    /// </summary>
    public LLamaCppServerKernal LLamaCppServer { get; private set; } = new LLamaCppServerKernal();

    /// <summary>
    /// 当前选择的运行时版本
    /// </summary>
    public VersionInfo? CurrentSeletedVersion { get; private set; }

    // /// <summary>
    // /// 所有模型列表
    // /// </summary>
    // private List<ModelRunningData> _modelList = new List<ModelRunningData>();

    /// <summary>
    ///模型列表缓存
    /// </summary>
    private Dictionary<string, ModelRunningData> _chacheModels = new Dictionary<string, ModelRunningData>();

    /// <summary>
    /// 缓存中不存在的模型列表，便于清理
    /// </summary>
    private List<string> _modelDeleteCacheList = new List<string>();

    public RuntimeEngineManager()
    {
        InitializeAvailableVersions();
    }

    private void InitializeAvailableVersions()
    {
        var versions = GetLocalVersions().Result;
        foreach (var version in versions.VersionsList)
        {
            if (version.VersionNumber == Config.SelecetedRuntimeEngine) CurrentSeletedVersion = version;
        }
    }

    /// <summary>
    /// 切换运行时
    /// </summary>
    /// <param name="version"></param>
    public void SetSelectedVersion(VersionInfo? version)
    {
        CurrentSeletedVersion = version;
        Config.SelecetedRuntimeEngine = version?.VersionNumber;
        SaveConfig();
    }

    /// <summary>
    /// 获取所有本地的版本列表
    /// </summary>
    /// <returns></returns>
    public async Task<VersionManager> GetLocalVersions()
    {
        var versions = await LLamaCppServer.GetLocalVersions(SettingConfig.BackendRuntimeEnginePath)
            .ConfigureAwait(false);
        //之前设定的运行时已经不存在了，自动剔除掉
        if (versions.VersionsList.FindIndex(x => x.Name == Config.SelecetedRuntimeEngine) < 0)
        {
            CurrentSeletedVersion = null;
        }

        CurrentSeletedVersion ??= versions.VersionsList.FirstOrDefault();
        return versions;
    }

    /// <summary>
    /// 获取远程及本地的版本列表
    /// </summary>
    /// <returns></returns>
    public async Task<VersionManager> PullLastestVersion()
    {
        var versionManager = await LLamaCppServer.PullLastestVersion(SettingConfig.BackendRuntimeEnginePath);
        return versionManager;
    }

    /// <summary>
    /// 重新加载(获取)本地模型列表
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ModelRunningData>> GetModelList()
    {
        _modelDeleteCacheList.Clear();

        var modelList = await LLamaCppServer.GetModelList(CurrentSeletedVersion).ConfigureAwait(false);
        // var modelList = await LLamaCppServer.GetModelList().ConfigureAwait(false);

        //清理缓存中不存在的模型
        // _modelList.RemoveAll(x => !modelList.ContainsKey(x.ModelInfo.ModelName));

        foreach (var model in _chacheModels)
        {
            if (modelList.ContainsKey(model.Key)) continue;
            _modelDeleteCacheList.Add(model.Key);
        }

        //清理缓存中不存在的模型
        foreach (var modelName in _modelDeleteCacheList)
        {
            _chacheModels.Remove(modelName);
        }

        //更新缓存中存在的模型
        foreach (var model in modelList)
        {
            if (_chacheModels.TryGetValue(model.Key, out var runningData))
            {
                runningData.ForceUpdateModelInfo(model.Value);
                // _modelList.Add(runningData);
                continue;
            }

            runningData = new ModelRunningData(this, model.Value);
            _chacheModels[model.Key] = runningData;
            // _modelList.Add(runningData);
        }


        return _chacheModels;
    }

    public Task Run(ILlmModel model, Action<float>? onLoading = null,
        Action? onLoadOver = null, CancellationToken token = default)
    {
        if (CurrentSeletedVersion == null)
        {
            Log.Error("当前没有选择本地运行时！");
            return Task.CompletedTask;
        }

        return LLamaCppServer.Run(CurrentSeletedVersion, model, onLoading, onLoadOver, token);
    }
}