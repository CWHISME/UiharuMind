using System.Diagnostics;
using CliWrap;
using CliWrap.EventStream;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.ServerKernal;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppServerKernal : ServerKernalBase<LLamaCppServerKernal, LLamaCppSettingConfig>
{
    public async Task<string?> ScanLocalModels()
    {
        if (Config.LLamaCppPath == null)
        {
            return "LLamaCppPath is null";
        }

        string lookupExe = Path.Combine(Config.LLamaCppPath, "lookup-stats");

        string[] files = Directory.GetFiles(Config.LocalModelPath, "*.gguf", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            //缓存扫描结果，避免重复扫描，除非强制标记
            // if (Config.ModelInfos.ContainsKey(fileName))
            // {
            //     continue;
            // }
            var info = await GetModelStateInfo(lookupExe, file);

            Config.ModelInfos[fileName] = info;
            // break;
        }

        Config.Save();
        return null;
    }

    private async Task<GGufModelInfo> GetModelStateInfo(string lookupExe, string file)
    {
        using var cts = new CancellationTokenSource();
        var cmd = Cli.Wrap(lookupExe).WithArguments($"-m {file}").WithValidation(CommandResultValidation.None);
        GGufModelInfo info = new GGufModelInfo();
        try
        {
            await foreach (var cmdEvent in cmd.ListenAsync(cancellationToken: cts.Token))
            {
                switch (cmdEvent)
                {
                    // case StartedCommandEvent started:
                    //     Log.Debug($"Process started; ID: {started.ProcessId}");
                    //     break;
                    case StandardOutputCommandEvent stdOut:
                        // Log.Debug($"Out> {stdOut.Text}");
                        // if (stdOut.Text.StartsWith("llm_load_tensors")) await cts.CancelAsync();
                        // info.UpdateValue(stdOut.Text);
                        await ParseModelInfo(stdOut.Text, info, cts);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        // Log.Debug($"Err> {stdErr.Text}");
                        // if (stdErr.Text.StartsWith("llm_load_tensors")) await cts.CancelAsync();
                        // info.UpdateValue(stdErr.Text);
                        await ParseModelInfo(stdErr.Text, info, cts);
                        break;
                    // case ExitedCommandEvent exited:
                    //     Log.Debug($"Process exited; Code: {exited.ExitCode}");
                    //     break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Log.Debug("Scan Canceled");
        }

        return info;
    }

    private async ValueTask ParseModelInfo(string line, GGufModelInfo info, CancellationTokenSource cts)
    {
        if (line.StartsWith("llm_load_tensors")) await cts.CancelAsync();
        info.UpdateValue(line);
    }
}