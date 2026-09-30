using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace UiharuMind.App.Tests.Architecture;

/// <summary>
/// 依赖规范 [规则4-1] 的机械检查：<c>App.Services</c> 只许出现在组装入口。
/// 视图数据、视图模型、辅助类要的服务从构造接收，弹确认、弹提示的流程才测得到
/// </summary>
public class ServiceAccessBoundaryTests
{
    // 不以 .axaml.cs 结尾的组装入口：两个纯代码窗口，与开窗口调系统的界面外壳
    private static readonly string[] CompositionRoots =
    [
        "Shared/Shell/DummyWindow.cs",
        "Shared/Windows/UiharuWindowBase.cs",
        "Shared/Services/FileOpener.cs",
    ];

    // 框架按类型创建、只能无参构造的：无参构造取一次，转调可注入的那个
    private static readonly Regex DelegatingConstructor = new(@"^\s*public \w+\(\)\s*:\s*this\(App\.Services\b");

    [Fact]
    public void AppServices_IsOnlyReachedFromCompositionRoots()
    {
        string root = AppSourceRoot();
        List<string> violations = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("obj/") || relative.StartsWith("bin/")) continue;
            if (relative.EndsWith(".axaml.cs") || CompositionRoots.Contains(relative)) continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!line.Contains("App.Services") || line.TrimStart().StartsWith("//")) continue;
                if (DelegatingConstructor.IsMatch(line)) continue;
                violations.Add($"{relative}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "这些地方直接取了 App.Services，改为从构造接收（见 AGENTS.md [规则4-1]）：\n" + string.Join("\n", violations));
    }

    // 本文件在 src/UiharuMind.App.Tests/Architecture/ 下，App 项目源码在 src/UiharuMind/
    private static string AppSourceRoot([CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "UiharuMind"));
        Assert.True(File.Exists(Path.Combine(root, "UiharuMind.csproj")), $"找不到 App 项目源码：{root}");
        return root;
    }
}
