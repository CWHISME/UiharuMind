using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 交接时写出的纯文本转录。它存在的全部理由是"现成的 Grep 搜得动"：
/// 历史 jsonl 一条消息一行，Grep 对超长命中行从行首截断，命中点稍靠后模型就看不到。
/// </summary>
public class HistoryTranscriptTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-transcript-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    [Fact]
    public void Render_HeadsEachMessage_AndCutsToolTrafficToOneLine()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "看一下 a.cs"),
            new(ChatRole.Assistant, [new TextReasoningContent("内心独白不该进转录"), new TextContent("好")]),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", FileToolNames.Read, new Dictionary<string, object?> { ["filePath"] = "a.cs" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "line1\nline2\nline3")]),
        ];

        string transcript = HistoryTranscript.Render(history);

        Assert.Contains("## #1 user\n看一下 a.cs\n", transcript);
        Assert.Contains("## #2 assistant\n好\n", transcript);
        Assert.DoesNotContain("内心独白", transcript);
        Assert.Contains("[tool call] Read(filePath=a.cs)", transcript);
        Assert.Contains("[tool result] line1 line2 line3 (17 chars)", transcript);
    }

    [Fact]
    public void Render_NamedSkill_ShowsWhatTheUserTyped_NotTheSkillBody()
    {
        ChatMessage skill = new(ChatRole.User, "# 整份技能说明……")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.NamedSkill] = "review",
                [ChatMessageAnnotations.NamedSkillInput] = "/review 登录模块",
            },
        };

        string transcript = HistoryTranscript.Render([skill]);

        Assert.Contains("## #1 user (skill)\n/review 登录模块\n", transcript);
        Assert.DoesNotContain("整份技能说明", transcript);
    }

    /// <summary>整条链路：长段落里的约束，经转录落盘后用真实的 Grep 搜，返回的那一行要能看见命中</summary>
    [Fact]
    public async Task LongParagraph_IsWrapped_SoGrepShowsTheHit()
    {
        string paragraph = new string('前', 1500) + "别动 vendor 目录" + new string('后', 1500);
        string transcript = HistoryTranscript.Render([new ChatMessage(ChatRole.User, paragraph)]);
        await File.WriteAllTextAsync(Path.Combine(_dir, "s.transcript.md"), transcript,
            TestContext.Current.CancellationToken);

        GrepToolResult result = await new PermissiveFileAccessTools(_dir)
            .Grep("vendor", ct: TestContext.Current.CancellationToken);

        GrepFileHits file = Assert.Single(result.Matches);
        Assert.Contains(file.Lines, line => line.Contains("vendor"));
        Assert.All(transcript.Split('\n'), line => Assert.True(line.Length <= HistoryTranscript.WrapChars, line));
    }

    [Fact]
    public void TrySave_SkipsTransientSessions_AndSessionsThatCannotSearch()
    {
        AITool[] searchable = [Named(FileToolNames.Grep), Named(FileToolNames.Read)];
        ChatSession transient = new("t", new CharacterData { CharacterId = "t" }) { IsTransient = true };
        ChatSession persistent = new("p", new CharacterData { CharacterId = "p" });
        persistent.History.Add(new ChatMessage(ChatRole.User, "hi"));

        Assert.Null(HistoryTranscript.TrySave(transient, searchable)); //临时会话写了就是孤儿文件
        Assert.Null(HistoryTranscript.TrySave(persistent, [Named(FileToolNames.Read)])); //没有 Grep 就搜不了
        Assert.Null(HistoryTranscript.TrySave(persistent, null));

        string? path = HistoryTranscript.TrySave(persistent, searchable);
        Assert.NotNull(path);
        Assert.StartsWith("## #1 user", File.ReadAllText(path));
        SessionManager.Instance.Delete(persistent.SessionId);
        Assert.False(File.Exists(path), "删会话要连转录一起删");
    }

    private static AITool Named(string name) => AIFunctionFactory.Create(() => string.Empty, name);
}
