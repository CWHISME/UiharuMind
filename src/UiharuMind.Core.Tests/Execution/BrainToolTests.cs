using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 脑工具（ADR 0082）的参数面与纯函数：schema、报告裁剪、回执契约、追问路由的错误分支。
///
/// 跑模型的那条主路径不在单测里（与委派同口径，走开发脚本/实机）；这里钉住的是
/// 模型侧看得见的形状，以及不烧模型的错误分支。
/// </summary>
public class BrainToolTests
{
    private static SubAgentTool.LaunchContext Context(string parent = "parent") => new()
    {
        ParentSessionId = parent,
        Profile = SubAgentProfile.Brain,
        Roster = [],
    };

    private static AIFunction Brain() =>
        Assert.IsAssignableFrom<AIFunction>(BrainTool.Create(Context()));

    [Fact]
    public void Brain_OnlyTaskIsRequired_BrainIdOptional()
    {
        Assert.Equal(["task"], Required(Brain()));
    }

    [Fact]
    public async Task Run_EmptyTask_Errors()
    {
        string result = await BrainTool.RunAsync(Context(), "  ", null, TestContext.Current.CancellationToken);

        Assert.StartsWith("Error", result);
        Assert.Contains("task", result);
    }

    /// <summary>brainId 查无此脑：按「没有这个脑」收口，不开新脑也不续别的</summary>
    [Fact]
    public async Task Run_UnknownBrainId_Errors()
    {
        string result = await BrainTool.RunAsync(Context(), "总结", "nope9999", TestContext.Current.CancellationToken);

        Assert.StartsWith("Error", result);
        Assert.Contains("nope9999", result);
    }

    /// <summary>别的会话派出去的脑不归本会话追问（同委派的归属不变量）</summary>
    [Fact]
    public async Task Run_OtherParentsBrain_IsUnknown()
    {
        string id = "brain-other0001";
        SessionManager.Instance.Add(new ChatSession
        {
            SessionId = id,
            ParentSessionId = "someone-else",
            SubAgentType = ESubAgentType.Brain,
        });
        try
        {
            string result = await BrainTool.RunAsync(Context("parent"), "总结", id, TestContext.Current.CancellationToken);

            Assert.StartsWith("Error", result); //归属不对，不续跑
        }
        finally
        {
            SessionManager.Instance.Delete(id);
        }
    }

    /// <summary>追问只认真脑会话：拿一个普通子会话的编号来追问，不算数</summary>
    [Fact]
    public async Task Run_NonBrainSubSession_IsUnknown()
    {
        string id = "brain-notbrain01";
        SessionManager.Instance.Add(new ChatSession
        {
            SessionId = id,
            ParentSessionId = "parent",
            SubAgentType = ESubAgentType.General,
        });
        try
        {
            string result = await BrainTool.RunAsync(Context("parent"), "总结", id, TestContext.Current.CancellationToken);

            Assert.StartsWith("Error", result); //不是脑，不续跑
        }
        finally
        {
            SessionManager.Instance.Delete(id);
        }
    }

    //================= CapReport =================

    [Fact]
    public void CapReport_UnderBudget_Unchanged()
    {
        const string report = "结论：文件不大，直接给全文。";

        Assert.Equal(report, BrainTool.CapReport(report));
    }

    [Fact]
    public void CapReport_OverBudget_TruncatesAtCharBoundaryAndNotes()
    {
        // 中文每字 3 字节。预算取在一个字符中间：截断必须落在字符边界，不劈开 UTF-8 序列
        string report = new string('我', 1000); //3000 字节，远超预算
        const int budget = 9; //只能放 3 个字

        string capped = BrainTool.CapReport(report, budget);

        Assert.Equal(3, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(capped).Take(budget).ToArray()).Length);
        Assert.Contains("brainId", capped); //附言点名可追问
        Assert.True(Encoding.UTF8.GetByteCount(capped) <= budget + Encoding.UTF8.GetByteCount(BrainTool.TruncationNote),
            "正文字节数超出预算");
    }

    //================= 回执契约 =================

    /// <summary>回执内容在前、标识在末行（ToolCallItem.ParseSubSessionId 认末行，App 侧再钉一遍）</summary>
    [Fact]
    public void BuildReceipt_EndsWithShortIdMarker()
    {
        string receipt = BrainTool.BuildReceipt("aaaa1111bbbb2222", "结论：这是一个总结。");

        Assert.StartsWith("结论：这是一个总结。", receipt);
        Assert.EndsWith($"[sub-session: {SubSessionIdAlias.Short("aaaa1111bbbb2222")}]", receipt);
    }

    private static IEnumerable<string> Required(AIFunction function) =>
        function.JsonSchema.TryGetProperty("required", out JsonElement required)
            ? required.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : [];
}
