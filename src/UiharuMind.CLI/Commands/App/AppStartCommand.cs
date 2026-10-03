using System.Diagnostics;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;

namespace UiharuMind.CLI.Commands.App;

[Command("app start", Description = "Launch the desktop app detached with --dev-control and wait until the control channel answers.")]
public partial class AppStartCommand : ICommand
{
    [CommandOption("exe", Description = "Path to the UiharuMind.Desktop executable.", EnvironmentVariable = "UIHARU_DESKTOP_EXE")]
    public string? Exe { get; set; }

    [CommandOption("home", Description = "Profile directory; defaults to UIHARU_HOME or ~/.uiharu.")]
    public string? Home { get; set; }

    [CommandOption("timeout", Description = "Seconds to wait for the app to answer.")]
    public int Timeout { get; set; } = 90;

    [CommandOption("allow-other-instance", Description = "Start even if another UiharuMind is running on the default profile.")]
    public bool AllowOtherInstance { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var endpoint = AppControl.EndpointOf(Home);
        if (await AppControl.PingAsync(endpoint) is { } running)
        {
            await console.Output.WriteLineAsync($"already running (pid {running})");
            return;
        }

        // 没指定档案就是用户的默认档案：那里若有个没开通道的实例在跑，再起一个就是两个实例同用一份档案
        // （后起的会清掉先起的那个的本地模型进程）。指定了档案的，默认档案上的实例不相干
        bool defaultProfile = string.IsNullOrWhiteSpace(Home) &&
                              string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UIHARU_HOME"));
        if (defaultProfile && !AllowOtherInstance && FindOtherInstance() is { } other)
        {
            throw new CommandException(
                $"UiharuMind is already running (pid {other}) without a control channel on the default profile. " +
                "Quit it or turn on developer mode in it, use --home for another profile, or pass --allow-other-instance.", 2);
        }

        if (string.IsNullOrWhiteSpace(Exe) || !File.Exists(Exe))
            throw new CommandException("desktop executable not found; pass --exe or set UIHARU_DESKTOP_EXE", 2);

        Process.Start(CreateStartInfo(Path.GetFullPath(Exe)))?.Dispose();

        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(Timeout))
        {
            if (await AppControl.PingAsync(endpoint) is { } pid)
            {
                await console.Output.WriteLineAsync($"started (pid {pid}, {watch.Elapsed.TotalSeconds:0.#}s)");
                return;
            }

            await Task.Delay(500);
        }

        throw new CommandException($"app did not answer within {Timeout}s; check its log under the profile's Logs/", 3);
    }

    // 进程名在 macOS 上会被截到 16 字节，按前缀认；排除 CLI 自己
    private static int? FindOtherInstance()
    {
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                try
                {
                    name = process.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    continue; //枚举到一半退出了
                }

                if (process.Id != Environment.ProcessId &&
                    name.StartsWith("UiharuMind", StringComparison.Ordinal) &&
                    !name.StartsWith("UiharuMind.CLI", StringComparison.Ordinal))
                    return process.Id;
            }
        }

        return null;
    }

    // 不挂在调用方名下：标准输入输出接到空设备，调用方的管道不会被应用占着（否则调用方要等应用退出才收得到 EOF），
    // Unix 上经 nohup 后台起、sh 立即退出，应用归到 init 名下
    private ProcessStartInfo CreateStartInfo(string exe)
    {
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("--dev-control");
        }
        else
        {
            info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("nohup \"$0\" \"$@\" </dev/null >/dev/null 2>&1 &");
            info.ArgumentList.Add(exe);
            info.ArgumentList.Add("--dev-control");
        }

        if (!string.IsNullOrWhiteSpace(Home)) info.Environment["UIHARU_HOME"] = Path.GetFullPath(Home);
        return info;
    }
}
