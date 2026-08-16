/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 能力快照的记账口径。
///
/// 这块面板存在的意义是让「每轮固定开销」看得见，所以它算错的代价不是显示难看，
/// 而是用户按一个错的数去关能力、或者根本不去关。两个必守的口径：
/// <b>不重复计</b>（MCP 自述同时出现在自述段与 MCP 分组里）、
/// <b>不漏计</b>（一个工具都没挂的智能体仍然有角色提示与工作区规矩要报）。
/// </summary>
public class CapabilitySnapshotTests
{
    [Fact]
    public void Capture_CountsPromptSegments_OnTopOfToolDefinitions()
    {
        AgentPromptSegment persona = new(EPromptSection.Character, "I am a helpful little agent.");
        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.Capture(
            [new AgentToolEntry(EAgentCapability.WebSearch, Tool("search", "search the web"))],
            McpToolSet.Empty, [persona]);

        int personaTokens = ToolTokenEstimator.EstimateText(persona.Text);
        Assert.True(personaTokens > 0, "角色段应当分得出 token");
        Assert.Equal(personaTokens, snapshot.PromptTokensOf(EPromptSection.Character));
        Assert.Equal(snapshot.BuiltInTools.Sum(x => x.EstimatedTokens) + personaTokens,
            snapshot.EstimatedTokens);
    }

    /// <summary>
    /// MCP 自述只计一次。它既是系统提示里的一段，又挂在 MCP 分组的 <c>InstructionsEstimatedTokens</c> 上，
    /// 把两边都加进合计的话，接一个带长自述的 server 会让固定开销凭空翻倍。
    /// </summary>
    [Fact]
    public void Capture_DoesNotCountMcpNotesTwice()
    {
        const int notesTokens = 120;
        McpToolSet mcp = new()
        {
            Instructions = "## demo\nuse the demo server.",
            Groups =
            [
                new McpServerToolGroup
                {
                    ServerName = "demo",
                    Tools = [],
                    InstructionsInjected = true,
                    InstructionsEstimatedTokens = notesTokens,
                },
            ],
        };

        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.Capture([], mcp,
            [new AgentPromptSegment(EPromptSection.Mcp, mcp.Instructions)]);

        //自述那一段仍然登记在册(提示词明细要能看到它),但只从 MCP 分组那一侧计入合计
        Assert.Single(snapshot.PromptSegments);
        Assert.Equal(notesTokens, snapshot.EstimatedTokens);
    }

    /// <summary>
    /// 一个工具都没挂时仍要报提示词。此处曾经直接早退返回空快照——
    /// 结果是「能力全关」这种最该看清账的场合，账反而整个没了。
    /// </summary>
    [Fact]
    public void Capture_KeepsPromptSegments_WhenNoToolIsMounted()
    {
        AgentCapabilitySnapshot snapshot = AgentCapabilitySnapshot.Capture(null, McpToolSet.Empty,
            [new AgentPromptSegment(EPromptSection.Workspace, "never touch the vendor folder")]);

        Assert.Empty(snapshot.BuiltInTools);
        Assert.True(snapshot.PromptTokensOf(EPromptSection.Workspace) > 0);
        Assert.Equal(snapshot.EstimatedTokens, snapshot.PromptTokensOf(EPromptSection.Workspace));
    }

    /// <summary>没登记的段问出来是 0，而不是抛</summary>
    [Fact]
    public void PromptTokensOf_ReturnsZero_ForSectionsThatWereNeverEmitted()
    {
        Assert.Equal(0, AgentCapabilitySnapshot.Empty.PromptTokensOf(EPromptSection.Workspace));
    }

    /// <summary>
    /// schema 的排版不该计入占用：请求体里发的是压缩形态，而
    /// <c>JsonElement.GetRawText()</c> 还的是解析前那份原文。
    /// 曾经直接拿原文分词，一个回缩进 JSON 的 MCP server 因此虚高三倍多
    /// （估 21k、实际约 6.5k）——用户会照着那个数去关一个其实并不贵的 server。
    /// </summary>
    [Fact]
    public void EstimateSchema_IgnoresJsonFormatting()
    {
        const string compact =
            """{"type":"object","properties":{"path":{"type":"string","description":"the path"}}}""";
        const string indented = """
                                {
                                    "type": "object",
                                    "properties": {
                                        "path": {
                                            "type": "string",
                                            "description": "the path"
                                        }
                                    }
                                }
                                """;

        int fromCompact = ToolTokenEstimator.EstimateSchema(JsonDocument.Parse(compact).RootElement);
        int fromIndented = ToolTokenEstimator.EstimateSchema(JsonDocument.Parse(indented).RootElement);

        Assert.Equal(fromCompact, fromIndented);
        //也不能是"两边都算成 0"这种同样相等的退化情形
        Assert.True(fromCompact > 0);
    }

    private static AITool Tool(string name, string description)
    {
        return AIFunctionFactory.Create(() => "ok", name, description);
    }
}
