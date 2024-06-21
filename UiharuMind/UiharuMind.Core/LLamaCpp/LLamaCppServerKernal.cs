using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppServerKernal : ServerKernalBase<LLamaCppServerKernal, LLamaCppSettingConfig>
{
    private LLamaCppVersionManager _llamaCppVersionManager = new LLamaCppVersionManager();
    private List<GGufModelInfo> _modelInfos = new List<GGufModelInfo>();

    public async Task StartServer(string modelFilePath, int port)
    {
        await ProcessHelper.StartProcess(Config.ExeServer, $"-m {modelFilePath} --port {port}",
            (line, cts) => { Log.Debug(line); });
    }

    public async Task<IReadOnlyList<GGufModelInfo>> GetModelList()
    {
        return await ScanLocalModels();
    }

    public async Task<List<GGufModelInfo>> ScanLocalModels(bool force = false)
    {
        string lookupExe = Config.ExeLookupStats;
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
                _modelInfos.Add(info);
                continue;
            }

            if (!isChanged) isChanged = true;
            info = await GetModelStateInfo(lookupExe, file);
            info.ModelName = fileName;
            info.ModelPath = file;
            _modelInfos.Add(info);

            Config.ModelInfos[fileName] = info;
            // break;
        }

        if (isChanged) Config.Save();
        return _modelInfos;
        // return null;
    }

    public async Task ScanLocalModel(string modelFilePath)
    {
        var info = await GetModelStateInfo(Config.ExeLookupStats, modelFilePath);
        Config.ModelInfos[Path.GetFileName(modelFilePath)] = info;
    }

    public async Task<LLamaCppVersionItem> PullLastestVersion()
    {
        return await _llamaCppVersionManager.GetLatestVersion();
    }

    private async Task<GGufModelInfo> GetModelStateInfo(string lookupExe, string file)
    {
        // Stopwatch stopwatch = new Stopwatch();
        // stopwatch.Start();
        GGufModelInfo info = new GGufModelInfo();
        await ProcessHelper.StartProcess(lookupExe, $"-m {file}",
            async (line, cts) => await ParseModelInfo(line, info, cts));
        // stopwatch.Stop();
        // Log.Debug($"Scan Model {file} {stopwatch.ElapsedMilliseconds}");
        return info;
    }

    private async ValueTask ParseModelInfo(string line, GGufModelInfo info, CancellationTokenSource cts)
    {
        if (line.StartsWith("llm_load_tensors", StringComparison.Ordinal)) await cts.CancelAsync();
        info.UpdateValue(line);
    }
}