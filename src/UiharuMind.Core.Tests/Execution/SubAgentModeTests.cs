using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 派活的档位参数（ADR 0112）：<c>mode</c> 收 <c>general</c> / <c>explore</c> / <c>review</c>。
///
/// 之前这里是一个 <c>isExplore: bool</c>。换掉它不只是好看：布尔只放得下两档，加第三档时模型
/// 只能靠猜组合（两个布尔？还是干脆不传？），而猜错的后果是「以为派了评审，实际派了个通用档」——
/// 派活方没有任何渠道能发现。解析失败也**不静默降级**：当场回绝并列出可选值。
/// </summary>
public class SubAgentModeTests
{
    /// <summary>规范值</summary>
    [Theory]
    [InlineData("general", ESubAgentType.General)]
    [InlineData("explore", ESubAgentType.Explorer)]
    [InlineData("review", ESubAgentType.Reviewer)]
    public void ParsesCanonicalNames(string text, ESubAgentType expected)
    {
        Assert.True(SubAgentMode.TryParse(text, out ESubAgentType type));
        Assert.Equal(expected, type);
    }

    /// <summary>大小写与空白不该影响判定：模型常把参数写成「Review」或带换行</summary>
    [Theory]
    [InlineData(" Review ")]
    [InlineData("REVIEW")]
    [InlineData("review\n")]
    public void TrimsAndIgnoresCase(string text)
    {
        Assert.True(SubAgentMode.TryParse(text, out ESubAgentType type));
        Assert.Equal(ESubAgentType.Reviewer, type);
    }

    /// <summary>空白 = 没填 = 默认档。这条要成立，否则不传参数的普通委派会变成一个错误</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_MeansDefaultGeneral(string? text)
    {
        Assert.True(SubAgentMode.TryParse(text, out ESubAgentType type));
        Assert.Equal(ESubAgentType.General, type);
    }

    /// <summary>模型把用户原话直接塞进来的场合认中文，比让它重试一次便宜</summary>
    [Theory]
    [InlineData("通用", ESubAgentType.General)]
    [InlineData("探索", ESubAgentType.Explorer)]
    [InlineData("评审", ESubAgentType.Reviewer)]
    public void AcceptsChineseAliases(string text, ESubAgentType expected)
    {
        Assert.True(SubAgentMode.TryParse(text, out ESubAgentType type));
        Assert.Equal(expected, type);
    }

    /// <summary>枚举名/英文词直接塞进来也认：review 收 reviewer，explore 就得收 explorer——两边不对称＝白烧一次往返</summary>
    [Theory]
    [InlineData("explorer", ESubAgentType.Explorer)]
    [InlineData("reviewer", ESubAgentType.Reviewer)]
    public void AcceptsEnumStyleNames(string text, ESubAgentType expected)
    {
        Assert.True(SubAgentMode.TryParse(text, out ESubAgentType type));
        Assert.Equal(expected, type);
    }

    /// <summary>认不出来要当场说清楚：静默落进通用档等于把「评审」悄悄换成「通用」</summary>
    [Theory]
    [InlineData("explor")]
    [InlineData("reviewer2")]
    [InlineData("brain")]
    public void Unknown_IsRejected_NotSilentlyDefaulted(string text)
    {
        Assert.False(SubAgentMode.TryParse(text, out ESubAgentType type));
        Assert.Equal(ESubAgentType.General, type); // 失败时给的是无害的默认值，调用方必须先看返回值
    }

    /// <summary>同一条不变量：委派不该能派出一个脑——脑有自己那把工具，且语义完全不同</summary>
    [Fact]
    public void BrainIsNotAValidMode()
    {
        Assert.False(SubAgentMode.TryParse("brain", out _));
        Assert.DoesNotContain("brain", SubAgentMode.All);
    }

    /// <summary>
    /// 参数说明里必须列出每一个可选值：它要作为 <c>[Description]</c> 的常量，没法从
    /// <see cref="SubAgentMode.All"/> 拼出来，所以在这里钉住两边一致——漏一个值时模型看不到、
    /// 也就不会用，而那种漏不会报错。
    /// </summary>
    [Fact]
    public void ModeParamDocument_ListsEveryValue()
    {
        foreach (string mode in SubAgentMode.All)
        {
            Assert.Contains(mode, SubAgentToolPrompts.ModeParam);
        }
    }

    /// <summary>回绝话要把可选值原样写出来：只说「参数不合法」，模型会原样再试一次</summary>
    [Fact]
    public void Reject_ListsTheOptions()
    {
        string text = SubAgentMode.Reject("explorr");

        Assert.Contains("explorr", text);
        foreach (string mode in SubAgentMode.All) Assert.Contains(mode, text);
    }

}
