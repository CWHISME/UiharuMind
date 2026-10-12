using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死 <see cref="PathGrouping"/> 的自顶向下展开：各组互不重叠、总和等于全部权重，
/// 等重兄弟不拆一半，子目录过多时只挑偏重的拆出、其余并成「其余」组。
/// </summary>
public class PathGroupingTests
{
    [Fact]
    public void Group_WithinLimit_StaysOneLevelUp()
    {
        var items = new (string Path, int Weight)[]
        {
            ("a/x.cs", 1), ("a/y.cs", 1), ("b/z.cs", 1),
        };

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Prefix == "a" && g.Total == 2);
        Assert.Contains(groups, g => g.Prefix == "b" && g.Total == 1);
    }

    [Fact]
    public void Group_OverLimit_CollapsesToRoot()
    {
        // 60 个不同目录各 1 个文件，展开放不下且没有偏重的 → 整组留在根
        List<(string, int)> items = [];
        for (int i = 0; i < 60; i++) items.Add(($"dir{i:D2}/f.cs", 1));

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Single(groups);
        Assert.Equal(".", groups[0].Prefix);
        Assert.Equal(60, groups[0].Total);
    }

    [Fact]
    public void Group_OverLimit_StopsAtUpperDirectory()
    {
        // 60 个子目录分属 3 个父目录：三个等重父目录一起展开放不下 → 都不拆，停在 3 组
        List<(string, int)> items = [];
        for (int p = 0; p < 3; p++)
        {
            for (int s = 0; s < 20; s++)
            {
                items.Add(($"dir{(char)('A' + p)}/sub{s:D2}/f.cs", 1));
            }
        }

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, g => g.Prefix == "dirA" && g.Total == 20);
        Assert.Contains(groups, g => g.Prefix == "dirB" && g.Total == 20);
        Assert.Contains(groups, g => g.Prefix == "dirC" && g.Total == 20);
    }

    [Fact]
    public void Group_Weights_AreSummedNotCounted()
    {
        // 权重 = 命中数：聚合后目录组的数值是命中数之和，不是文件数
        var items = new (string Path, int Weight)[]
        {
            ("a/x.cs", 100), ("a/y.cs", 5), ("b/z.cs", 3),
        };

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Contains(groups, g => g.Prefix == "a" && g.Total == 105);
        Assert.Contains(groups, g => g.Prefix == "b" && g.Total == 3);
    }

    [Fact]
    public void Group_ShallowFiles_DoNotFallIntoRootGroup()
    {
        // 浅层文件（src/PXX/File.cs）留在真实目录组，根下没有文件时分组不应出现 "."；
        // 30 个等重的 PXX 一起拆会超限 → 都不拆
        List<(string, int)> items = [];
        for (int i = 0; i < 30; i++)
        {
            items.Add(($"src/P{i:D2}/File.cs", 1));
            items.Add(($"src/P{i:D2}/Sub/Deep.cs", 1));
        }

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.DoesNotContain(groups, g => g.Prefix == ".");
        Assert.Equal(30, groups.Count);
        Assert.All(groups, g => Assert.Equal(2, g.Total)); // 浅层文件留在 src/PXX，没被冲进根
    }

    [Fact]
    public void Group_Empty_ReturnsEmpty()
    {
        Assert.Empty(PathGrouping.Group([]));
    }

    [Fact]
    public void Group_ExactlyMaxGroups_StaysWithoutMerging()
    {
        // 恰好 50 组（≤ 上限）可以完整展开
        List<(string, int)> items = [];
        for (int i = 0; i < 50; i++) items.Add(($"src/D{i:D2}/f.cs", 1));

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Equal(50, groups.Count);
        Assert.All(groups, g => Assert.Equal(1, g.Total));
    }

    [Fact]
    public void Group_WindowsAbsolutePath_KeepsDriveRoot()
    {
        // Windows 绝对路径：盘符（C:/...）是路径根，不应被还原成 "." 或 "C"
        var items = new (string Path, int Weight)[]
        {
            ("C:/proj/src/X.cs", 1), ("C:/proj/src/Y.cs", 1), ("D:/other/Z.cs", 1),
        };

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.DoesNotContain(groups, g => g.Prefix is "." or "C");
        Assert.Contains(groups, g => g.Prefix == "C:/proj/src" && g.Total == 2);
        Assert.Contains(groups, g => g.Prefix == "D:/other" && g.Total == 1);
    }

    [Fact]
    public void Group_DenseSubtree_IsPreservedWhileSparseMerges()
    {
        // 90 条命中里 30 条集中在 src/Dense，60 条散在 src/A00..A59 各 1 条。
        // 61 个子目录放不下 → 挑出偏重的 Dense，散点并成「其余」（不能标成 "src"，那像整个 src 的合计）
        List<(string, int)> items = [];
        for (int i = 0; i < 30; i++) items.Add(("src/Dense/F.cs", 1));
        for (int i = 0; i < 60; i++) items.Add(($"src/A{i:D2}/f.cs", 1));

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Prefix == "src/Dense" && g.Total == 30); // 密集子树保留原样
        Assert.Contains(groups, g => g.Prefix == "src（其余）" && g.Total == 60);
    }

    [Fact]
    public void Group_TooManyChildren_PullsOutAboveAverageOnly()
    {
        // 65 个子目录放不下：高于平均的 30 个 B（各 2）拆出，35 个 A（各 1）并成「其余」
        List<(string, int)> items = [];
        for (int i = 0; i < 30; i++)
        {
            items.Add(($"src/B{i:D2}/X.cs", 1));
            items.Add(($"src/B{i:D2}/Y.cs", 1));
        }

        for (int i = 0; i < 35; i++) items.Add(($"src/A{i:D2}/f.cs", 1));

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Equal(31, groups.Count);
        Assert.Contains(groups, g => g.Prefix == "src/B00" && g.Total == 2);
        Assert.Contains(groups, g => g.Prefix == "src（其余）" && g.Total == 35);
    }

    [Fact(Timeout = 5000)]
    public async Task Group_WindowsAbsolutePath_OverLimit_Converges()
    {
        // 回归：ParentOrSelf("C:")=="C:"，建树时的父链必须以「父==自身」终止，否则在盘符根上死循环。
        // 带超时：复发时报失败而不是挂住整个测试进程
        List<(string, int)> items = [];
        for (int i = 0; i < 60; i++) items.Add(($"C:/d{i:D2}/f.cs", 1));

        List<PathGrouping.PathGroup> groups = await Task.Run(
            () => PathGrouping.Group(items), TestContext.Current.CancellationToken);

        Assert.Single(groups);
        Assert.Equal("C:", groups[0].Prefix); // 盘符根是路径根，收敛到它而不是 "."
        Assert.Equal(60, groups[0].Total);
    }

    [Fact]
    public void Group_HotFile_DoesNotSwallowOtherDirectories()
    {
        // 复现旧实现的「src: 103、.: 7」：一个热点文件命中极多时，其余目录不能被一锅收进 "src" / "."
        List<(string, int)> items = [("src/Hot/H.cs", 5000)];
        for (int i = 0; i < 10; i++) items.Add(($"src/Core/M{i:D2}/f.cs", 1));
        for (int i = 0; i < 5; i++) items.Add(($"docs/adr/{i}.md", 1));
        items.Add(("README.md", 1));

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Contains(groups, g => g.Prefix == "src/Hot" && g.Total == 5000);
        Assert.Contains(groups, g => g.Prefix == "docs/adr" && g.Total == 5);
        Assert.Contains(groups, g => g.Prefix == "./*" && g.Total == 1); // 只有真正在根下的文件
        Assert.DoesNotContain(groups, g => g.Prefix is "src" or ".");
    }

    [Fact]
    public void Group_DirectFiles_AreSeparatedFromSubdirectories()
    {
        // 目录既有直属文件又有子目录：直属文件单列 "dir/*"，各组不重叠、总和不变
        var items = new (string Path, int Weight)[]
        {
            ("src/A/x.cs", 3), ("src/A/B/y.cs", 2), ("src/A/C/z.cs", 1), ("src/D/w.cs", 4),
        };

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.Contains(groups, g => g.Prefix == "src/A/*" && g.Total == 3 && g.Count == 1);
        Assert.Contains(groups, g => g.Prefix == "src/A/B" && g.Total == 2);
        Assert.Contains(groups, g => g.Prefix == "src/A/C" && g.Total == 1);
        Assert.Contains(groups, g => g.Prefix == "src/D" && g.Total == 4);
        Assert.Equal(10, groups.Sum(g => g.Total));
    }

    [Fact]
    public void Group_LargeTree_StaysWithinLimitAndKeepsTotal()
    {
        // 深而宽的树：组数不超上限，总和恒等于全部权重（组间无重叠）
        List<(string, int)> items = [];
        for (int a = 0; a < 12; a++)
        {
            for (int b = 0; b < 12; b++)
            {
                for (int c = 0; c < 4; c++) items.Add(($"src/P{a}/M{b}/S{c}/f.cs", 1 + (a * b + c) % 7));
            }
        }

        List<PathGrouping.PathGroup> groups = PathGrouping.Group(items);

        Assert.InRange(groups.Count, 2, PathGrouping.MaxGroups);
        Assert.Equal(items.Sum(x => x.Item2), groups.Sum(g => g.Total));
    }

    [Fact]
    public void DescribeLabels_OnlyExplainsLabelsPresent()
    {
        List<PathGrouping.PathGroup> plain = PathGrouping.Group([("a/x.cs", 1), ("b/y.cs", 1)]);
        Assert.Null(PathGrouping.DescribeLabels(plain));

        List<PathGrouping.PathGroup> mixed = PathGrouping.Group([("a/x.cs", 1), ("a/b/y.cs", 1), ("c/z.cs", 1)]);
        string? legend = PathGrouping.DescribeLabels(mixed);
        Assert.NotNull(legend);
        Assert.Contains("「目录/*」", legend);
        Assert.DoesNotContain("其余", legend);
    }

    [Fact]
    public void Group_Output_IsInTreeOrder_RestAfterItsSiblings()
    {
        // 「其余」紧跟在被单列的兄弟后面，直属文件排在本目录最前；不按权重排
        List<(string, int)> items = [("lib/x.cs", 999), ("src/F.cs", 1)];
        for (int i = 0; i < 30; i++) items.Add(("src/Dense/F.cs", 1));
        for (int i = 0; i < 60; i++) items.Add(($"src/A{i:D2}/f.cs", 1));

        List<string> order = PathGrouping.Group(items).Select(g => g.Prefix).ToList();

        Assert.Equal(["lib", "src/Dense", "src（其余）"], order);
    }
}
