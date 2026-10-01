/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.IO;
using System.Threading.Tasks;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 选定工作区时为项目级 MCP 要的那一次确认：批了才记授权，拒了什么都不落、下次再问
/// </summary>
public class WorkspaceMcpApprovalFlowTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), $"uiharu-mcp-approval-{Guid.NewGuid():N}");

    public WorkspaceMcpApprovalFlowTests()
    {
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, ".mcp.json"),
            """{"mcpServers":{"probe-server":{"command":"echo","args":["probe-arg"]}}}""");
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    [Fact]
    public async Task Confirmed_RecordsApproval()
    {
        RecordingMessageService messages = new() { ConfirmResult = true };

        await new WorkspaceMcpApprovalFlow(messages).PromptAsync(_workspace);

        Assert.Equal(1, messages.ConfirmCount);
        Assert.Empty(McpManager.Instance.GetPendingApprovals(_workspace));
    }

    [Fact]
    public async Task Declined_RecordsNothingAndAsksAgain()
    {
        RecordingMessageService messages = new() { ConfirmResult = false };
        WorkspaceMcpApprovalFlow flow = new(messages);

        await flow.PromptAsync(_workspace);
        await flow.PromptAsync(_workspace);

        Assert.Equal(2, messages.ConfirmCount);
        Assert.Single(McpManager.Instance.GetPendingApprovals(_workspace));
    }

    /// <summary>用户批的是这条命令而不是这个名字，所以命令原文必须逐字摆出来</summary>
    [Fact]
    public async Task Message_ShowsServerNameAndCommandLine()
    {
        RecordingMessageService messages = new() { ConfirmResult = false };

        await new WorkspaceMcpApprovalFlow(messages).PromptAsync(_workspace);

        Assert.Contains("probe-server", messages.LastConfirm);
        Assert.Contains("echo", messages.LastConfirm);
        Assert.Contains("probe-arg", messages.LastConfirm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NoWorkspace_AsksNothing(string? workspace)
    {
        RecordingMessageService messages = new();

        await new WorkspaceMcpApprovalFlow(messages).PromptAsync(workspace);

        Assert.Equal(0, messages.ConfirmCount);
    }
}
