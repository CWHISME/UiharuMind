using HttpAllocBench;
using UiharuMind.Core.Core.SimpleLog;

// 用法见 ../http-alloc.sh。UIHARU_HOME 必须指到临时目录：清洗流收尾会打日志，不隔离就会写进并轮换真实日志
string home = Environment.GetEnvironmentVariable("UIHARU_HOME") ?? "";
string realProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".uiharu");
if (home.Length == 0 || Path.GetFullPath(home).TrimEnd('/') == realProfile)
{
    Console.Error.WriteLine("UIHARU_HOME 要指到临时目录（不能是真实档案），请走 src/scripts/perf/http-alloc.sh");
    return 2;
}

LogManager.UseDirectory(Path.Combine(home, "Logs"));
using var alloc = new AllocListener();

string mode = args.Length > 0 ? args[0] : "all";
switch (mode)
{
    case "all":
        SseBench.Run(alloc);
        await RequestBench.Run(alloc, home);
        await EndToEndBench.Run(alloc, home);
        break;
    case "sse":
        SseBench.Run(alloc);
        break;
    case "req":
        await RequestBench.Run(alloc, home);
        break;
    case "e2e":
        await EndToEndBench.Run(alloc, home);
        break;
    case "gc":
        await EndToEndBench.RunGc(args.Length > 1 ? args[1] : "current", home);
        break;
    case "compat":
        return Compat.Run();
    default:
        Console.Error.WriteLine($"未知模式 {mode}：sse | req | e2e | gc [current|proto] | compat | all");
        return 2;
}

LogManager.Instance.Flush();
return 0;
