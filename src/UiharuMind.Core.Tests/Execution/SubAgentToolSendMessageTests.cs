using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// SendMessage 的参数面（ADR 0044 之后唯一一把委派工具）。
///
/// 实测踩过的坑：<c>to</c> 曾没有默认值，于是 AIFunctionFactory 把它在 schema 里标成
/// <b>必填</b>——模型要么瞎填一个不在名单里的名字（"default"）被拒、要么不传直接报
/// "missing a value for the required parameter 'to'"。两条测试把两个方向都钉死：
/// schema 里 to 不是必填，以及 "default" 与留空等价。
/// </summary>
public class SubAgentToolSendMessageTests
{
    private static AITool Tool() => SubAgentTool.Create(new SubAgentTool.LaunchContext
    {
        ParentSessionId = "parent",
        Profile = SubAgentProfile.General,
        Roster = [],
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeTo_Blank_FallsThroughToTheDefaultHelper(string? to)
    {
        Assert.Null(SubAgentTool.NormalizeTo(to));
    }

    [Theory]
    [InlineData("Alice", "Alice")]
    [InlineData("  Bob  ", "Bob")]
    public void NormalizeTo_NamedRecipients_PassThroughTrimmed(string? to, string expected)
    {
        Assert.Equal(expected, SubAgentTool.NormalizeTo(to));
    }

    /// <summary>
    /// "default" 不是合法收件人：它不是名单成员，也不该被当作「默认对象」的特例——
    /// 模型填了它就该收到 UnknownRecipient 的报错，由此学会「留空 = 默认对象」。
    /// 曾经有过宽容它的版本，副作用是把「default 是个名字」的错误心智固化下来，已撤销
    /// </summary>
    [Fact]
    public void NormalizeTo_DoesNotSpecialCaseDefault()
    {
        Assert.Equal("default", SubAgentTool.NormalizeTo("default"));
        Assert.Equal("DEFAULT", SubAgentTool.NormalizeTo(" DEFAULT "));
    }

    /// <summary>
    /// 回执末行整行粘进 to 也认：实测模型会把 "[sub-session: xxx]" 原样当参数传过来，
    /// 剥出方括号里的编号后按续跑走，而不是报 UnknownRecipient。
    /// </summary>
    [Theory]
    [InlineData("[sub-session: 1e44d522c1a644799bebfdd4ce1d11df]", "1e44d522c1a644799bebfdd4ce1d11df")]
    [InlineData("  [sub-session: abc123]  ", "abc123")]
    [InlineData("[SUB-SESSION: abc123]", "abc123")]
    public void NormalizeTo_StripsTheSubSessionMarker(string? to, string expected)
    {
        Assert.Equal(expected, SubAgentTool.NormalizeTo(to));
    }

    /// <summary>裸编号本来就走续跑分支，宽容改动不得把它搞坏</summary>
    [Fact]
    public void NormalizeTo_BareIdPassesThrough()
    {
        Assert.Equal("1e44d522c1a644799bebfdd4ce1d11df",
            SubAgentTool.NormalizeTo("1e44d522c1a644799bebfdd4ce1d11df"));
    }

    /// <summary>to 不在 required 里：模型不传这一键时框架不报错，落到默认对象</summary>
    [Fact]
    public void SendMessage_ToIsOptionalInTheSchema()
    {
        AIFunction function = Assert.IsAssignableFrom<AIFunction>(Tool());
        JsonElement schema = function.AsDeclarationOnly().JsonSchema;
        if (!schema.TryGetProperty("required", out JsonElement required)) return; //没标 required 即已是可空

        foreach (JsonElement item in required.EnumerateArray())
        {
            Assert.NotEqual("to", item.GetString());
        }
    }
}
