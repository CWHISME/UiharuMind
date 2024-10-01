using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppServerKernal : ServerKernalBase<LLamaCppServerKernal, LLamaCppSettingConfig>, ILlmRuntimeEngine
{
    private LLamaCppVersionManager _llamaCppVersionManager = new LLamaCppVersionManager();
    private Dictionary<string, GGufModelInfo> _modelInfos = new Dictionary<string, GGufModelInfo>();


    public async Task StartServer(string executablePath, string modelFilePath, int port,
        Action<string>? onMessageUpdate = null, CancellationToken token = default)
    {
        // var exePath = Config.GetExeServerPath(executablePath);
        // if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        // {
        //     Log.Error($"Can't find server executable {exePath}");
        //     return;
        // }

        // config ??= Config.ServerConfig;
        string paramsStr = Config.GetExeParams();
        Log.Debug("Start sever:" + paramsStr);
        await ProcessHelper.StartProcess(Config.GetExeServerPath(executablePath),
                $"-m {modelFilePath} --alias {Path.GetFileNameWithoutExtension(modelFilePath)} --port {port} {paramsStr}",
                onMessageUpdate, token)
            .ConfigureAwait(false);
    }

    public async Task Run(VersionInfo info, ILlmModel model, Action<float>? onLoading = null, Action? onLoadOver = null,
        CancellationToken token = default)
    {
        int loadingCount = 0;
        var loadOver = false;

        void OnMessageUpdate(string msg)
        {
            if (!loadOver || Config.DebugConfig.LogRunningInfo) Log.Debug(msg);
            if (loadOver) return;
            if (loadingCount < 128 && !msg.StartsWith("main: server is listening"))
            {
                loadingCount++;
                var loadingPercent = Math.Min(1, loadingCount / 128f);
                onLoading?.Invoke(loadingPercent);
            }
            else
            {
                loadOver = true;
                onLoadOver?.Invoke();
            }
        }

        await StartServer(info.ExecutablePath, model.ModelPath, Config.DefautPort, OnMessageUpdate, token)
            .ConfigureAwait(false);
    }

    // public async Task Run(ILlmModel model, Action<CancellationTokenSource> onStartLoad, Action<float>? onLoading = null,
    //     Action? onLoadOver = null)
    // {
    //     int loadingCount = 0;
    //
    //     void OnMessageUpdate(string msg)
    //     {
    //         Log.Debug(msg);
    //         if (loadingCount < 128 && !msg.StartsWith("main: server is listening"))
    //         {
    //             loadingCount++;
    //             var loadingPercent = Math.Min(1, loadingCount / 128f);
    //             onLoading?.Invoke(loadingPercent);
    //         }
    //         else onLoadOver?.Invoke();
    //     }
    //
    //     await StartServer(model.ModelPath, Config.DefautPort, onStartLoad, OnMessageUpdate);
    // }

    public async Task<IReadOnlyDictionary<string, GGufModelInfo>> GetModelList(VersionInfo? versionInfo)
    {
        return await ScanLocalModels(versionInfo).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, GGufModelInfo>> ScanLocalModels(VersionInfo? versionInfo, bool force = false)
    {
        string? executablePath = Config.GetExeLookupStatsPath(versionInfo?.ExecutablePath);
        _modelInfos.Clear();
        string[] files = Directory.GetFiles(Config.LocalModelPath!, "*.gguf", SearchOption.AllDirectories);
        bool isChanged = false;
        foreach (var file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            //缓存扫描结果，避免重复扫描，除非强制标记
            if (!force && Config.ModelInfos.TryGetValue(fileName, out var info))
                // if (Config.ModelInfos.ContainsKey(fileName))
            {
                info.ModelPath = file;
                _modelInfos[fileName] = info;
                continue;
            }

            if (executablePath == null) continue;

            if (!isChanged) isChanged = true;
            info = await GetModelStateInfo(executablePath, file);
            info.ModelName = fileName;
            info.ModelPath = file;

            _modelInfos[fileName] = info;
            Config.ModelInfos[fileName] = info;
            // break;
        }

        if (isChanged) Config.Save();
        //缓存一次时因为可能出现更换目录，配置设置还在但是模型已经不在的情况
        return _modelInfos;
        // return null;
    }

    // public async Task ScanLocalModel(string modelFilePath)
    // {
    //     var info = await GetModelStateInfo(Config.ExeLookupStats, modelFilePath);
    //     Config.ModelInfos[Path.GetFileName(modelFilePath)] = info;
    // }

    /// <summary>
    /// 获取本地版本列表
    /// </summary>
    /// <param name="enginePath"></param>
    /// <returns></returns>
    public async Task<VersionManager> GetLocalVersions(string enginePath)
    {
        string path = Path.Combine(enginePath, "LLamaCpp");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return await _llamaCppVersionManager.GetLocalVersions(path).ConfigureAwait(false);
    }

    /// <summary>
    /// 拉取最新版本列表
    /// </summary>
    /// <returns></returns>
    public async Task<VersionManager> PullLastestVersion(string enginePath)
    {
        string path = Path.Combine(enginePath, "LLamaCpp");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return await _llamaCppVersionManager.GetLatestVersion(path);
    }

    private async Task<GGufModelInfo> GetModelStateInfo(string exec, string file)
    {
        // Stopwatch stopwatch = new Stopwatch();
        // stopwatch.Start();
        GGufModelInfo info = new GGufModelInfo();
        // await ProcessHelper.StartProcess(lookupExe, $"-m {file}",
        //     async (line, cts) => await ParseModelInfo(line, info, cts));
        CancellationTokenSource cts = new CancellationTokenSource();
        await ProcessHelper.StartProcess(exec, $"-m {file} -v",
            (line) => ParseModelInfo(line, info, cts),
            cts.Token);
        // stopwatch.Stop();
        // Log.Debug($"Scan Model {file} {stopwatch.ElapsedMilliseconds}");
        return info;
    }

    private void ParseModelInfo(string line, GGufModelInfo info, CancellationTokenSource cts)
    {
        if (line.StartsWith("llm_load_tensors", StringComparison.Ordinal)) cts.Cancel();
        info.UpdateValue(line);
    }
}