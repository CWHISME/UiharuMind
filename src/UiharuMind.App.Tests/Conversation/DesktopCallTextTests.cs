using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 桌面调用的审批卡（ADR 0084）：编号翻成「按钮『保存』」并写明所在窗口，密码只给圆点，真实键鼠另起一行提醒；
/// 「本会话允许」按应用记，真实键鼠另记一格；接管卡换成「请你接手这个窗口」
/// </summary>
public class DesktopCallTextTests
{
    private readonly DesktopHost _host = new(new OneWindow());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Window => Loc.Text(LangKey.DesktopCallWindow, "TextEdit", "Note");

    private static string Named(LangKey role, string name) => Loc.Text(LangKey.BrowserElementNamed, Loc.Text(role), name);

    private static ToolApprovalRequestContent Desktop(string command, params (string Key, object? Value)[] args) =>
        new("r1", new FunctionCallContent("c1", ExtensionCalls.CallName, new Dictionary<string, object?>
        {
            ["extension"] = BuiltinExtensions.Desktop,
            ["command"] = command,
            ["arguments"] = JsonSerializer.SerializeToElement(args.ToDictionary(x => x.Key, x => x.Value)),
        }));

    [Fact]
    public async Task Click_NamesTheElementAndTheWindow()
    {
        await SnapshotAsync();

        ApprovalRequestItem card = new(Desktop(BuiltinExtensions.DesktopAct, ("window", "w1"), ("action", "click"),
            ("target", "e1")), sessionId: "s1", desktop: _host);

        Assert.True(card.IsDescribed);
        Assert.Equal(Loc.Text(LangKey.BrowserCallClick, Named(LangKey.BrowserRoleButton, "Save")) + "\n" + Window,
            card.ArgumentSummary);
    }

    [Fact]
    public async Task PasswordValues_AreMasked_AndRealInputIsCalledOut()
    {
        await SnapshotAsync();

        ApprovalRequestItem card = new(Desktop(BuiltinExtensions.DesktopAct, ("window", "w1"), ("action", "fill"),
            ("target", "e2"), ("value", "hunter2"), ("input", "real")), sessionId: "s1", desktop: _host);

        Assert.DoesNotContain("hunter2", card.ArgumentSummary);
        Assert.Contains(Loc.Text(LangKey.DesktopCallReal), card.ArgumentSummary);
    }

    [Fact]
    public async Task RealInput_IsAllowedForTheSessionSeparately()
    {
        await SnapshotAsync();
        List<string> approved = new();
        ApprovalRequestItem card = new(Desktop(BuiltinExtensions.DesktopAct, ("window", "w1"), ("action", "click"),
            ("target", "e1"), ("input", "real")), sessionId: "s1", desktop: _host)
        {
            ApproveExtensionCallback = approved.Add,
        };

        Assert.True(card.CanApproveForSession);
        Assert.Contains(Loc.Text(LangKey.DesktopSessionReal, ""), card.SessionButtonText);
        card.ResolveCommand.Execute("session");

        Assert.Equal(["desktop:real com.apple.TextEdit"], approved);
    }

    /// <summary>「本会话允许」写明是哪个应用，记进名单的也是这个应用：放行记事本不连带放行别的</summary>
    [Fact]
    public async Task SessionApproval_IsRecordedForTheApp()
    {
        await SnapshotAsync();
        List<string> approved = new();
        ApprovalRequestItem card = new(Desktop(BuiltinExtensions.DesktopAct, ("window", "w1"), ("action", "click"),
            ("target", "e1")), sessionId: "s1", desktop: _host)
        {
            ApproveExtensionCallback = approved.Add,
        };

        Assert.Contains("TextEdit", card.SessionButtonText);
        card.ResolveCommand.Execute("session");

        Assert.Equal(["desktop:com.apple.TextEdit"], approved);
    }

    [Fact]
    public async Task HandoffCard_AsksTheUserToTakeOverTheWindow()
    {
        await SnapshotAsync();

        ApprovalRequestItem card = new(Desktop(BuiltinExtensions.DesktopHandoff, ("window", "w1"), ("task", "输入密码")),
            sessionId: "s1", desktop: _host);

        Assert.True(card.IsHandoff);
        Assert.False(card.IsBrowserHandoff);
        Assert.False(card.CanApproveForSession);
        Assert.Equal(Loc.Text(LangKey.DesktopHandoffTitle), card.HandoffTitle);
        Assert.Equal("输入密码\n" + Window, card.ArgumentSummary);
    }

    private async Task SnapshotAsync()
    {
        await _host.ListWindowsAsync(Token);
        await _host.SnapshotAsync("s1", "w1", Token);
    }

    // 一个记事本窗口：一个按钮、一个密码框
    private sealed class OneWindow : IDesktopPlatform
    {
        private readonly DesktopWindowInfo _window = new(1, "TextEdit", "com.apple.TextEdit", "Note", new AxRect(0, 0, 400, 300))
        {
            Handle = "note",
        };

        public string? Unavailable => null;

        public double SecondsSinceUserInput => 60;

        public string? DialogPathHint => null;

        public void RequestAccess()
        {
        }

        public IReadOnlyList<DesktopWindowInfo> ListWindows() => [_window];

        public bool SameWindow(DesktopWindowInfo a, DesktopWindowInfo b) => Equals(a.Handle, b.Handle);

        public AxNode ReadWindow(DesktopWindowInfo window, int maxNodes) => new("window", "Note")
        {
            Children = [new AxNode("button", "Save"), new AxNode("textbox", "Password") { Secret = true }],
        };

        public bool TryPerform(AxNode node, EDesktopAction action, string value) => true;

        public void Input(DesktopWindowInfo window, DesktopInput input, bool real)
        {
        }

        public byte[] CaptureWindow(DesktopWindowInfo window) => [];

        public bool Raise(DesktopWindowInfo window) => true;

        public void Open(string target, bool isFile)
        {
        }
    }
}
