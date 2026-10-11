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
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 标签页上的动作：把编号或文字落到元素上，点、填、选、按键，并等动作引起的跳转落定
/// </summary>
internal sealed partial class PuppeteerTab
{
    private const int ActionTimeoutMs = 10_000;
    private const int NavigationGraceMs = 700; //动作之后这么久里没起跳，就当它不跳转
    private const int UploadPickerWaitMs = 3000; //上传时点完目标，等文件选择框弹出的时长

    // 密码框的认法。跨 iframe 的元素 instanceof 认不出来，按标签名判；头一次认出就在元素上做个记号——
    // 点了「显示密码」切成明文后照样认得；autocomplete 写着 password、或用 CSS 把字遮成圆点的也算
    private const string IsPasswordScript =
        """
        e => {
            if (e.localName !== 'input') return false;
            if (e.type === 'password') e.__uiharuSecret = true;
            return e.__uiharuSecret === true || /password/i.test(e.autocomplete || '') ||
                (getComputedStyle(e).webkitTextSecurity || 'none') !== 'none';
        }
        """;

    // 真正的焦点：顺着 Shadow DOM 与同源 iframe 往里找（组件库的登录框常在 Shadow DOM 里）；跨源的看不进去，当不是
    private const string FocusIsPasswordScript =
        "() => { const secret = " + IsPasswordScript + """
        ;
            let e = document.activeElement;
            for (;;) {
                const inner = e && ((e.shadowRoot && e.shadowRoot.activeElement) ||
                    (e.contentDocument && e.contentDocument.activeElement));
                if (!inner) break;
                e = inner;
            }
            return !!e && secret(e);
        }
        """;

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
        // 点「上传」会弹系统的文件选择框：模型看不见、关不掉，框里还是用户的整个主目录（实测）。挂着它就不弹，由这里关掉
        Task<FileChooser> chooser = _page.WaitForFileChooserAsync(new WaitForOptions
        {
            Timeout = ActionTimeoutMs + NavigationGraceMs,
        });
        _ = chooser.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

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

            if (request.Action == EBrowserAction.Upload) return await AcceptUploadAsync(chooser, request.Value, note);
            if (chooser.IsCompletedSuccessfully) note += " " + await DismissFileChooserAsync(chooser.Result);
            return note;
        }
        finally
        {
            _page.Request -= OnRequest;
        }
    }

    // 上传就是点开选择框再把文件交进去；框弹得比点击返回晚一点，多等一会儿
    private static async Task<string> AcceptUploadAsync(Task<FileChooser> chooser, string value, string clicked)
    {
        if (await Task.WhenAny(chooser, Task.Delay(UploadPickerWaitMs)) != chooser || !chooser.IsCompletedSuccessfully)
        {
            throw new BrowserActionException(
                $"{clicked} It did not open a file picker: target the upload button or the file input itself.");
        }

        string[] files = value.Split('\n');
        if (files.Length > 1 && !chooser.Result.IsMultiple)
        {
            await DismissFileChooserAsync(chooser.Result);
            throw new BrowserActionException("This file picker takes a single file: upload one file per action.");
        }

        await chooser.Result.AcceptAsync(files);
        return $"Uploaded {string.Join(", ", files.Select(Path.GetFileName))}. {clicked}";
    }

    private static async Task<string> DismissFileChooserAsync(FileChooser chooser)
    {
        try
        {
            await chooser.CancelAsync();
        }
        catch (PuppeteerException e)
        {
            Log.Debug($"[Browser] cancelling a file chooser failed: {e.Message}");
        }

        return "That opened a file picker, which was closed: this tool can't upload files. " +
               "If the task needs a file uploaded, hand the browser over to the user for that step.";
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
                string[] keys = BrowserKeys.Parse(request.Value);
                if (BrowserKeys.TypesText(keys) && await _page.EvaluateFunctionAsync<bool>(FocusIsPasswordScript))
                {
                    throw new BrowserActionException(PasswordRefusal("The focused field"));
                }

                await PressKeysAsync(keys, request.Value);
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
                $"{node.Label} is no longer on the page. Take a new snapshot and use its refs.");
        }

        try
        {
            Locator locator = handle.AsLocator().SetTimeout(ActionTimeoutMs);
            switch (action)
            {
                case EBrowserAction.Click:
                case EBrowserAction.Upload:
                    await locator.ClickAsync();
                    return $"Clicked {node.Label}.";
                case EBrowserAction.Hover:
                    await locator.HoverAsync();
                    return $"Hovered {node.Label}.";
                case EBrowserAction.Fill:
                    // 清空不算输入密码，放行
                    if (value.Length > 0 && await IsPasswordFieldAsync(handle, node))
                    {
                        throw new BrowserActionException(PasswordRefusal(node.Label));
                    }

                    await locator.FillAsync(value);
                    return $"Filled {node.Label}.";
                case EBrowserAction.Select:
                    return await SelectAsync(handle, node, value);
                case EBrowserAction.Check:
                case EBrowserAction.Uncheck:
                    bool wanted = action == EBrowserAction.Check;
                    if (node.Checked == "true" != wanted) await locator.ClickAsync();
                    return $"{(wanted ? "Checked" : "Unchecked")} {node.Label}.";
                case EBrowserAction.Press:
                    string[] keys = BrowserKeys.Parse(value);
                    bool typesText = BrowserKeys.TypesText(keys);
                    if (typesText && await IsPasswordFieldAsync(handle, node))
                    {
                        throw new BrowserActionException(PasswordRefusal(node.Label));
                    }

                    await handle.FocusAsync();
                    // 目标聚焦不了（文字、标签）时焦点还在原处，字会打进那里：再看一眼真正的焦点
                    if (typesText && await _page.EvaluateFunctionAsync<bool>(FocusIsPasswordScript))
                    {
                        throw new BrowserActionException(PasswordRefusal("The focused field"));
                    }

                    await PressKeysAsync(keys, value);
                    return $"Pressed {value} on {node.Label}.";
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

        if (RefPattern().IsMatch(key))
        {
            return AxRefReconciler.Find(_round.Refs, _refs, key) ?? throw new BrowserActionException(
                $"No {key} in the latest snapshot. Refs are renumbered every time a snapshot is taken.");
        }

        return Lookup(key) ?? throw new BrowserActionException($"Nothing on the page shows the text \"{key}\".");
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
                $"{node.Label} is not a native drop-down: click it, then click the option in the new snapshot.");
        }

        if (values.Length == 0)
        {
            string[] labels = await handle.EvaluateFunctionAsync<string[]>(
                "el => [...el.options].map(o => o.label.trim())");
            throw new BrowserActionException(
                $"{node.Label} has no option \"{wanted}\". Options: {string.Join(" | ", labels)}");
        }

        await handle.SelectAsync(values);
        return $"Selected \"{wanted}\" in {node.Label}.";
    }

    // 组合键按住修饰键再按主键，按完松开
    private async Task PressKeysAsync(string[] names, string written)
    {
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
            throw new BrowserActionException($"Unknown key \"{written}\". Use key names such as Enter, Tab, Escape, " +
                                             "ArrowDown, Backspace, or a combination like Control+A.");
        }
        finally
        {
            for (int i = held.Count - 1; i >= 0; i--) await _page.Keyboard.UpAsync(held[i]);
        }
    }

    // 快照时已标过的直接认；没标到的（快照之后才换成密码框）当场再问一次页面
    private static async Task<bool> IsPasswordFieldAsync(IElementHandle handle, AxNode node) =>
        node.Secret || await handle.EvaluateFunctionAsync<bool>(IsPasswordScript);

    // 用户给过密码也不代填：密码只经用户的手，模型改用接管
    private static string PasswordRefusal(string field) =>
        $"{field} is a password field, and passwords are never typed for the user, even one they gave you. " +
        $"Call {BrowserTools.HandoffMention} so the user types it in the browser window themselves.";

    [GeneratedRegex(@"^e\d+$")]
    private static partial Regex RefPattern();
}
