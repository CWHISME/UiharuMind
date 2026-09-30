using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 成员写过的文件按会话缓存：产物区每次刷新都要问一遍，而成员没有界面壳钉着，历史冷下来就被卸掉——
/// 不缓存的话每问一次就把整份历史读回来，清扫时再整份写一遍，周而复始
/// </summary>
public sealed class GroupWrittenPathsTests : IDisposable
{
    private readonly ChatSession _member = SessionManager.Instance.StartNewSession(new CharacterData { CharacterName = "W" });
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"written-{Guid.NewGuid():N}");

    public void Dispose() => SessionManager.Instance.Delete(_member.SessionId);

    [Fact]
    public void UnloadedMember_AnswersFromCache_WithoutReloadingHistory()
    {
        string spec = Write("1", "spec.md");
        Assert.Equal([spec], GroupWrittenPaths.Of(_member, new AgentPathResolver(_workspace)));

        Assert.True(_member.UnloadHistory());
        Assert.Equal([spec], GroupWrittenPaths.Of(_member, new AgentPathResolver(_workspace)));

        Assert.False(_member.IsHistoryResident);
    }

    [Fact]
    public void AppendAfterCaching_IsSeenOnceUnloaded()
    {
        string spec = Write("1", "spec.md");
        Assert.Equal([spec], GroupWrittenPaths.Of(_member, new AgentPathResolver(_workspace)));

        string code = Write("2", "code.cs"); //追加落盘：缓存作废
        Assert.True(_member.UnloadHistory());

        Assert.Equal([spec, code], GroupWrittenPaths.Of(_member, new AgentPathResolver(_workspace)));
    }

    [Fact]
    public void DifferentWorkspace_IsNotServedFromCache()
    {
        Write("1", "spec.md");
        GroupWrittenPaths.Of(_member, new AgentPathResolver(_workspace));
        Assert.True(_member.UnloadHistory());

        string other = Path.Combine(Path.GetTempPath(), $"written-other-{Guid.NewGuid():N}");
        Assert.Equal([Path.Combine(other, "spec.md")], GroupWrittenPaths.Of(_member, new AgentPathResolver(other)));
    }

    /// <summary>成员成功写了一个文件，并按常规落盘（追加）</summary>
    /// <returns>写的文件的绝对路径</returns>
    private string Write(string callId, string relative)
    {
        int before = _member.History.Count;
        _member.History.Add(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, FileToolNames.Write, new Dictionary<string, object?> { ["filePath"] = relative })]));
        _member.History.Add(new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(callId, $"Saved '{relative}' (1 lines).")]));
        _member.SaveAppended(before);
        return Path.Combine(_workspace, relative);
    }
}
