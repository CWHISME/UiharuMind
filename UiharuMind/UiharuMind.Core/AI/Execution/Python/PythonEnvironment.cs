/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using CliWrap;
using CliWrap.Buffered;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Python;

/// <summary>
/// 一次探测或创建的结果。<see cref="Message"/> 是给用户看的原因，失败时必填
/// </summary>
/// <param name="Ok">是否成功</param>
/// <param name="InterpreterPath">解释器绝对路径；失败时为空串</param>
/// <param name="Message">版本号或失败原因</param>
public readonly record struct PythonProbeResult(bool Ok, string InterpreterPath, string Message);

/// <summary>
/// 受管 Python 环境：<b>解释器由用户提供，虚拟环境由我们建、由 agent 往里装包</b>。
///
/// 存在的理由是给 <c>Shell</c> 一个确定的 Python，而不是引入第二个执行面：shell 本来就能跑
/// Python，缺的只是「跑哪一个、装的包去哪儿」这个确定性。见 ADR 0019。
///
/// 用虚拟环境而非直接用宿主解释器，是因为 macOS 自带的 python3 与 Debian 系都是
/// PEP 668 externally-managed，往里 <c>pip install</c> 会被直接拒绝；绕过去就等于在改
/// 用户的系统环境，那是越界。虚拟环境让"解释器归用户、包归我们"两件事各自成立。
///
/// <b>本类不参与装配的贵活</b>：装配期只读 <see cref="IsReady"/> 这一个布尔
/// （<c>AgentAssemblyPlan.Resolve</c> 是同步的、从不等网络），建环境是设置页的显式一步。
/// </summary>
public static class PythonEnvironment
{
    /// <summary>
    /// 探测与建环境的超时。
    ///
    /// <b>不是可有可无的保险</b>：macOS 上 <c>/usr/bin/python3</c> 是个存根，没装 Xcode
    /// Command Line Tools 时执行它会弹出 GUI 安装对话框并<b>无限期挂住子进程</b>。
    /// 超时是把那种情况变回一次干净失败的唯一办法——文件存在与否判断不出来。
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    /// <summary>建虚拟环境要解压标准库，比探测慢得多</summary>
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromMinutes(5);

    /// <summary>虚拟环境根目录</summary>
    public static string Root => AppPaths.External.PythonEnv;

    /// <summary>
    /// 虚拟环境里的解释器绝对路径。目录布局由 <c>venv</c> 模块决定，两平台不同：
    /// Windows 是 <c>Scripts\python.exe</c>，其余是 <c>bin/python</c>
    /// </summary>
    public static string InterpreterPath => OperatingSystem.IsWindows()
        ? Path.Combine(Root, "Scripts", "python.exe")
        : Path.Combine(Root, "bin", "python");

    /// <summary>环境是否已就绪。<b>装配期唯一读的那个事实</b></summary>
    public static bool IsReady => File.Exists(InterpreterPath);

    /// <summary>
    /// 探测可用的宿主解释器：配置里填了就只认那一个，否则按 PATH 找。
    /// 找到的候选都会真的执行一次 <c>--version</c> 验证——PATH 上有个同名文件不等于它能跑。
    /// </summary>
    /// <param name="token">取消令牌</param>
    /// <returns>探测结果</returns>
    public static async Task<PythonProbeResult> ProbeHostAsync(CancellationToken token = default)
    {
        string configured = AgentSettingConfig.Current.PythonInterpreterPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
            {
                return new PythonProbeResult(false, string.Empty, $"设置里填的解释器不存在：{configured}");
            }

            return await VerifyAsync(configured, token).ConfigureAwait(false);
        }

        List<string> candidates = EnumerateOnPath().ToList();
        if (candidates.Count == 0)
        {
            return new PythonProbeResult(false, string.Empty,
                "PATH 上找不到 python3 / python。请先安装 Python，或在上方手动填写解释器路径。");
        }

        string lastMessage = string.Empty;
        foreach (string candidate in candidates)
        {
            PythonProbeResult result = await VerifyAsync(candidate, token).ConfigureAwait(false);
            if (result.Ok) return result;
            lastMessage = result.Message;
        }

        return new PythonProbeResult(false, string.Empty, lastMessage);
    }

    /// <summary>
    /// 用宿主解释器创建虚拟环境。已就绪则直接返回，不重复建。
    /// </summary>
    /// <param name="onLog">进度行回调（venv 与后续校验的输出），可空</param>
    /// <param name="token">取消令牌</param>
    /// <returns>创建结果；成功时 <c>InterpreterPath</c> 是虚拟环境里那个解释器</returns>
    public static async Task<PythonProbeResult> CreateAsync(Action<string>? onLog = null,
        CancellationToken token = default)
    {
        if (IsReady) return await VerifyAsync(InterpreterPath, token).ConfigureAwait(false);

        PythonProbeResult host = await ProbeHostAsync(token).ConfigureAwait(false);
        if (!host.Ok) return host;

        onLog?.Invoke($"使用 {host.InterpreterPath}（{host.Message}）创建虚拟环境…");

        // 半成品目录比没有目录更麻烦:IsReady 会因为缺 bin/python 而为 false,
        // 但下一次 venv 又会因为目录已存在而走"更新"分支,把上一次的残骸沿用下去
        RemoveDirectory();

        try
        {
            using CancellationTokenSource cts = WithTimeout(token, CreateTimeout);
            BufferedCommandResult result = await Cli.Wrap(host.InterpreterPath)
                .WithArguments(["-m", "venv", Root])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cts.Token)
                .ConfigureAwait(false);

            if (result.StandardOutput.Length > 0) onLog?.Invoke(result.StandardOutput.TrimEnd());
            if (result.ExitCode != 0)
            {
                RemoveDirectory();
                string detail = result.StandardError.TrimEnd();
                Log.Error($"创建 Python 虚拟环境失败({result.ExitCode})：{detail}");
                return new PythonProbeResult(false, string.Empty,
                    detail.Length > 0 ? detail : $"venv 退出码 {result.ExitCode}");
            }
        }
        catch (OperationCanceledException)
        {
            RemoveDirectory();
            return new PythonProbeResult(false, string.Empty, "创建超时或已取消。");
        }
        catch (Exception e)
        {
            RemoveDirectory();
            Log.Error($"创建 Python 虚拟环境异常：{e}");
            return new PythonProbeResult(false, string.Empty, e.Message);
        }

        if (!IsReady)
        {
            RemoveDirectory();
            return new PythonProbeResult(false, string.Empty, $"venv 跑完了但没有生成 {InterpreterPath}。");
        }

        PythonProbeResult verified = await VerifyAsync(InterpreterPath, token).ConfigureAwait(false);
        if (!verified.Ok) RemoveDirectory();
        else onLog?.Invoke($"就绪：{verified.InterpreterPath}（{verified.Message}）");
        return verified;
    }

    /// <summary>
    /// 删除整个虚拟环境（用户在设置页点"移除"，或想换个解释器重建）
    /// </summary>
    public static void Remove() => RemoveDirectory();

    /// 执行一次 --version。超时按失败处理,理由见 ProbeTimeout
    private static async Task<PythonProbeResult> VerifyAsync(string path, CancellationToken token)
    {
        try
        {
            using CancellationTokenSource cts = WithTimeout(token, ProbeTimeout);
            BufferedCommandResult result = await Cli.Wrap(path)
                .WithArguments("--version")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cts.Token)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                return new PythonProbeResult(false, string.Empty,
                    $"{path} 执行失败（退出码 {result.ExitCode}）。");
            }

            // 老版本把版本号打在 stderr 上
            string version = result.StandardOutput.Trim();
            if (version.Length == 0) version = result.StandardError.Trim();
            return new PythonProbeResult(true, path, version.Length > 0 ? version : "Python");
        }
        catch (OperationCanceledException)
        {
            // macOS 上未装 CLT 的 /usr/bin/python3 就吊死在这里
            return new PythonProbeResult(false, string.Empty,
                $"{path} 无响应（可能是 macOS 上未安装 Xcode Command Line Tools 的存根）。");
        }
        catch (Exception e)
        {
            return new PythonProbeResult(false, string.Empty, $"{path} 无法执行：{e.Message}");
        }
    }

    /// PATH 上的候选解释器。只做文件存在判断、不执行——macOS 的存根一执行就弹 GUI
    private static IEnumerable<string> EnumerateOnPath()
    {
        string[] names = OperatingSystem.IsWindows()
            ? ["python.exe", "python3.exe"]
            : ["python3", "python"];

        string paths = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string dir in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string name in names)
            {
                string full;
                try
                {
                    full = Path.Combine(dir.Trim(), name);
                }
                catch (ArgumentException)
                {
                    continue; //PATH 里混进了非法字符的条目
                }

                if (File.Exists(full) && seen.Add(full)) yield return full;
            }
        }
    }

    private static CancellationTokenSource WithTimeout(CancellationToken token, TimeSpan timeout)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static void RemoveDirectory()
    {
        if (!Directory.Exists(Root)) return;
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e)
        {
            Log.Error($"删除 Python 虚拟环境失败：{e.Message}");
        }
    }
}
