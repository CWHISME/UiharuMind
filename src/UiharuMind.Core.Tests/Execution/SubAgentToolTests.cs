using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 委派两把工具的参数面（ADR 0065）：<c>CreateAgent</c> 新开、<c>SendMessage</c> 续聊。
///
/// 拆开之前 <c>to</c> 一个参数兼三义（留空新开 / 人名 / 编号），实测弱模型往里编名字、该续不续。
/// 这里钉住拆开后的两条边界：没挂子角色时 schema 里没有可填名字的地方；续聊只收编号，
/// 填错或留空都把两条路一起指给它。
/// </summary>
public class SubAgentToolTests
{
    private static IReadOnlyList<AITool> Tools(params SubAgentChoice[] roster) => SubAgentTool.Create(
        new SubAgentTool.LaunchContext
        {
            ParentSessionId = "parent",
            Profile = SubAgentProfile.General,
            Roster = roster,
        });

    private static AIFunction Launcher(params SubAgentChoice[] roster) =>
        Assert.IsAssignableFrom<AIFunction>(Tools(roster).Single(x => x.Name == SubAgentTool.ToolName));

    private static AIFunction Messenger() =>
        Assert.IsAssignableFrom<AIFunction>(Tools().Single(x => x.Name == SubAgentTool.MessageToolName));

    [Fact]
    public void Create_GivesLaunchThenMessage()
    {
        Assert.Equal([SubAgentTool.ToolName, SubAgentTool.MessageToolName], Tools().Select(x => x.Name));
    }

    /// <summary>
    /// 没挂子角色就没有 subagent_type。实测弱模型见到「可以填名字」就自己造一个
    /// （general、reviewer…），干脆不给它填的地方
    /// </summary>
    [Fact]
    public void Agent_WithoutRoster_HasNoPlaceForANameAtAll()
    {
        JsonElement properties = Launcher().JsonSchema.GetProperty("properties");

        Assert.False(properties.TryGetProperty("subagent_type", out _));
        Assert.True(properties.TryGetProperty("prompt", out _));
    }

    /// <summary>挂了子角色：名字连同用途列在要填名字的地方</summary>
    [Fact]
    public void Agent_WithRoster_ListsThePeopleOnSubagentType()
    {
        string description = Launcher(new SubAgentChoice("Alice", "审代码", "alice-id"), new SubAgentChoice("Bob", "", "bob-id"))
            .JsonSchema.GetProperty("properties").GetProperty("subagent_type").GetProperty("description").GetString() ?? "";

        Assert.Contains("- Alice: 审代码", description);
        Assert.Contains("- Bob", description);
    }

    /// <summary>「同一主题回到同一个人」写在模型正打算新开的那一把上，并指名续聊工具</summary>
    [Fact]
    public void Agent_Description_SendsFollowUpsToTheSameOne()
    {
        string description = Launcher().Description;

        Assert.Contains("same sub-agent", description);
        Assert.Contains(SubAgentTool.MessageToolName, description);
    }

    /// <summary>只有 prompt 必填：role / model / subagent_type 都可不传</summary>
    [Fact]
    public void Agent_OnlyPromptIsRequired()
    {
        Assert.Equal(["prompt"], Required(Launcher(new SubAgentChoice("Alice", "", "alice-id"))));
    }

    [Fact]
    public void SendMessage_ToAndMessageAreRequired()
    {
        Assert.Equal(["message", "to"], Required(Messenger()).Order());
    }

    [Fact]
    public async Task Agent_UnknownSubagentType_ListsTheRoster()
    {
        AIFunction launcher = Launcher(new SubAgentChoice("Alice", "", "alice-id"));

        string result = await Invoke(launcher, new AIFunctionArguments { ["prompt"] = "看看", ["subagent_type"] = "reviewer" });

        Assert.Contains("no one called 'reviewer'", result);
        Assert.Contains("Alice", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeTo_Blank_IsNull(string? to)
    {
        Assert.Null(SubAgentTool.NormalizeTo(to));
    }

    [Fact]
    public void NormalizeTo_Trims()
    {
        Assert.Equal("abc123", SubAgentTool.NormalizeTo("  abc123  "));
    }

    /// <summary>
    /// 回执末行整行粘进 to 也认：实测模型会把 "[sub-session: xxx]" 原样当参数传过来
    /// </summary>
    [Theory]
    [InlineData("[sub-session: 1e44d522c1a644799bebfdd4ce1d11df]", "1e44d522c1a644799bebfdd4ce1d11df")]
    [InlineData("  [sub-session: abc123]  ", "abc123")]
    [InlineData("[SUB-SESSION: abc123]", "abc123")]
    public void NormalizeTo_StripsTheSubSessionMarker(string? to, string expected)
    {
        Assert.Equal(expected, SubAgentTool.NormalizeTo(to));
    }

    /// <summary>留空是想新开：报错指回 Agent，而不是悄悄开一个</summary>
    [Fact]
    public async Task SendMessage_EmptyTo_PointsBackToAgent()
    {
        string result = await Send("");

        Assert.StartsWith("Error", result);
        Assert.Contains($"`{SubAgentTool.ToolName}`", result);
    }

    /// <summary>
    /// 回执给的是短号，<c>to</c> 填短号必须走到续跑那条路。撞号分支只有经反查才走得到，
    /// 用它钉住「路由按短号反查」（唯一命中分支会真的起一轮，不在单测里跑）
    /// </summary>
    [Fact]
    public async Task SendMessage_ShortIdCollision_ListsFullIds()
    {
        string idA = "sendshort-0001";
        string idB = "sendshort-0002";
        SessionManager.Instance.Add(new ChatSession { SessionId = idA, ParentSessionId = "parent" });
        SessionManager.Instance.Add(new ChatSession { SessionId = idB, ParentSessionId = "parent" });
        try
        {
            string result = await Send("[sub-session: sendshort]");

            Assert.DoesNotContain("no conversation", result);
            Assert.Contains(idA, result);
            Assert.Contains(idB, result);
        }
        finally
        {
            SessionManager.Instance.Delete(idA);
            SessionManager.Instance.Delete(idB);
        }
    }

    /// <summary>别的会话派出的子会话不归本会话续</summary>
    [Fact]
    public async Task SendMessage_OtherParentsRun_IsUnknown()
    {
        string id = "sendother-0001";
        SessionManager.Instance.Add(new ChatSession { SessionId = id, ParentSessionId = "someone-else" });
        try
        {
            Assert.Contains("no conversation", await Send("sendothe"));
        }
        finally
        {
            SessionManager.Instance.Delete(id);
        }
    }

    /// <summary>
    /// 认不出编号时把聊过的人列出来照抄。实测只说「去回执里找标识」，模型凭印象补写对不上，
    /// 转头另起新人，前面做过的全丢
    /// </summary>
    [Fact]
    public async Task SendMessage_UnknownId_ListsEarlierConversations()
    {
        string id = "listearly0001aa";
        SessionManager.Instance.Add(new ChatSession { SessionId = id, ParentSessionId = "parent", Title = "审查员" });
        try
        {
            string result = await Send("someone-made-up");

            Assert.Contains("no conversation", result);
            Assert.Contains($"- {SubSessionIdAlias.Short(id)} — 审查员", result);
        }
        finally
        {
            SessionManager.Instance.Delete(id);
        }
    }

    private static Task<string> Send(string to) =>
        Invoke(Messenger(), new AIFunctionArguments { ["message"] = "继续", ["to"] = to });

    private static async Task<string> Invoke(AIFunction function, AIFunctionArguments arguments)
    {
        object? raw = await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        return raw is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString() ?? string.Empty
            : raw?.ToString() ?? string.Empty;
    }

    private static IEnumerable<string> Required(AIFunction function) =>
        function.JsonSchema.TryGetProperty("required", out JsonElement required)
            ? required.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : [];
}
