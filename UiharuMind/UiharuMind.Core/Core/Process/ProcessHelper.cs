using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Process;

public static class ProcessHelper
{
    private static readonly HashSet<CancellationTokenSource> _cancellationTokenSources = new();

    public static void CancelAll()
    {
        foreach (var cts in _cancellationTokenSources)
        {
            if (!cts.IsCancellationRequested) cts.Cancel();
        }

        _cancellationTokenSources.Clear();
    }

    /// <summary>
    /// 执行指定目录下的某个可执行文件
    /// </summary>
    /// <param name="dirPath"></param>
    /// <param name="exeName"></param>
    /// <param name="args"></param>
    /// <param name="onInitCallback"></param>
    /// <param name="onLogCallback"></param>
    public static async Task StartProcess(string dirPath, string exeName, string args,
        Action<CancellationTokenSource>? onInitCallback = null,
        Action<string, CancellationTokenSource>? onLogCallback = null)
    {
        await StartProcess(Path.Combine(dirPath, exeName), args, onInitCallback, onLogCallback);
    }

    /// <summary>
    /// 执行指定可执行文件
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="args"></param>
    /// <param name="onLogCallback"></param>
    public static async Task StartProcess(string exePath, string args,
        Action<string, CancellationTokenSource>? onLogCallback)
    {
        await StartProcess(exePath, args, null, onLogCallback);
    }

    /// <summary>
    /// 执行指定可执行文件
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="args"></param>
    /// <param name="onInitCallback"></param>
    /// <param name="onLogCallback"></param>
    public static async Task StartProcess(string exePath, string args,
        Action<CancellationTokenSource>? onInitCallback = null,
        Action<string, CancellationTokenSource>? onLogCallback = null)
    {
        using var cts = new CancellationTokenSource();
        _cancellationTokenSources.Add(cts);
        var cmd = Cli.Wrap(exePath).WithArguments(args).WithValidation(CommandResultValidation.None);
        try
        {
            onInitCallback?.Invoke(cts);
            if (onLogCallback == null)
            {
                var result = await cmd.ExecuteBufferedAsync();
                if (result.ExitCode == 0)
                {
                    Log.Debug($"Process {exePath} excuted successfully.");
                }
                else
                {
                    Log.Error($"Process {exePath} excuted failed: {result.StandardError}");
                }

                return;
            }

            string lastLine = "";
            await foreach (var cmdEvent in cmd.ListenAsync(cancellationToken: cts.Token))
            {
                switch (cmdEvent)
                {
                    // case StartedCommandEvent started:
                    //     Log.Debug($"Process started; ID: {started.ProcessId}");
                    //     break;
                    case StandardOutputCommandEvent stdOut:
                        lastLine = stdOut.Text;
                        onLogCallback.Invoke(lastLine, cts);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        lastLine = stdErr.Text;
                        onLogCallback.Invoke(lastLine, cts);
                        break;
                    case ExitedCommandEvent exited:
                        if (exited.ExitCode != 0) Log.Error(lastLine);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Execution error: " + ex.Message);
        }
        finally
        {
            if (!cts.IsCancellationRequested) await cts.CancelAsync();
            _cancellationTokenSources.Remove(cts);
        }
    }
}