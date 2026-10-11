using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 浏览器调用的审批卡文案：编号翻成「按钮『提交订单』」、附上所在网页，密码只给圆点；
/// 接管卡换成「完成了 / 取消」，不给「本会话允许」——框架按工具名记住它，以后的接管就不等人了
/// </summary>
public class BrowserCallTextTests
{
    private const string Page = "https://shop.example.com/cart?step=2";

    private static BrowserCallPreview Act(string action, BrowserElement? element, string value = "",
        string target = "e21") => new(BrowserTools.ActToolName, action, target, element, value, Page);

    private static string Site => Loc.Text(LangKey.BrowserCallSite, "shop.example.com");

    private static string Named(LangKey role, string name) =>
        Loc.Text(LangKey.BrowserElementNamed, Loc.Text(role), name);

    [Fact]
    public void Click_NamesTheElementAndTheSite()
    {
        string text = BrowserCallText.Describe(Act("click", new BrowserElement("button", "提交订单", false)));

        Assert.Equal(Loc.Text(LangKey.BrowserCallClick, Named(LangKey.BrowserRoleButton, "提交订单")) + "\n" + Site,
            text);
    }

    [Fact]
    public void Upload_ShowsEveryFileInFull()
    {
        string text = BrowserCallText.Describe(Act("upload", new BrowserElement("button", "上传附件", false),
            "/Users/me/project/cv.pdf\n/Users/me/project/cover.pdf"));

        Assert.Equal(Loc.Text(LangKey.BrowserCallUpload, Named(LangKey.BrowserRoleButton, "上传附件"),
            "/Users/me/project/cv.pdf、/Users/me/project/cover.pdf") + "\n" + Site, text);
    }

    [Fact]
    public void PasswordValues_AreMasked()
    {
        string text = BrowserCallText.Describe(Act("fill", new BrowserElement("textbox", "Password", true), "hunter2"));

        Assert.DoesNotContain("hunter2", text);
        Assert.StartsWith(Loc.Text(LangKey.BrowserCallFill, Named(LangKey.BrowserRolePassword, "Password"), "••••••"),
            text);
    }

    /// <summary>翻不回元素（编号过期）就照模型写的原样摆，不猜</summary>
    [Fact]
    public void UnresolvedTargets_AreShownAsWritten()
    {
        string text = BrowserCallText.Describe(Act("click", null));

        Assert.StartsWith(Loc.Text(LangKey.BrowserCallClick, Loc.Text(LangKey.BrowserElementRaw, "e21")), text);
    }

    [Fact]
    public void Navigation_ShowsWhereItGoes_WithoutTheCurrentSite()
    {
        BrowserCallPreview preview = new(BrowserTools.NavigateToolName, string.Empty, string.Empty, null,
            "https://github.com/login", Page);

        Assert.Equal(Loc.Text(LangKey.BrowserCallNavigate, "https://github.com/login"),
            BrowserCallText.Describe(preview));
    }

    [Fact]
    public void WithoutAPage_ThereIsNoSiteLine()
    {
        BrowserCallPreview preview = new(BrowserTools.ActToolName, "back", string.Empty, null, string.Empty, string.Empty);

        Assert.Equal(Loc.Text(LangKey.BrowserCallBack), BrowserCallText.Describe(preview));
    }

    [Fact]
    public void LongValues_AreCut()
    {
        string text = BrowserCallText.Describe(Act("fill", new BrowserElement("textbox", "Note", false), new string('长', 200)));

        Assert.Contains(new string('长', 80) + "…", text);
        Assert.DoesNotContain(new string('长', 81), text);
    }

    [Fact]
    public void PageWidePressAndScroll_NeedNoElement()
    {
        Assert.StartsWith(Loc.Text(LangKey.BrowserCallPressPage, "Enter"),
            BrowserCallText.Describe(Act("press", null, "Enter", target: "")));
        Assert.StartsWith(Loc.Text(LangKey.BrowserCallScrollUp),
            BrowserCallText.Describe(Act("scroll", null, "up", target: "")));
    }

    [Fact]
    public void UnknownRoles_AreCalledElements()
    {
        string text = BrowserCallText.Describe(Act("click", new BrowserElement("gridcell", "", false)));

        Assert.StartsWith(Loc.Text(LangKey.BrowserCallClick,
            Loc.Text(LangKey.BrowserElementUnnamed, Loc.Text(LangKey.BrowserRoleElement))), text);
    }
}
