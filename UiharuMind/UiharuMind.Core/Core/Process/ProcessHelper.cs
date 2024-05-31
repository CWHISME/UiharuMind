using CliWrap;
using CliWrap.EventStream;

namespace UiharuMind.Core.Core.Process;

public static class ProcessHelper
{
    /// <summary>
    /// 执行指定目录下的某个可执行文件
    /// </summary>
    /// <param name="dirPath"></param>
    /// <param name="exeName"></param>
    /// <param name="args"></param>
    /// <param name="callback"></param>
    public static async Task StartProcess(string dirPath, string exeName, string args,
        Func<string, CancellationTokenSource, Task> callback)

    {
        await StartProcess(Path.Combine(dirPath, exeName), args, callback);
    }

    /// <summary>
    /// 执行指定可执行文件
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="args"></param>
    /// <param name="callback"></param>
    public static async Task StartProcess(string exePath, string args,
        Func<string, CancellationTokenSource, Task> callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        using var cts = new CancellationTokenSource();
        var cmd = Cli.Wrap(exePath).WithArguments(args).WithValidation(CommandResultValidation.None);
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
                        // await ParseModelInfo(stdOut.Text, info, cts);
                        await callback.Invoke(stdOut.Text, cts);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        // Log.Debug($"Err> {stdErr.Text}");
                        // if (stdErr.Text.StartsWith("llm_load_tensors")) await cts.CancelAsync();
                        // info.UpdateValue(stdErr.Text);
                        // await ParseModelInfo(stdErr.Text, info, cts);
                        await callback.Invoke(stdErr.Text, cts);
                        break;
                    case ExitedCommandEvent exited:
                        Log.Debug($"Process exited; Code: {exited.ExitCode}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Log.Debug("Scan Canceled");
        }

        // onEnd?.Invoke();
        // return info;
    }

    public static async Task StartProcess(string exePath, string args,
        Action<string, CancellationTokenSource> callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        using var cts = new CancellationTokenSource();
        var cmd = Cli.Wrap(exePath).WithArguments(args).WithValidation(CommandResultValidation.None);
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
                        callback.Invoke(stdOut.Text, cts);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        callback.Invoke(stdErr.Text, cts);
                        break;
                    case ExitedCommandEvent exited:
                        Log.Debug($"Process exited; Code: {exited.ExitCode}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}