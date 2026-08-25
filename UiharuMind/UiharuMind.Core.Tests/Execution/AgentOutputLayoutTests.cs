using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死 agent 产出的目录布局。承重之处是<b>不同会话不许共用一个目录</b>：
/// 产出以绝对路径写进对话历史，共用时两个会话各画一张同名图，后者会静默盖掉前者，
/// 而旧对话回头看一切正常、只是图是错的——这类缺陷在实机上几乎不可能被发现。
/// </summary>
public class AgentOutputLayoutTests
{
    private const string SessionA = "3f2a1b0c9d8e7f6a5b4c3d2e1f009988";
    private const string SessionB = "aabbccdd11223344556677889900aabb";

    [Fact]
    public void GetFolderName_SeparatesSessionsWithTheSameTitle()
    {
        string a = AgentOutputLayout.GetFolderName("数据分析", SessionA);
        string b = AgentOutputLayout.GetFolderName("数据分析", SessionB);

        Assert.NotEqual(a, b);
        Assert.StartsWith("数据分析_", a); //标题要留在目录名里,用户得能认出来
    }

    [Fact]
    public void GetFolderName_FallsBackToIdWhenTitleHasNothingUsable()
    {
        // 标题只有标点/空白时不能产出一个以下划线开头的怪目录名
        Assert.Equal("3f2a1b0c", AgentOutputLayout.GetFolderName("··· ---", SessionA));
    }

    [Fact]
    public void GetFolderName_IsEmptyWithoutSession()
    {
        // 能力预览走的是 FromDraft,此时没有会话。空串让装配退回父目录而不是造一个 "_" 目录
        Assert.Equal(string.Empty, AgentOutputLayout.GetFolderName("标题", string.Empty));
    }

    [Fact]
    public void DeleteAll_RemovesEveryFolderOfThatSessionIncludingRenamedOnes()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-outputs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // 改名不搬目录,所以同一个会话可能留下多个目录——清理必须按 id 后缀通配全收
            string oldName = Path.Combine(root, AgentOutputLayout.GetFolderName("旧标题", SessionA));
            string newName = Path.Combine(root, AgentOutputLayout.GetFolderName("新标题", SessionA));
            string other = Path.Combine(root, AgentOutputLayout.GetFolderName("别人的会话", SessionB));
            foreach (string dir in new[] { oldName, newName, other })
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "chart.png"), "x");
            }

            DeleteAllIn(root, SessionA);

            Assert.False(Directory.Exists(oldName));
            Assert.False(Directory.Exists(newName));
            Assert.True(Directory.Exists(other)); //别的会话的产出不能被牵连
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// <see cref="AgentOutputLayout.DeleteAll"/> 的父目录是 <c>AppPaths</c> 写死的，
    /// 单测不能往用户真实数据目录里造文件，故在此复刻同一条通配规则。
    /// 规则本身极短，复刻的风险小于让测试去动真实磁盘布局。
    /// </summary>
    private static void DeleteAllIn(string root, string sessionId)
    {
        string suffix = AgentOutputLayout.GetFolderName(string.Empty, sessionId);
        foreach (string path in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(path);
            if (name == suffix || name.EndsWith($"_{suffix}", StringComparison.Ordinal))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
