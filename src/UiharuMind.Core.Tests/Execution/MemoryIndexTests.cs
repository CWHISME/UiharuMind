using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 记忆清单（ADR 0076）：怎么从目录生成、什么时候挂到本轮输入上、写笔记时报出它在清单里的样子
/// </summary>
public sealed class MemoryIndexTests : IDisposable
{
    private const int Budget = AgentSettingConfig.FactoryDefaultMemoryIndexBudget;
    private const string Token = "$MEMORY";

    private readonly string _memory = Directory.CreateTempSubdirectory("uiharu-memory-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_memory, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    private void Note(string name, string text, int minutesAgo = 0)
    {
        string path = Path.Combine(_memory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
    }

    [Fact]
    public void Build_ListsNewestFirst_WithTheFirstLineAsSummary()
    {
        Note("old.md", "# 旧的标题\n正文", minutesAgo: 10);
        Note("sub/new.md", "\n\n审批误报的根因与修法\n正文", minutesAgo: 1);
        Note("skip.txt", "不是 md");

        string list = MemoryIndex.Build(_memory, Token, Budget)!;

        Assert.Equal("- $MEMORY/sub/new.md — 审批误报的根因与修法\n- $MEMORY/old.md — 旧的标题", list.ReplaceLineEndings("\n"));
        Assert.Equal(2, MemoryIndex.CountEntries(list));
    }

    [Fact]
    public void Build_NoNotes_IsNull()
    {
        Assert.Null(MemoryIndex.Build(_memory, Token, Budget));
        Assert.Null(MemoryIndex.Build(Path.Combine(_memory, "missing"), Token, Budget));
    }

    [Fact]
    public void Build_OverBudget_KeepsTheNewest_AndSaysHowManyAreLeftOut()
    {
        const int budget = 1000;
        string summary = new('长', MemoryIndex.SummaryMaxLength);
        int count = budget / MemoryIndex.SummaryMaxLength + 5;
        for (int i = 0; i < count; i++) Note($"n{i:D3}.md", summary, minutesAgo: i);

        string list = MemoryIndex.Build(_memory, Token, budget)!;

        int listed = MemoryIndex.CountEntries(list);
        Assert.InRange(listed, 1, count - 1);
        Assert.StartsWith("- $MEMORY/n000.md", list);
        Assert.Contains($"另有 {count - listed} 份", list);
    }

    /// <summary>上限来自设置，手改配置写出离谱的值也按可设范围夹住，不会一份都不列</summary>
    [Fact]
    public void Build_BudgetOutOfRange_IsClamped()
    {
        for (int i = 0; i < 30; i++) Note($"n{i:D3}.md", new string('长', MemoryIndex.SummaryMaxLength), minutesAgo: i);

        string tiny = MemoryIndex.Build(_memory, Token, 0)!;
        string atMin = MemoryIndex.Build(_memory, Token, MemoryIndex.MinListBudget)!;

        Assert.Equal(atMin, tiny);
        Assert.True(MemoryIndex.CountEntries(tiny) > 1);
    }

    [Fact]
    public void SummaryOf_TruncatesByTextElements()
    {
        string longLine = string.Concat(Enumerable.Repeat("👍", MemoryIndex.SummaryMaxLength + 3));

        string summary = MemoryIndex.SummaryOf(longLine);

        Assert.Equal(string.Concat(Enumerable.Repeat("👍", MemoryIndex.SummaryMaxLength)) + "…", summary);
        Assert.Equal(string.Empty, MemoryIndex.SummaryOf(" \n\t\n"));
    }

    [Fact]
    public void TryAnchor_FirstTurn_PutsTheSnapshotOnTheInput_AndLeavesTheTextAlone()
    {
        Note("a.md", "偏好");
        ChatMessage input = new(ChatRole.User, "开始干活");

        Assert.True(MemoryIndex.TryAnchor([], [input], _memory, Token, Budget));

        Assert.Equal("- $MEMORY/a.md — 偏好", ChatMessageAnnotations.MemoryIndexSnapshotOf(input));
        Assert.Equal("开始干活", input.Text);
    }

    [Fact]
    public void TryAnchor_WindowAlreadyHasOne_DoesNothing()
    {
        Note("a.md", "偏好");
        ChatMessage first = new(ChatRole.User, "第一句");
        MemoryIndex.TryAnchor([], [first], _memory, Token, Budget);
        ChatMessage next = new(ChatRole.User, "第二句");

        Assert.False(MemoryIndex.TryAnchor([first, new(ChatRole.Assistant, "好")], [next], _memory, Token, Budget));
        Assert.Null(ChatMessageAnnotations.MemoryIndexSnapshotOf(next));
    }

    /// <summary>交接之后供给从交接文档开始，之前那份模型看不到了，下一轮补挂一份新的</summary>
    [Fact]
    public void TryAnchor_AfterAHandoff_AnchorsAgain()
    {
        Note("a.md", "偏好");
        ChatMessage first = new(ChatRole.User, "第一句");
        MemoryIndex.TryAnchor([], [first], _memory, Token, Budget);
        List<ChatMessage> history = [first, new(ChatRole.Assistant, "好"), HistoryHandoff.CreateNote("前情")];
        ChatMessage next = new(ChatRole.User, "接着说");

        Assert.True(MemoryIndex.TryAnchor(history, [next], _memory, Token, Budget));
    }

    [Fact]
    public void TryAnchor_SkipsApprovalResponses_AndAlreadyAnchoredRetries()
    {
        Note("a.md", "偏好");
        ToolApprovalRequestContent request = new("r1", new FunctionCallContent("c1", "Write"));
        ChatMessage approval = new(ChatRole.User, [request.CreateResponse(approved: true)]);
        Assert.False(MemoryIndex.TryAnchor([], [approval], _memory, Token, Budget));

        ChatMessage retried = new(ChatRole.User, "重新生成");
        MemoryIndex.TryAnchor([], [retried], _memory, Token, Budget);
        Note("b.md", "后来写的");
        Assert.False(MemoryIndex.TryAnchor([], [retried], _memory, Token, Budget));
        Assert.Equal(1, MemoryIndex.CountEntries(ChatMessageAnnotations.MemoryIndexSnapshotOf(retried)!));
    }

    [Fact]
    public void TryAnchor_EmptyDirectory_DoesNothing()
    {
        ChatMessage input = new(ChatRole.User, "开始");

        Assert.False(MemoryIndex.TryAnchor([], [input], _memory, Token, Budget));
        Assert.Null(input.AdditionalProperties);
    }

    [Fact]
    public async Task WriteAndEdit_InTheMemoryDirectory_ReportTheListLine()
    {
        string workspace = Directory.CreateTempSubdirectory("uiharu-memory-ws-").FullName;
        try
        {
            PermissiveFileAccessTools tools = new(new AgentPathResolver(workspace, memoryRoot: _memory),
                new FileBackupStore(Path.Combine(workspace, ".backups")));
            CancellationToken ct = TestContext.Current.CancellationToken;
            string note = Path.Combine(_memory, "pitfalls.md");

            string written = await tools.Write(note, "# 踩坑\n正文", ct);
            string edited = await tools.Edit(note, [new FileEdit { OldString = "正文", NewString = "新正文" }], ct);
            string elsewhere = await tools.Write(Path.Combine(workspace, "a.md"), "# 不是记忆", ct);

            Assert.Contains("Shown in the memory list as: $MEMORY/pitfalls.md — 踩坑", written);
            Assert.Contains("Shown in the memory list as: $MEMORY/pitfalls.md — 踩坑", edited);
            Assert.DoesNotContain("memory list", elsewhere);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
