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

using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.AI.Runtime;

/// <summary>
/// 本地AI运行时引擎管理器
/// </summary>
public class RuntimeEngineManager : ServerKernalBase<RuntimeEngineManager, RuntimeEngineSettingConfig>
{
    /// <summary>
    /// llamacpp 服务
    /// </summary>
    public LLamaCppServerKernal LLamaCppServer { get; private set; } = new LLamaCppServerKernal();

    /// <summary>
    /// 当前选择的运行时版本
    /// </summary>
    public VersionInfo? CurrentSeletedVersion { get; private set; }

    public RuntimeEngineManager()
    {
        InitializeAvailableVersions();
    }

    private void InitializeAvailableVersions()
    {
        var versions = GetLocalVersions().Result;
        foreach (var version in versions.VersionsList)
        {
            if (version.Name == Config.SelecetedRuntimeEngine) CurrentSeletedVersion = version;
        }
    }

    /// <summary>
    /// 切换运行时
    /// </summary>
    /// <param name="version"></param>
    public void SetSelectedVersion(VersionInfo? version)
    {
        CurrentSeletedVersion = version;
        Config.SelecetedRuntimeEngine = version?.Name;
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
    /// 尝试确保嵌入式服务启动
    /// </summary>
    /// <param name="onLoadOver"></param>
    [Obsolete("Use ModelRuntimeService.CreateEmbeddingSessionAsync through EmbeddingModelService instead.")]
    public void TryEnsureEmbededServer(Action<EmbeddedServerConfig?>? onLoadOver = null)
    {
        if (CurrentSeletedVersion == null)
        {
            Log.Error(
                "Current Selected Local RuntimeBackend Engine Version is null！Plese to Setting Page to select a version first.");
            onLoadOver?.Invoke(null);
            return;
        }

        LLamaCppServer.TryEnsureEmbededServer(CurrentSeletedVersion, onLoadOver);
    }

    // private async void StartEmbededServer(Action<IChatClient>? onLoadOver = null)
    // {
    //     if (CurrentSeletedVersion == null)
    //     {
    //         Log.Error(
    //             "Current Selected Local RuntimeBackend Engine Version is null！Plese to Setting Page to select a version first.");
    //         return;
    //     }
    //
    //     await LLamaCppServer.Run(CurrentSeletedVersion, LLamaCppServer.Config.EmbededModelInfo, onLoadOver: onLoadOver,
    //         port: LLamaCppServer.Config.DefaultEmbededPort, extraParams: "--embedding");
    // }
}
