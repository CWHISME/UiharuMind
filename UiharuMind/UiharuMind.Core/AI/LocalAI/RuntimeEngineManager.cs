using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.AI.LocalAI;

/// <summary>
/// 本地AI运行时引擎管理器
/// </summary>
public class RuntimeEngineManager : ServerKernalBase<RuntimeEngineManager, RuntimeEngineSettingConfig>
{
    /// <summary>
    /// llamacpp 服务
    /// </summary>
    public LLamaCppServerKernal LLamaCppServer { get; private set; } = new LLamaCppServerKernal();

    public VersionInfo? CurrentSeletedVersion { get; private set; }

    public RuntimeEngineManager()
    {
        InitializeAvailableVersions();
    }

    private async void InitializeAvailableVersions()
    {
        var versions = await GetLocalVersions();
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
        var versions = await LLamaCppServer.GetLocalVersions(SettingConfig.BackendRuntimeEnginePath);
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
}