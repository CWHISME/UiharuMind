using System.Diagnostics;
using CliWrap;
using CliWrap.EventStream;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppServerKernal : ServerKernalBase<LLamaCppServerKernal, LLamaCppSettingConfig>
{
    public async Task StartServer(string modelFilePath)
    {
        await ProcessHelper.StartProcess(Config.ExeServer, $"-m {modelFilePath}", (line, cts) => { Log.Debug(line); });
    }

    public async Task<GGufModelInfo[]> GetModelList()
    {
        await ScanLocalModels();
        return Config.ModelInfos.Values.ToArray();
    }

    public async Task<Dictionary<string, GGufModelInfo>> ScanLocalModels(bool force = false)
    {
        string lookupExe = Config.ExeLookupStats;

        string[] files = Directory.GetFiles(Config.LocalModelPath, "*.gguf", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            //缓存扫描结果，避免重复扫描，除非强制标记
            if (!force && Config.ModelInfos.TryGetValue(fileName, out var info))
                // if (Config.ModelInfos.ContainsKey(fileName))
            {
                info.ModelPath = file;
                continue;
            }

            info = await GetModelStateInfo(lookupExe, file);
            info.ModelName = fileName;
            info.ModelPath = file;

            Config.ModelInfos[fileName] = info;
            // break;
        }

        Config.Save();
        return Config.ModelInfos;
        // return null;
    }

    public async Task ScanLocalModel(string modelFilePath)
    {
        var info = await GetModelStateInfo(Config.ExeLookupStats, modelFilePath);
        Config.ModelInfos[Path.GetFileName(modelFilePath)] = info;
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