/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.RegularExpressions;
using PuppeteerSharp;
using PuppeteerSharp.Locators;
using PuppeteerSharp.PageAccessibility;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 标签页上的动作：把编号或文字落到元素上，点、填、选、按键，并等动作引起的跳转落定
/// </summary>
internal sealed partial class PuppeteerTab
{
    private const int ActionTimeoutMs = 10_000;
    private const int NavigationGraceMs = 700; //动作之后这么久里没起跳，就当它不跳转

    // 点链接、提交表单、回车搜索都可能跳转：动作前就挂上导航等待，动作后一小会儿里主框架起跳了就等新页面就绪，
    // 不然拍到的是半路上的空页
    private async Task<string> PerformAndWaitAsync(BrowserActionRequest request)
    {
        Task<IResponse> navigation = _page.WaitForNavigationAsync(new NavigationOptions
        {
            Timeout = NavigationTimeoutMs,
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
        });
        // 不跳转时它会一直等到超时：没人接的异常要吞掉
        _ = navigation.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRequest(object? sender, RequestEventArgs e)
        {
            if (e.Request.IsNavigationRequest && e.Request.Frame == _page.MainFrame) started.TrySetResult();
        }

        _page.Request += OnRequest;
        try
        {
            string note = await PerformAsync(request);
            Task grace = Task.Delay(NavigationGraceMs);
            if (await Task.WhenAny(started.Task, navigation, grace) != grace)
            {
                try
                {
                    await navigation;
                }
                catch (Exception e) when (e is TimeoutException or PuppeteerException)
                {
                    Log.Debug($"[Browser] navigation after action did not finish: {e.Message}");
                }
            }

            return note;
        }
        finally
        {
            _page.Request -= OnRequest;
        }
    }

    private async Task<string> PerformAsync(BrowserActionRequest request)
    {
        switch (request.Action)
        {
            case EBrowserAction.Back:
                await _page.GoBackAsync(new NavigationOptions { Timeout = NavigationTimeoutMs });
                return "Went back.";
            case EBrowserAction.Scroll:
                bool up = string.Equals(request.Value, "up", StringComparison.OrdinalIgnoreCase);
                await _page.EvaluateExpressionAsync($"window.scrollBy(0, {(up ? -1 : 1)} * window.innerHeight * 0.8)");
                return up ? "Scrolled up." : "Scrolled down.";
            case EBrowserAction.Press when request.Target.Length == 0:
                await PressKeysAsync(request.Value);
                return $"Pressed {request.Value}.";
        }

        AxNode node = Resolve(request.Target);
        EBrowserAction action = request.Action;
        string value = request.Value;
        // 收起的原生下拉框里的选项没有位置、点不着：点它、对它 select，都改成在所属下拉框上选这一项
        if (action is EBrowserAction.Click or EBrowserAction.Select or EBrowserAction.Check &&
            _pickerOptions.TryGetValue(node, out AxNode? picker))
        {
            value = node.Name.Trim();
            node = picker;
            action = EBrowserAction.Select;
        }

        if (node.Source is not SerializedAXNode source || await source.ElementHandleAsync() is not { } handle)
        {
            throw new BrowserActionException(
                $"{Describe(node)} is no longer on the page. Take a new snapshot and use its refs.");
        }

        try
        {
            Locator locator = handle.AsLocator().SetTimeout(ActionTimeoutMs);
            switch (action)
            {
                case EBrowserAction.Click:
                    await locator.ClickAsync();
                    return $"Clicked {Describe(node)}.";
                case EBrowserAction.Hover:
                    await locator.HoverAsync();
                    return $"Hovered {Describe(node)}.";
                case EBrowserAction.Fill:
                    await locator.FillAsync(value);
                    return $"Filled {Describe(node)}.";
                case EBrowserAction.Select:
                    return await SelectAsync(handle, node, value);
                case EBrowserAction.Check:
                case EBrowserAction.Uncheck:
                    bool wanted = action == EBrowserAction.Check;
                    if (node.Checked == "true" != wanted) await locator.ClickAsync();
                    return $"{(wanted ? "Checked" : "Unchecked")} {Describe(node)}.";
                case EBrowserAction.Press:
                    await handle.FocusAsync();
                    await PressKeysAsync(value);
                    return $"Pressed {value} on {Describe(node)}.";
                default:
                    throw new BrowserActionException($"Unsupported action: {action}.");
            }
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }

    private AxNode Resolve(string target)
    {
        string key = AxSnapshotSearch.NormalizeTarget(target);
        if (key.Length == 0)
        {
            throw new BrowserActionException("This action needs a target: a ref from the latest snapshot (e.g. e5).");
        }

        if (_refs.TryGetValue(key, out AxNode? byRef)) return byRef;
        if (RefPattern().IsMatch(key))
        {
            throw new BrowserActionException(
                $"No {key} in the latest snapshot. Refs are renumbered every time a snapshot is taken.");
        }

        // 没有编号的元素（div 做的按钮）按看得见的文字找
        if (_tree != null && AxSnapshotSearch.FindByText(_tree, key) is { } byText) return byText;
        throw new BrowserActionException($"Nothing on the page shows the text \"{key}\".");
    }

    private static async Task<string> SelectAsync(IElementHandle handle, AxNode node, string wanted)
    {
        // 模型给的是看得见的选项文字，<select> 只认 value：先在页面里按文字或 value 找到那一项
        string[]? values = await handle.EvaluateFunctionAsync<string[]?>(
            """
            (el, wanted) => {
                if (!(el instanceof HTMLSelectElement)) return null;
                const norm = s => s.replace(/\s+/g, ' ').trim();
                const hit = [...el.options].find(o =>
                    norm(o.label) === norm(wanted) || norm(o.text) === norm(wanted) || o.value === wanted);
                return hit ? [hit.value] : [];
            }
            """, wanted);
        if (values == null)
        {
            throw new BrowserActionException(
                $"{Describe(node)} is not a native drop-down: click it, then click the option in the new snapshot.");
        }

        if (values.Length == 0)
        {
            string[] labels = await handle.EvaluateFunctionAsync<string[]>(
                "el => [...el.options].map(o => o.label.trim())");
            throw new BrowserActionException(
                $"{Describe(node)} has no option \"{wanted}\". Options: {string.Join(" | ", labels)}");
        }

        await handle.SelectAsync(values);
        return $"Selected \"{wanted}\" in {Describe(node)}.";
    }

    // 模型给的按键五花八门（Ctrl+A、Cmd+Enter、esc）：统一成 Puppeteer 的键名，组合键按住修饰键再按、按完松开
    private async Task PressKeysAsync(string keys)
    {
        string[] names = keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeKey).ToArray();
        if (names.Length == 0)
        {
            throw new BrowserActionException("press needs a key, e.g. Enter, Tab, ArrowDown or Control+A.");
        }

        List<string> held = new();
        try
        {
            foreach (string modifier in names[..^1])
            {
                await _page.Keyboard.DownAsync(modifier);
                held.Add(modifier);
            }

            await _page.Keyboard.PressAsync(names[^1]);
        }
        catch (KeyNotFoundException)
        {
            throw new BrowserActionException($"Unknown key \"{keys}\". Use key names such as Enter, Tab, Escape, " +
                                             "ArrowDown, Backspace, or a combination like Control+A.");
        }
        finally
        {
            for (int i = held.Count - 1; i >= 0; i--) await _page.Keyboard.UpAsync(held[i]);
        }
    }

    private static string NormalizeKey(string key) => key.ToLowerInvariant() switch
    {
        "ctrl" or "control" => "Control",
        "cmd" or "command" or "meta" or "win" or "super" => "Meta",
        "alt" or "option" or "opt" => "Alt",
        "shift" => "Shift",
        "esc" or "escape" => "Escape",
        "return" or "enter" => "Enter",
        "del" or "delete" => "Delete",
        "space" or "spacebar" => "Space",
        "up" => "ArrowUp",
        "down" => "ArrowDown",
        "left" => "ArrowLeft",
        "right" => "ArrowRight",
        "pgup" => "PageUp",
        "pgdn" => "PageDown",
        _ => key.Length == 1 ? key : char.ToUpperInvariant(key[0]) + key[1..],
    };

    private static string Describe(AxNode node) =>
        node.Name.Trim() is { Length: > 0 } name ? $"{node.Role} \"{name}\"" : node.Role;

    [GeneratedRegex(@"^e\d+$")]
    private static partial Regex RefPattern();
}
